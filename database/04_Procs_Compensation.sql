/* 04 - Salary, components, overtime, loans, holidays */
USE PayrollDb;
GO
/* ============================ Salary structure ============================ */
CREATE OR ALTER PROCEDURE dbo.usp_Salary_SetStructure
    @EmployeeId INT, @EffectiveFrom DATE, @BasicSalary DECIMAL(18,2), @StandardHoursPerMonth DECIMAL(6,2),
    @OvertimeEligible BIT, @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        IF NOT EXISTS (SELECT 1 FROM dbo.Employee WHERE EmployeeId = @EmployeeId) THROW 50001, 'Employee not found.', 1;
        IF @BasicSalary <= 0 THROW 50005, 'Basic salary must be greater than zero.', 1;
        DECLARE @CurId INT, @CurFrom DATE;
        SELECT @CurId = SalaryStructureId, @CurFrom = EffectiveFrom
        FROM dbo.SalaryStructure WITH (UPDLOCK, HOLDLOCK) WHERE EmployeeId = @EmployeeId AND EffectiveTo IS NULL;
        IF @CurId IS NOT NULL
        BEGIN
            IF @EffectiveFrom <= @CurFrom THROW 50005, 'The new effective date must be later than the current structure''s effective date.', 1;
            IF EXISTS (SELECT 1 FROM dbo.PayrollRun WHERE [Status] IN (N'Approved', N'Paid')
                       AND EOMONTH(DATEFROMPARTS(PeriodYear, PeriodMonth, 1)) >= @EffectiveFrom)
                THROW 50002, 'Cannot change salary with an effective date inside a finalised payroll period.', 1;
            UPDATE dbo.SalaryStructure SET EffectiveTo = DATEADD(DAY, -1, @EffectiveFrom) WHERE SalaryStructureId = @CurId;
        END
        INSERT dbo.SalaryStructure(EmployeeId, EffectiveFrom, BasicSalary, StandardHoursPerMonth, OvertimeEligible, CreatedBy)
        VALUES(@EmployeeId, @EffectiveFrom, @BasicSalary, @StandardHoursPerMonth, @OvertimeEligible, @ActingUserId);
        DECLARE @d NVARCHAR(200) = CONCAT(N'{"from":"', CONVERT(NVARCHAR(10), @EffectiveFrom, 23), N'","basic":', @BasicSalary, N'}');
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'SalaryStructure', @EntityId = @EmployeeId, @Action = N'Set', @Details = @d;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Salary_GetHistory @EmployeeId INT
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT SalaryStructureId AS Id, EmployeeId, EffectiveFrom, EffectiveTo, BasicSalary, StandardHoursPerMonth, OvertimeEligible
    FROM dbo.SalaryStructure WHERE EmployeeId = @EmployeeId ORDER BY EffectiveFrom DESC;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Component_List
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT ComponentId, Code, Name, ComponentType, CalcMethod, IsTaxable FROM dbo.SalaryComponent WHERE IsActive = 1 ORDER BY ComponentType DESC, Code;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_EmployeeComponent_Assign
    @EmployeeId INT, @ComponentId INT, @Value DECIMAL(18,4), @EffectiveFrom DATE, @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @Calc NVARCHAR(20);
        SELECT @Calc = CalcMethod FROM dbo.SalaryComponent WHERE ComponentId = @ComponentId AND IsActive = 1;
        IF @Calc IS NULL THROW 50001, 'Component not found.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Employee WHERE EmployeeId = @EmployeeId) THROW 50001, 'Employee not found.', 1;
        IF @Value <= 0 OR (@Calc = N'PercentOfBasic' AND @Value > 100) THROW 50005, 'Value is out of range.', 1;
        DECLARE @CurId INT, @CurFrom DATE;
        SELECT @CurId = EmployeeComponentId, @CurFrom = EffectiveFrom
        FROM dbo.EmployeeComponent WITH (UPDLOCK, HOLDLOCK) WHERE EmployeeId = @EmployeeId AND ComponentId = @ComponentId AND EffectiveTo IS NULL;
        IF @CurId IS NOT NULL
        BEGIN
            IF @EffectiveFrom <= @CurFrom THROW 50005, 'The new effective date must be later than the current assignment''s date.', 1;
            UPDATE dbo.EmployeeComponent SET EffectiveTo = DATEADD(DAY, -1, @EffectiveFrom) WHERE EmployeeComponentId = @CurId;
        END
        INSERT dbo.EmployeeComponent(EmployeeId, ComponentId, [Value], EffectiveFrom, CreatedBy) VALUES(@EmployeeId, @ComponentId, @Value, @EffectiveFrom, @ActingUserId);
        DECLARE @d NVARCHAR(200) = CONCAT(N'{"componentId":', @ComponentId, N',"value":', @Value, N'}');
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'EmployeeComponent', @EntityId = @EmployeeId, @Action = N'Assign', @Details = @d;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_EmployeeComponent_End @EmployeeComponentId INT, @EndDate DATE, @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF;
    UPDATE dbo.EmployeeComponent SET EffectiveTo = @EndDate
    WHERE EmployeeComponentId = @EmployeeComponentId AND EffectiveTo IS NULL AND EffectiveFrom <= @EndDate;
    IF @@ROWCOUNT = 0 THROW 50002, 'Assignment not found, already ended, or end date precedes its start.', 1;
    EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'EmployeeComponent', @EntityId = @EmployeeComponentId, @Action = N'End';
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_EmployeeComponent_GetByEmployee @EmployeeId INT
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT ec.EmployeeComponentId, ec.ComponentId, c.Code, c.Name, c.ComponentType, c.CalcMethod, ec.[Value], ec.EffectiveFrom, ec.EffectiveTo
    FROM dbo.EmployeeComponent ec JOIN dbo.SalaryComponent c ON c.ComponentId = ec.ComponentId
    WHERE ec.EmployeeId = @EmployeeId ORDER BY ec.EffectiveTo, c.Code, ec.EffectiveFrom DESC;
