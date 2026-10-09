/* 06 - Payroll run lifecycle: Open -> Processed -> Approved -> Paid */
USE PayrollDb;
GO
CREATE OR ALTER PROCEDURE dbo.usp_Payroll_CreateRun @Year SMALLINT, @Month TINYINT, @ActingUserId INT, @NewRunId INT OUTPUT
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    IF @Month NOT BETWEEN 1 AND 12 OR @Year NOT BETWEEN 2000 AND 2100 THROW 50005, 'Invalid payroll period.', 1;
    IF EXISTS (SELECT 1 FROM dbo.PayrollRun WHERE PeriodYear = @Year AND PeriodMonth = @Month) THROW 50004, 'A payroll run already exists for that period.', 1;
    INSERT dbo.PayrollRun(PeriodYear, PeriodMonth, CreatedBy) VALUES(@Year, @Month, @ActingUserId);
    SET @NewRunId = SCOPE_IDENTITY();
    EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'PayrollRun', @EntityId = @NewRunId, @Action = N'Create';
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Payroll_GetRuns @Top INT = 60
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT TOP (@Top) PayrollRunId, PeriodYear, PeriodMonth, [Status], EmployeeCount, TotalGross, TotalDeductions, TotalNet, TotalEmployerCost,
           ProcessedAt, ApprovedAt, PaidAt
    FROM dbo.PayrollRun ORDER BY PeriodYear DESC, PeriodMonth DESC;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Payroll_GetRun @RunId INT
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT PayrollRunId, PeriodYear, PeriodMonth, [Status], EmployeeCount, TotalGross, TotalDeductions, TotalNet, TotalEmployerCost,
           ProcessedAt, ApprovedAt, PaidAt
    FROM dbo.PayrollRun WHERE PayrollRunId = @RunId;
