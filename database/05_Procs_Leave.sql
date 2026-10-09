/* 05 - Leave management procedures */
USE PayrollDb;
GO
CREATE OR ALTER PROCEDURE dbo.usp_LeaveType_List
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT LeaveTypeId, Code, Name, AnnualEntitlement, AccrualMethod, MaxCarryForward, IsPaid, AllowHalfDay, MaxConsecutiveDays
    FROM dbo.LeaveType WHERE IsActive = 1 ORDER BY Code;
END
GO
/* Submit: @Days is computed by the business layer (working days minus holidays); re-validated here for balance/overlap. */
CREATE OR ALTER PROCEDURE dbo.usp_Leave_Submit
    @EmployeeId INT, @LeaveTypeId INT, @FromDate DATE, @ToDate DATE, @IsHalfDay BIT, @Days DECIMAL(5,2),
    @Reason NVARCHAR(500) = NULL, @ActingUserId INT, @NewRequestId INT OUTPUT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @IsPaid BIT, @Year SMALLINT = YEAR(@FromDate);
        SELECT @IsPaid = IsPaid FROM dbo.LeaveType WHERE LeaveTypeId = @LeaveTypeId AND IsActive = 1;
        IF @IsPaid IS NULL THROW 50001, 'Leave type not found.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Employee WHERE EmployeeId = @EmployeeId AND ExitDate IS NULL) THROW 50001, 'Employee not found or no longer active.', 1;
        IF @ToDate < @FromDate OR YEAR(@ToDate) <> @Year THROW 50005, 'A request must fall within a single calendar year.', 1;
        IF @Days <= 0 THROW 50005, 'The selected dates contain no working days.', 1;

        DECLARE @res NVARCHAR(255) = CONCAT(N'leave:', @EmployeeId);       -- serialise per employee
        EXEC sp_getapplock @Resource = @res, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;

        IF EXISTS (SELECT 1 FROM dbo.LeaveRequest WHERE EmployeeId = @EmployeeId AND [Status] IN (N'Pending', N'Approved')
                   AND FromDate <= @ToDate AND ToDate >= @FromDate)
            THROW 50004, 'The request overlaps an existing pending or approved leave.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.LeaveBalance WHERE EmployeeId = @EmployeeId AND LeaveTypeId = @LeaveTypeId AND [Year] = @Year)
            INSERT dbo.LeaveBalance(EmployeeId, LeaveTypeId, [Year]) VALUES(@EmployeeId, @LeaveTypeId, @Year);

        IF @IsPaid = 1
        BEGIN
            DECLARE @Bal DECIMAL(8,2), @Pend DECIMAL(8,2);
            SELECT @Bal = Balance FROM dbo.LeaveBalance WHERE EmployeeId = @EmployeeId AND LeaveTypeId = @LeaveTypeId AND [Year] = @Year;
            SELECT @Pend = ISNULL(SUM(Days), 0) FROM dbo.LeaveRequest
            WHERE EmployeeId = @EmployeeId AND LeaveTypeId = @LeaveTypeId AND [Status] = N'Pending'
              AND FromDate >= DATEFROMPARTS(@Year, 1, 1) AND FromDate < DATEFROMPARTS(@Year + 1, 1, 1);
            IF @Bal - @Pend < @Days THROW 50003, 'Insufficient leave balance (pending requests are reserved).', 1;
        END

        INSERT dbo.LeaveRequest(EmployeeId, LeaveTypeId, FromDate, ToDate, IsHalfDay, Days, Reason)
        VALUES(@EmployeeId, @LeaveTypeId, @FromDate, @ToDate, @IsHalfDay, @Days, @Reason);
        SET @NewRequestId = SCOPE_IDENTITY();
        DECLARE @d NVARCHAR(200) = CONCAT(N'{"days":', @Days, N',"from":"', CONVERT(NVARCHAR(10), @FromDate, 23), N'"}');
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'LeaveRequest', @EntityId = @NewRequestId, @Action = N'Submit', @Details = @d;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Leave_Decide
    @LeaveRequestId INT, @Approve BIT, @ActingUserId INT, @ApproverEmployeeId INT = NULL,
    @RestrictToManagerId INT = NULL, @Remarks NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @Emp INT, @TypeId INT, @From DATE, @To DATE, @Days DECIMAL(5,2), @Status NVARCHAR(10), @IsPaid BIT;
        SELECT @Emp = r.EmployeeId, @TypeId = r.LeaveTypeId, @From = r.FromDate, @To = r.ToDate, @Days = r.Days, @Status = r.[Status], @IsPaid = t.IsPaid
        FROM dbo.LeaveRequest r WITH (UPDLOCK, HOLDLOCK) JOIN dbo.LeaveType t ON t.LeaveTypeId = r.LeaveTypeId
        WHERE r.LeaveRequestId = @LeaveRequestId;
        IF @Emp IS NULL THROW 50001, 'Leave request not found.', 1;
        IF @Status <> N'Pending' THROW 50002, 'Only pending requests can be decided.', 1;
        IF @ApproverEmployeeId IS NOT NULL AND @ApproverEmployeeId = @Emp THROW 50021, 'You cannot approve or reject your own request.', 1;
        IF @RestrictToManagerId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Employee WHERE EmployeeId = @Emp AND ManagerId = @RestrictToManagerId)
            THROW 50020, 'You are not authorised to decide this request.', 1;

        IF @Approve = 1
        BEGIN
            IF @IsPaid = 0 AND EXISTS (SELECT 1 FROM dbo.PayrollRun WHERE [Status] IN (N'Approved', N'Paid')
                    AND DATEFROMPARTS(PeriodYear, PeriodMonth, 1) <= @To AND EOMONTH(DATEFROMPARTS(PeriodYear, PeriodMonth, 1)) >= @From)
                THROW 50002, 'Unpaid leave in a finalised payroll period cannot be approved.', 1;
            DECLARE @Year SMALLINT = YEAR(@From), @Bal DECIMAL(8,2);
            SELECT @Bal = Balance FROM dbo.LeaveBalance WITH (UPDLOCK, HOLDLOCK) WHERE EmployeeId = @Emp AND LeaveTypeId = @TypeId AND [Year] = @Year;
            IF @Bal IS NULL
            BEGIN
                INSERT dbo.LeaveBalance(EmployeeId, LeaveTypeId, [Year]) VALUES(@Emp, @TypeId, @Year);
                SET @Bal = 0;
            END
            IF @IsPaid = 1 AND @Bal < @Days THROW 50003, 'Insufficient leave balance to approve this request.', 1;
            UPDATE dbo.LeaveBalance SET Used = Used + @Days WHERE EmployeeId = @Emp AND LeaveTypeId = @TypeId AND [Year] = @Year;
            INSERT dbo.LeaveLedger(EmployeeId, LeaveTypeId, [Year], [Month], TxnType, Days, LeaveRequestId, Remarks, CreatedBy)
            VALUES(@Emp, @TypeId, @Year, MONTH(@From), N'Usage', -@Days, @LeaveRequestId, @Remarks, @ActingUserId);
        END

        UPDATE dbo.LeaveRequest
        SET [Status] = CASE WHEN @Approve = 1 THEN N'Approved' ELSE N'Rejected' END,
            DecidedBy = @ActingUserId, DecidedAt = SYSUTCDATETIME(), DecisionRemarks = @Remarks
        WHERE LeaveRequestId = @LeaveRequestId;
        DECLARE @a NVARCHAR(40) = CASE WHEN @Approve = 1 THEN N'Approve' ELSE N'Reject' END;
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'LeaveRequest', @EntityId = @LeaveRequestId, @Action = @a;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
/* @RequestingEmployeeId set => self-service cancel (ownership + not-yet-started). NULL => privileged cancel. */
CREATE OR ALTER PROCEDURE dbo.usp_Leave_Cancel @LeaveRequestId INT, @ActingUserId INT, @RequestingEmployeeId INT = NULL
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @Emp INT, @TypeId INT, @From DATE, @To DATE, @Days DECIMAL(5,2), @Status NVARCHAR(10), @IsPaid BIT;
        SELECT @Emp = r.EmployeeId, @TypeId = r.LeaveTypeId, @From = r.FromDate, @To = r.ToDate, @Days = r.Days, @Status = r.[Status], @IsPaid = t.IsPaid
        FROM dbo.LeaveRequest r WITH (UPDLOCK, HOLDLOCK) JOIN dbo.LeaveType t ON t.LeaveTypeId = r.LeaveTypeId
        WHERE r.LeaveRequestId = @LeaveRequestId;
        IF @Emp IS NULL THROW 50001, 'Leave request not found.', 1;
        IF @RequestingEmployeeId IS NOT NULL AND @RequestingEmployeeId <> @Emp THROW 50020, 'You can only cancel your own requests.', 1;
        IF @Status NOT IN (N'Pending', N'Approved') THROW 50002, 'This request can no longer be cancelled.', 1;
        IF @Status = N'Approved'
        BEGIN
            IF @RequestingEmployeeId IS NOT NULL AND @From <= CAST(SYSUTCDATETIME() AS DATE)
                THROW 50002, 'Leave that has already started can only be cancelled by HR.', 1;
            IF @IsPaid = 0 AND EXISTS (SELECT 1 FROM dbo.PayrollRun WHERE [Status] IN (N'Approved', N'Paid')
                    AND DATEFROMPARTS(PeriodYear, PeriodMonth, 1) <= @To AND EOMONTH(DATEFROMPARTS(PeriodYear, PeriodMonth, 1)) >= @From)
                THROW 50002, 'Unpaid leave in a finalised payroll period cannot be cancelled.', 1;
            DECLARE @Year SMALLINT = YEAR(@From);
            UPDATE dbo.LeaveBalance SET Used = Used - @Days WHERE EmployeeId = @Emp AND LeaveTypeId = @TypeId AND [Year] = @Year;
            INSERT dbo.LeaveLedger(EmployeeId, LeaveTypeId, [Year], [Month], TxnType, Days, LeaveRequestId, Remarks, CreatedBy)
            VALUES(@Emp, @TypeId, @Year, MONTH(@From), N'Reversal', @Days, @LeaveRequestId, N'Cancelled after approval', @ActingUserId);
        END
        UPDATE dbo.LeaveRequest SET [Status] = N'Cancelled', DecidedAt = ISNULL(DecidedAt, SYSUTCDATETIME()) WHERE LeaveRequestId = @LeaveRequestId;
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'LeaveRequest', @EntityId = @LeaveRequestId, @Action = N'Cancel';
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Leave_GetRequests
    @EmployeeId INT = NULL, @Status NVARCHAR(10) = NULL, @ManagerId INT = NULL, @PageNumber INT = 1, @PageSize INT = 25