END
GO
/* ============================ Overtime ============================ */
CREATE OR ALTER PROCEDURE dbo.usp_Overtime_Submit
    @EmployeeId INT, @WorkDate DATE, @Hours DECIMAL(5,2), @Multiplier DECIMAL(4,2), @Remarks NVARCHAR(300) = NULL,
    @ActingUserId INT, @NewOvertimeId INT OUTPUT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        IF NOT EXISTS (SELECT 1 FROM dbo.Employee WHERE EmployeeId = @EmployeeId AND ExitDate IS NULL) THROW 50001, 'Employee not found or no longer active.', 1;
        IF @WorkDate > CAST(SYSUTCDATETIME() AS DATE) THROW 50005, 'Overtime cannot be logged for a future date.', 1;
        DECLARE @res NVARCHAR(255) = CONCAT(N'ot:', @EmployeeId, N':', CONVERT(NVARCHAR(10), @WorkDate, 23));
        EXEC sp_getapplock @Resource = @res, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
        IF (SELECT ISNULL(SUM(Hours), 0) FROM dbo.OvertimeEntry WHERE EmployeeId = @EmployeeId AND WorkDate = @WorkDate AND [Status] <> N'Rejected') + @Hours > 16
            THROW 50005, 'Total overtime for one day cannot exceed 16 hours.', 1;
        IF EXISTS (SELECT 1 FROM dbo.PayrollRun WHERE PeriodYear = YEAR(@WorkDate) AND PeriodMonth = MONTH(@WorkDate) AND [Status] IN (N'Approved', N'Paid'))
            THROW 50002, 'Payroll for that month is already finalised.', 1;
        INSERT dbo.OvertimeEntry(EmployeeId, WorkDate, Hours, Multiplier, Remarks) VALUES(@EmployeeId, @WorkDate, @Hours, @Multiplier, @Remarks);
        SET @NewOvertimeId = SCOPE_IDENTITY();
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'OvertimeEntry', @EntityId = @NewOvertimeId, @Action = N'Submit';
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Overtime_Decide
    @OvertimeId INT, @Approve BIT, @ActingUserId INT, @ApproverEmployeeId INT = NULL, @RestrictToManagerId INT = NULL, @Remarks NVARCHAR(300) = NULL
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @Emp INT, @Date DATE, @Status NVARCHAR(10);
        SELECT @Emp = EmployeeId, @Date = WorkDate, @Status = [Status] FROM dbo.OvertimeEntry WITH (UPDLOCK, HOLDLOCK) WHERE OvertimeId = @OvertimeId;
        IF @Emp IS NULL THROW 50001, 'Overtime entry not found.', 1;
        IF @Status <> N'Pending' THROW 50002, 'Only pending entries can be decided.', 1;
        IF @ApproverEmployeeId IS NOT NULL AND @ApproverEmployeeId = @Emp THROW 50021, 'You cannot approve or reject your own overtime.', 1;
        IF @RestrictToManagerId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Employee WHERE EmployeeId = @Emp AND ManagerId = @RestrictToManagerId)
            THROW 50020, 'You are not authorised to decide this entry.', 1;
        IF EXISTS (SELECT 1 FROM dbo.PayrollRun WHERE PeriodYear = YEAR(@Date) AND PeriodMonth = MONTH(@Date) AND [Status] IN (N'Approved', N'Paid'))
            THROW 50002, 'Payroll for that month is already finalised.', 1;
        UPDATE dbo.OvertimeEntry SET [Status] = CASE WHEN @Approve = 1 THEN N'Approved' ELSE N'Rejected' END,
               DecidedBy = @ActingUserId, DecidedAt = SYSUTCDATETIME(), Remarks = ISNULL(@Remarks, Remarks) WHERE OvertimeId = @OvertimeId;
        DECLARE @a NVARCHAR(40) = CASE WHEN @Approve = 1 THEN N'Approve' ELSE N'Reject' END;
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'OvertimeEntry', @EntityId = @OvertimeId, @Action = @a;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Overtime_List
    @Status NVARCHAR(10) = NULL, @EmployeeId INT = NULL, @ManagerId INT = NULL, @PageNumber INT = 1, @PageSize INT = 25