END
GO
/* One round trip returns every input the calculator needs (10 result sets) - no per-employee queries. */
CREATE OR ALTER PROCEDURE dbo.usp_Payroll_GetCalculationInputs @RunId INT, @FyStartMonth TINYINT = 4
AS
BEGIN
    SET NOCOUNT OFF;
    DECLARE @Year SMALLINT, @Month TINYINT, @Status NVARCHAR(10);
    SELECT @Year = PeriodYear, @Month = PeriodMonth, @Status = [Status] FROM dbo.PayrollRun WHERE PayrollRunId = @RunId;
    IF @Year IS NULL THROW 50001, 'Payroll run not found.', 1;
    DECLARE @First DATE = DATEFROMPARTS(@Year, @Month, 1), @Last DATE = EOMONTH(DATEFROMPARTS(@Year, @Month, 1));
    DECLARE @FY INT = CASE WHEN @Month >= @FyStartMonth THEN @Year ELSE @Year - 1 END;

    SELECT e.EmployeeId, e.EmployeeCode, e.FirstName + N' ' + e.LastName AS FullName, e.DepartmentId, e.JoinDate, e.ExitDate,
           CASE WHEN e.ExitDate IS NOT NULL AND e.ExitDate < @Last THEN e.ExitDate ELSE @Last END AS RefDate
    INTO #Emp
    FROM dbo.Employee e WHERE e.JoinDate <= @Last AND (e.ExitDate IS NULL OR e.ExitDate >= @First);
    CREATE UNIQUE CLUSTERED INDEX IX_Emp ON #Emp(EmployeeId);

    -- 1 run
    SELECT @RunId AS PayrollRunId, @Year AS PeriodYear, @Month AS PeriodMonth, @Status AS [Status], @FY AS FinancialYear;
    -- 2 employees + salary structure effective at the reference date
    SELECT x.EmployeeId, x.EmployeeCode, x.FullName, x.DepartmentId, x.JoinDate, x.ExitDate,
           s.BasicSalary, s.StandardHoursPerMonth, s.OvertimeEligible
    FROM #Emp x
    OUTER APPLY (SELECT TOP (1) BasicSalary, StandardHoursPerMonth, OvertimeEligible FROM dbo.SalaryStructure s
                 WHERE s.EmployeeId = x.EmployeeId AND s.EffectiveFrom <= x.RefDate AND (s.EffectiveTo IS NULL OR s.EffectiveTo >= x.RefDate)
                 ORDER BY s.EffectiveFrom DESC) s
    ORDER BY x.EmployeeId;
    -- 3 components
    SELECT ec.EmployeeId, c.Code, c.Name, c.ComponentType, c.CalcMethod, c.IsTaxable, ec.[Value]
    FROM #Emp x
    JOIN dbo.EmployeeComponent ec ON ec.EmployeeId = x.EmployeeId AND ec.EffectiveFrom <= x.RefDate AND (ec.EffectiveTo IS NULL OR ec.EffectiveTo >= x.RefDate)
    JOIN dbo.SalaryComponent c ON c.ComponentId = ec.ComponentId AND c.IsActive = 1
    ORDER BY ec.EmployeeId, c.ComponentType DESC, c.Code;
    -- 4 approved overtime in period
    SELECT o.EmployeeId, SUM(o.Hours) AS Hours, SUM(o.Hours * o.Multiplier) AS WeightedHours
    FROM dbo.OvertimeEntry o JOIN #Emp x ON x.EmployeeId = o.EmployeeId
    WHERE o.[Status] = N'Approved' AND o.WorkDate BETWEEN @First AND @Last GROUP BY o.EmployeeId;
    -- 5 approved unpaid leave overlapping the period
    SELECT r.EmployeeId, r.FromDate, r.ToDate, r.IsHalfDay, r.Days
    FROM dbo.LeaveRequest r JOIN dbo.LeaveType t ON t.LeaveTypeId = r.LeaveTypeId JOIN #Emp x ON x.EmployeeId = r.EmployeeId
    WHERE r.[Status] = N'Approved' AND t.IsPaid = 0 AND r.FromDate <= @Last AND r.ToDate >= @First;
    -- 6 holidays
    SELECT HolidayDate FROM dbo.Holiday WHERE HolidayDate BETWEEN @First AND @Last;
    -- 7 active loans
    SELECT l.LoanId, l.EmployeeId, l.LoanType, l.InstallmentAmount, l.OutstandingBalance
    FROM dbo.Loan l JOIN #Emp x ON x.EmployeeId = l.EmployeeId
    WHERE l.[Status] = N'Active' AND l.StartDate <= @Last AND l.OutstandingBalance > 0 ORDER BY l.EmployeeId, l.LoanId;
    -- 8 statutory rules effective at period end
    SELECT Code, Name, EmployeeRatePercent, EmployerRatePercent, WageCeiling, BaseOn, IsPreTax
    FROM dbo.StatutoryRule WHERE IsActive = 1 AND EffectiveFrom <= @Last AND (EffectiveTo IS NULL OR EffectiveTo >= @Last) ORDER BY Code;
    -- 9 tax slabs
    SELECT FromAmount, ToAmount, RatePercent FROM dbo.TaxSlab WHERE FinancialYear = @FY ORDER BY FromAmount;
    -- 10 tax parameters
    SELECT AnnualStandardDeduction, CessPercent FROM dbo.TaxParameter WHERE FinancialYear = @FY;