AS
BEGIN
    SET NOCOUNT OFF;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 OR @PageSize > 200 SET @PageSize = 25;
    SELECT r.LeaveRequestId, r.EmployeeId, e.FirstName + N' ' + e.LastName AS EmployeeName, t.Name AS LeaveType, r.FromDate, r.ToDate,
           r.IsHalfDay, r.Days, r.Reason, r.[Status], r.DecisionRemarks, r.CreatedAt, COUNT(*) OVER() AS TotalCount
    FROM dbo.LeaveRequest r
    JOIN dbo.Employee e ON e.EmployeeId = r.EmployeeId
    JOIN dbo.LeaveType t ON t.LeaveTypeId = r.LeaveTypeId
    WHERE (@EmployeeId IS NULL OR r.EmployeeId = @EmployeeId) AND (@Status IS NULL OR r.[Status] = @Status) AND (@ManagerId IS NULL OR e.ManagerId = @ManagerId)
    ORDER BY CASE WHEN r.[Status] = N'Pending' THEN 0 ELSE 1 END, r.FromDate DESC, r.LeaveRequestId DESC
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Leave_GetBalances @EmployeeId INT, @Year SMALLINT
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT t.LeaveTypeId, t.Code, t.Name, t.IsPaid, @Year AS [Year],
           ISNULL(b.Opening, 0) AS Opening, ISNULL(b.Accrued, 0) AS Accrued, ISNULL(b.Adjusted, 0) AS Adjusted,
           ISNULL(b.Used, 0) AS Used, ISNULL(b.Balance, 0) AS Balance, ISNULL(p.PendingDays, 0) AS PendingDays
    FROM dbo.LeaveType t
    LEFT JOIN dbo.LeaveBalance b ON b.LeaveTypeId = t.LeaveTypeId AND b.EmployeeId = @EmployeeId AND b.[Year] = @Year
    OUTER APPLY (SELECT SUM(r.Days) AS PendingDays FROM dbo.LeaveRequest r
                 WHERE r.EmployeeId = @EmployeeId AND r.LeaveTypeId = t.LeaveTypeId AND r.[Status] = N'Pending'
                   AND r.FromDate >= DATEFROMPARTS(@Year, 1, 1) AND r.FromDate < DATEFROMPARTS(@Year + 1, 1, 1)) p
    WHERE t.IsActive = 1 ORDER BY t.Code;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Leave_GetLedger @EmployeeId INT, @Year SMALLINT
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT l.LedgerId, t.Name AS LeaveType, l.[Year], l.[Month], l.TxnType, l.Days, l.LeaveRequestId, l.Remarks, l.CreatedAt
    FROM dbo.LeaveLedger l JOIN dbo.LeaveType t ON t.LeaveTypeId = l.LeaveTypeId
    WHERE l.EmployeeId = @EmployeeId AND l.[Year] = @Year ORDER BY l.LedgerId DESC;