AS
BEGIN
    SET NOCOUNT OFF;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 OR @PageSize > 200 SET @PageSize = 25;
    SELECT o.OvertimeId, o.EmployeeId, e.FirstName + N' ' + e.LastName AS EmployeeName, o.WorkDate, o.Hours, o.Multiplier, o.[Status], o.Remarks,
           COUNT(*) OVER() AS TotalCount
    FROM dbo.OvertimeEntry o JOIN dbo.Employee e ON e.EmployeeId = o.EmployeeId
    WHERE (@Status IS NULL OR o.[Status] = @Status) AND (@EmployeeId IS NULL OR o.EmployeeId = @EmployeeId) AND (@ManagerId IS NULL OR e.ManagerId = @ManagerId)
    ORDER BY o.WorkDate DESC, o.OvertimeId DESC OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO
/* ============================ Loans ============================ */
CREATE OR ALTER PROCEDURE dbo.usp_Loan_Create
    @EmployeeId INT, @LoanType NVARCHAR(40), @Principal DECIMAL(18,2), @Installment DECIMAL(18,2), @StartDate DATE,
    @ActingUserId INT, @NewLoanId INT OUTPUT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        IF NOT EXISTS (SELECT 1 FROM dbo.Employee WHERE EmployeeId = @EmployeeId AND ExitDate IS NULL) THROW 50001, 'Employee not found or no longer active.', 1;
        IF @Principal <= 0 OR @Installment <= 0 OR @Installment > @Principal THROW 50005, 'Installment must be positive and not exceed the principal.', 1;
        INSERT dbo.Loan(EmployeeId, LoanType, PrincipalAmount, InstallmentAmount, OutstandingBalance, StartDate, CreatedBy)
        VALUES(@EmployeeId, @LoanType, @Principal, @Installment, @Principal, @StartDate, @ActingUserId);
        SET @NewLoanId = SCOPE_IDENTITY();
        DECLARE @d NVARCHAR(200) = CONCAT(N'{"employeeId":', @EmployeeId, N',"principal":', @Principal, N'}');
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'Loan', @EntityId = @NewLoanId, @Action = N'Create', @Details = @d;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Loan_List @EmployeeId INT = NULL, @Status NVARCHAR(10) = NULL
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT l.LoanId, l.EmployeeId, e.FirstName + N' ' + e.LastName AS EmployeeName, l.LoanType, l.PrincipalAmount, l.InstallmentAmount,
           l.OutstandingBalance, l.StartDate, l.[Status]
    FROM dbo.Loan l JOIN dbo.Employee e ON e.EmployeeId = l.EmployeeId
    WHERE (@EmployeeId IS NULL OR l.EmployeeId = @EmployeeId) AND (@Status IS NULL OR l.[Status] = @Status)
    ORDER BY l.LoanId DESC;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Loan_Cancel @LoanId INT, @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF;
    UPDATE dbo.Loan SET [Status] = N'Cancelled' WHERE LoanId = @LoanId AND [Status] = N'Active';
    IF @@ROWCOUNT = 0 THROW 50002, 'Only active loans can be cancelled.', 1;
    EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'Loan', @EntityId = @LoanId, @Action = N'Cancel';
END
GO
/* ============================ Holidays ============================ */
CREATE OR ALTER PROCEDURE dbo.usp_Holiday_GetRange @From DATE, @To DATE
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT HolidayDate, Name FROM dbo.Holiday WHERE HolidayDate BETWEEN @From AND @To ORDER BY HolidayDate;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Holiday_Upsert @HolidayDate DATE, @Name NVARCHAR(100), @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    UPDATE dbo.Holiday SET Name = @Name WHERE HolidayDate = @HolidayDate;
    IF @@ROWCOUNT = 0 INSERT dbo.Holiday(HolidayDate, Name) VALUES(@HolidayDate, @Name);
    DECLARE @id NVARCHAR(10) = CONVERT(NVARCHAR(10), @HolidayDate, 23);
    EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'Holiday', @EntityId = @id, @Action = N'Upsert';
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Holiday_Delete @HolidayDate DATE, @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF;
    IF EXISTS (SELECT 1 FROM dbo.PayrollRun WHERE PeriodYear = YEAR(@HolidayDate) AND PeriodMonth = MONTH(@HolidayDate) AND [Status] IN (N'Approved', N'Paid'))
        THROW 50002, 'Payroll for that month is already finalised.', 1;
    DELETE FROM dbo.Holiday WHERE HolidayDate = @HolidayDate;
    DECLARE @id NVARCHAR(10) = CONVERT(NVARCHAR(10), @HolidayDate, 23);
    EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'Holiday', @EntityId = @id, @Action = N'Delete';
END
GO