END
GO
/* Atomically replaces all payslips of a run using two TVPs (bulk insert, no row-by-row calls). */
CREATE OR ALTER PROCEDURE dbo.usp_Payroll_SavePayslips
    @RunId INT, @ActingUserId INT, @Payslips dbo.PayslipTableType READONLY, @Lines dbo.PayslipLineTableType READONLY
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @Status NVARCHAR(10);
        SELECT @Status = [Status] FROM dbo.PayrollRun WITH (UPDLOCK, HOLDLOCK) WHERE PayrollRunId = @RunId;
        IF @Status IS NULL THROW 50001, 'Payroll run not found.', 1;
        IF @Status NOT IN (N'Open', N'Processed') THROW 50002, 'A finalised payroll run cannot be re-processed.', 1;

        DELETE pl FROM dbo.PayslipLine pl JOIN dbo.Payslip p ON p.PayslipId = pl.PayslipId WHERE p.PayrollRunId = @RunId;
        DELETE FROM dbo.Payslip WHERE PayrollRunId = @RunId;

        INSERT dbo.Payslip(PayrollRunId, EmployeeId, DepartmentId, BasicSalary, CalendarDays, WorkingDays, PaidDays, LopDays, OvertimeHours,
                           OvertimePay, GrossEarnings, TaxableIncome, TotalDeductions, NetPay, EmployerContributions)
        SELECT @RunId, EmployeeId, DepartmentId, BasicSalary, CalendarDays, WorkingDays, PaidDays, LopDays, OvertimeHours,
               OvertimePay, GrossEarnings, TaxableIncome, TotalDeductions, NetPay, EmployerContributions
        FROM @Payslips;

        INSERT dbo.PayslipLine(PayslipId, LineType, Code, [Description], Amount, IsStatutory, ReferenceId, SortOrder)
        SELECT p.PayslipId, l.LineType, l.Code, l.[Description], l.Amount, l.IsStatutory, l.ReferenceId, l.SortOrder
        FROM @Lines l JOIN dbo.Payslip p ON p.PayrollRunId = @RunId AND p.EmployeeId = l.EmployeeId;

        UPDATE dbo.PayrollRun
        SET [Status] = N'Processed',
            EmployeeCount = (SELECT COUNT(*) FROM @Payslips),
            TotalGross = ISNULL((SELECT SUM(GrossEarnings) FROM @Payslips), 0),
            TotalDeductions = ISNULL((SELECT SUM(TotalDeductions) FROM @Payslips), 0),
            TotalNet = ISNULL((SELECT SUM(NetPay) FROM @Payslips), 0),
            TotalEmployerCost = ISNULL((SELECT SUM(EmployerContributions) FROM @Payslips), 0),
            ProcessedBy = @ActingUserId, ProcessedAt = SYSUTCDATETIME()
        WHERE PayrollRunId = @RunId;

        DECLARE @n INT = (SELECT COUNT(*) FROM @Payslips);
        DECLARE @d NVARCHAR(100) = CONCAT(N'{"employees":', @n, N'}');
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'PayrollRun', @EntityId = @RunId, @Action = N'Process', @Details = @d;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Payroll_Approve @RunId INT, @ActingUserId INT, @EnforceSeparation BIT = 1
AS
BEGIN
    SET NOCOUNT OFF; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @Status NVARCHAR(10), @ProcessedBy INT;
        SELECT @Status = [Status], @ProcessedBy = ProcessedBy FROM dbo.PayrollRun WITH (UPDLOCK, HOLDLOCK) WHERE PayrollRunId = @RunId;
        IF @Status IS NULL THROW 50001, 'Payroll run not found.', 1;
        IF @Status <> N'Processed' THROW 50002, 'Only a processed run can be approved.', 1;
        IF @EnforceSeparation = 1 AND @ProcessedBy = @ActingUserId THROW 50022, 'Separation of duties: a different user must approve a run you processed.', 1;

        -- post loan recoveries and reduce outstanding balances
        INSERT dbo.LoanRepayment(LoanId, PayrollRunId, Amount)
        SELECT l.ReferenceId, @RunId, SUM(l.Amount)
        FROM dbo.PayslipLine l JOIN dbo.Payslip p ON p.PayslipId = l.PayslipId
        WHERE p.PayrollRunId = @RunId AND l.Code = N'LOAN' AND l.ReferenceId IS NOT NULL
        GROUP BY l.ReferenceId HAVING SUM(l.Amount) > 0;

        UPDATE ln
        SET OutstandingBalance = ln.OutstandingBalance - r.Amount,
            [Status] = CASE WHEN ln.OutstandingBalance - r.Amount <= 0 THEN N'Closed' ELSE ln.[Status] END
        FROM dbo.Loan ln JOIN dbo.LoanRepayment r ON r.LoanId = ln.LoanId AND r.PayrollRunId = @RunId;

        UPDATE dbo.PayrollRun SET [Status] = N'Approved', ApprovedBy = @ActingUserId, ApprovedAt = SYSUTCDATETIME() WHERE PayrollRunId = @RunId;
        EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'PayrollRun', @EntityId = @RunId, @Action = N'Approve';
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Payroll_MarkPaid @RunId INT, @ActingUserId INT
AS
BEGIN
    SET NOCOUNT OFF;
    UPDATE dbo.PayrollRun SET [Status] = N'Paid', PaidBy = @ActingUserId, PaidAt = SYSUTCDATETIME() WHERE PayrollRunId = @RunId AND [Status] = N'Approved';
    IF @@ROWCOUNT = 0 THROW 50002, 'Only an approved run can be marked as paid.', 1;
    EXEC dbo.usp_Audit_Write @UserId = @ActingUserId, @EntityName = N'PayrollRun', @EntityId = @RunId, @Action = N'MarkPaid';