END
GO
/* Idempotent monthly accrual (unique filtered index UX_Ledger_Accrual + NOT EXISTS guard). Set-based: one statement for all employees. */
CREATE OR ALTER PROCEDURE dbo.usp_Leave_RunMonthlyAccrual @Year SMALLINT, @Month TINYINT, @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        IF @Month NOT BETWEEN 1 AND 12 THROW 50005, 'Invalid month.', 1;
        DECLARE @P DATE = DATEFROMPARTS(@Year, @Month, 1), @E DATE = EOMONTH(DATEFROMPARTS(@Year, @Month, 1));
        DECLARE @New TABLE(EmployeeId INT, LeaveTypeId INT, Days DECIMAL(6,2));
        INSERT dbo.LeaveLedger(EmployeeId, LeaveTypeId, [Year], [Month], TxnType, Days, Remarks, CreatedBy)
        OUTPUT inserted.EmployeeId, inserted.LeaveTypeId, inserted.Days INTO @New
        SELECT s.EmployeeId, s.LeaveTypeId, @Year, @Month, N'Accrual', s.Days, N'Automatic accrual', @ActingUserId
        FROM (SELECT e.EmployeeId, t.LeaveTypeId,
                     CAST(CASE t.AccrualMethod
                        WHEN N'Monthly' THEN CASE WHEN e.JoinDate <= DATEFROMPARTS(@Year, @Month, 15) THEN ROUND(t.AnnualEntitlement / 12.0, 2) ELSE 0 END
                        WHEN N'Annual'  THEN CASE WHEN @Month = 1 AND e.JoinDate < @P THEN t.AnnualEntitlement
                                                  WHEN e.JoinDate >= @P AND e.JoinDate <= @E THEN ROUND(t.AnnualEntitlement * (13 - @Month) / 12.0 * 2, 0) / 2.0
                                                  ELSE 0 END
                        ELSE 0 END AS DECIMAL(6,2)) AS Days
              FROM dbo.Employee e CROSS JOIN dbo.LeaveType t
              WHERE t.IsActive = 1 AND e.JoinDate <= @E AND (e.ExitDate IS NULL OR e.ExitDate >= @P)) s
        WHERE s.Days > 0
          AND NOT EXISTS (SELECT 1 FROM dbo.LeaveLedger l WHERE l.EmployeeId = s.EmployeeId AND l.LeaveTypeId = s.LeaveTypeId
                          AND l.[Year] = @Year AND l.[Month] = @Month AND l.TxnType = N'Accrual');

        INSERT dbo.LeaveBalance(EmployeeId, LeaveTypeId, [Year])
        SELECT n.EmployeeId, n.LeaveTypeId, @Year FROM @New n
        WHERE NOT EXISTS (SELECT 1 FROM dbo.LeaveBalance b WHERE b.EmployeeId = n.EmployeeId AND b.LeaveTypeId = n.LeaveTypeId AND b.[Year] = @Year);
        UPDATE b SET Accrued = b.Accrued + n.Days
        FROM dbo.LeaveBalance b JOIN @New n ON n.EmployeeId = b.EmployeeId AND n.LeaveTypeId = b.LeaveTypeId AND b.[Year] = @Year;

        DECLARE @Posted INT = (SELECT COUNT(*) FROM @New);
        DECLARE @d NVARCHAR(100) = CONCAT(N'{"year":', @Year, N',"month":', @Month, N',"posted":', @Posted, N'}');
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'LeaveAccrual', @EntityId = NULL, @Action = N'Run', @Details = @d;
        COMMIT;
        SELECT @Posted AS PostedCount;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Leave_YearEndCarryForward @FromYear SMALLINT, @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @To SMALLINT = @FromYear + 1;
        DECLARE @New TABLE(EmployeeId INT, LeaveTypeId INT, Days DECIMAL(6,2));
        INSERT dbo.LeaveLedger(EmployeeId, LeaveTypeId, [Year], [Month], TxnType, Days, Remarks, CreatedBy)
        OUTPUT inserted.EmployeeId, inserted.LeaveTypeId, inserted.Days INTO @New
        SELECT b.EmployeeId, b.LeaveTypeId, @To, NULL, N'CarryForward',
               CASE WHEN b.Balance < t.MaxCarryForward THEN b.Balance ELSE t.MaxCarryForward END,
               N'Carry forward from ' + CAST(@FromYear AS NVARCHAR(4)), @ActingUserId
        FROM dbo.LeaveBalance b
        JOIN dbo.LeaveType t ON t.LeaveTypeId = b.LeaveTypeId
        JOIN dbo.Employee e ON e.EmployeeId = b.EmployeeId AND e.ExitDate IS NULL
        WHERE b.[Year] = @FromYear AND b.Balance > 0 AND t.MaxCarryForward > 0
          AND NOT EXISTS (SELECT 1 FROM dbo.LeaveLedger l WHERE l.EmployeeId = b.EmployeeId AND l.LeaveTypeId = b.LeaveTypeId
                          AND l.[Year] = @To AND l.TxnType = N'CarryForward');
        INSERT dbo.LeaveBalance(EmployeeId, LeaveTypeId, [Year])
        SELECT n.EmployeeId, n.LeaveTypeId, @To FROM @New n
        WHERE NOT EXISTS (SELECT 1 FROM dbo.LeaveBalance b WHERE b.EmployeeId = n.EmployeeId AND b.LeaveTypeId = n.LeaveTypeId AND b.[Year] = @To);
        UPDATE b SET Opening = b.Opening + n.Days
        FROM dbo.LeaveBalance b JOIN @New n ON n.EmployeeId = b.EmployeeId AND n.LeaveTypeId = b.LeaveTypeId AND b.[Year] = @To;
        DECLARE @Posted INT = (SELECT COUNT(*) FROM @New);
        DECLARE @d NVARCHAR(100) = CONCAT(N'{"fromYear":', @FromYear, N',"posted":', @Posted, N'}');
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'LeaveCarryForward', @EntityId = NULL, @Action = N'Run', @Details = @d;
        COMMIT;
        SELECT @Posted AS PostedCount;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Leave_Adjust
    @EmployeeId INT, @LeaveTypeId INT, @Year SMALLINT, @Days DECIMAL(6,2), @Remarks NVARCHAR(300), @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        IF @Days = 0 THROW 50005, 'Adjustment cannot be zero.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Employee WHERE EmployeeId = @EmployeeId) THROW 50001, 'Employee not found.', 1;
        DECLARE @IsPaid BIT;
        SELECT @IsPaid = IsPaid FROM dbo.LeaveType WHERE LeaveTypeId = @LeaveTypeId;
        IF @IsPaid IS NULL THROW 50001, 'Leave type not found.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.LeaveBalance WHERE EmployeeId = @EmployeeId AND LeaveTypeId = @LeaveTypeId AND [Year] = @Year)
            INSERT dbo.LeaveBalance(EmployeeId, LeaveTypeId, [Year]) VALUES(@EmployeeId, @LeaveTypeId, @Year);
        DECLARE @Bal DECIMAL(8,2);
        SELECT @Bal = Balance FROM dbo.LeaveBalance WITH (UPDLOCK, HOLDLOCK) WHERE EmployeeId = @EmployeeId AND LeaveTypeId = @LeaveTypeId AND [Year] = @Year;
        IF @IsPaid = 1 AND @Bal + @Days < 0 THROW 50003, 'Adjustment would make the balance negative.', 1;
        UPDATE dbo.LeaveBalance SET Adjusted = Adjusted + @Days WHERE EmployeeId = @EmployeeId AND LeaveTypeId = @LeaveTypeId AND [Year] = @Year;
        INSERT dbo.LeaveLedger(EmployeeId, LeaveTypeId, [Year], TxnType, Days, Remarks, CreatedBy)
        VALUES(@EmployeeId, @LeaveTypeId, @Year, N'Adjustment', @Days, @Remarks, @ActingUserId);
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'LeaveBalance', @EntityId = @EmployeeId, @Action = N'Adjust', @Details = @Remarks;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