END
GO
/* Employees (@RequestingEmployeeId set) only see their own payslips and only after approval. */
CREATE OR ALTER PROCEDURE dbo.usp_Payslip_Get @PayslipId INT, @RequestingEmployeeId INT = NULL
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT p.PayslipId, p.PayrollRunId, r.PeriodYear, r.PeriodMonth, r.[Status] AS RunStatus, p.EmployeeId, e.EmployeeCode,
           e.FirstName + N' ' + e.LastName AS FullName, d.Name AS Department, g.Title AS Designation, e.TaxIdNumber, e.BankName,
           CASE WHEN LEN(e.BankAccountNumber) > 4 THEN REPLICATE(N'*', LEN(e.BankAccountNumber) - 4) + RIGHT(e.BankAccountNumber, 4) ELSE e.BankAccountNumber END AS BankAccountMasked,
           e.JoinDate, p.BasicSalary, p.CalendarDays, p.WorkingDays, p.PaidDays, p.LopDays, p.OvertimeHours, p.OvertimePay,
           p.GrossEarnings, p.TaxableIncome, p.TotalDeductions, p.NetPay, p.EmployerContributions
    FROM dbo.Payslip p
    JOIN dbo.PayrollRun r ON r.PayrollRunId = p.PayrollRunId
    JOIN dbo.Employee e ON e.EmployeeId = p.EmployeeId
    JOIN dbo.Department d ON d.DepartmentId = p.DepartmentId
    JOIN dbo.Designation g ON g.DesignationId = e.DesignationId
    WHERE p.PayslipId = @PayslipId
      AND (@RequestingEmployeeId IS NULL OR (p.EmployeeId = @RequestingEmployeeId AND r.[Status] IN (N'Approved', N'Paid')));

    SELECT l.LineType, l.Code, l.[Description], l.Amount, l.IsStatutory
    FROM dbo.PayslipLine l JOIN dbo.Payslip p ON p.PayslipId = l.PayslipId JOIN dbo.PayrollRun r ON r.PayrollRunId = p.PayrollRunId
    WHERE l.PayslipId = @PayslipId
      AND (@RequestingEmployeeId IS NULL OR (p.EmployeeId = @RequestingEmployeeId AND r.[Status] IN (N'Approved', N'Paid')))
    ORDER BY l.SortOrder, l.PayslipLineId;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Payslip_ListByEmployee @EmployeeId INT, @OnlyFinalised BIT = 1
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT p.PayslipId, r.PeriodYear, r.PeriodMonth, p.GrossEarnings, p.TotalDeductions, p.NetPay, r.[Status] AS RunStatus
    FROM dbo.Payslip p JOIN dbo.PayrollRun r ON r.PayrollRunId = p.PayrollRunId
    WHERE p.EmployeeId = @EmployeeId AND (@OnlyFinalised = 0 OR r.[Status] IN (N'Approved', N'Paid'))
    ORDER BY r.PeriodYear DESC, r.PeriodMonth DESC;
END
GO
