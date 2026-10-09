/* 07 - Reports, audit search, dashboard. Column aliases become column headings in the UI/CSV.
   A column named TotalCount is treated as the paging total and hidden by the application. */
USE PayrollDb;
GO
CREATE OR ALTER PROCEDURE dbo.usp_Report_PayrollRegister @RunId INT, @PageNumber INT = 1, @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT OFF;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 SET @PageSize = 50;
    SELECT p.PayslipId AS [Payslip Id], e.EmployeeCode AS [Code], e.FirstName + N' ' + e.LastName AS [Employee], d.Name AS [Department],
           p.WorkingDays AS [Working Days], p.PaidDays AS [Paid Days], p.LopDays AS [LOP Days], p.BasicSalary AS [Basic],
           p.OvertimePay AS [Overtime], p.GrossEarnings AS [Gross], p.TotalDeductions AS [Deductions], p.NetPay AS [Net Pay],
           p.EmployerContributions AS [Employer Cost], COUNT(*) OVER() AS TotalCount
    FROM dbo.Payslip p JOIN dbo.Employee e ON e.EmployeeId = p.EmployeeId JOIN dbo.Department d ON d.DepartmentId = p.DepartmentId
    WHERE p.PayrollRunId = @RunId
    ORDER BY e.EmployeeCode OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Report_DepartmentSummary @RunId INT
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT d.Name AS [Department], COUNT(*) AS [Employees], SUM(p.BasicSalary) AS [Basic], SUM(p.OvertimePay) AS [Overtime],
           SUM(p.GrossEarnings) AS [Gross], SUM(p.TotalDeductions) AS [Deductions], SUM(p.NetPay) AS [Net Pay],
           SUM(p.EmployerContributions) AS [Employer Contributions], SUM(p.GrossEarnings + p.EmployerContributions) AS [Total Cost]
    FROM dbo.Payslip p JOIN dbo.Department d ON d.DepartmentId = p.DepartmentId
    WHERE p.PayrollRunId = @RunId GROUP BY d.Name ORDER BY d.Name;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Report_Statutory @RunId INT
AS
BEGIN
    SET NOCOUNT OFF;
    -- summary: remittance totals per statutory code (includes withholding tax)
    SELECT l.Code AS [Code], MAX(CASE WHEN l.LineType = N'Deduction' THEN l.[Description] END) AS [Description],
           COUNT(DISTINCT p.EmployeeId) AS [Employees],
           SUM(CASE WHEN l.LineType = N'Deduction' THEN l.Amount ELSE 0 END) AS [Employee Share],
           SUM(CASE WHEN l.LineType = N'Employer' THEN l.Amount ELSE 0 END) AS [Employer Share],
           SUM(l.Amount) AS [Total Remittance]
    FROM dbo.PayslipLine l JOIN dbo.Payslip p ON p.PayslipId = l.PayslipId
    WHERE p.PayrollRunId = @RunId AND l.IsStatutory = 1 GROUP BY l.Code ORDER BY l.Code;
    -- detail: per employee per code
    SELECT e.EmployeeCode AS [Employee Code], e.FirstName + N' ' + e.LastName AS [Employee], e.TaxIdNumber AS [Tax Id], l.Code AS [Code],
           SUM(CASE WHEN l.LineType = N'Deduction' THEN l.Amount ELSE 0 END) AS [Employee Share],
           SUM(CASE WHEN l.LineType = N'Employer' THEN l.Amount ELSE 0 END) AS [Employer Share]
    FROM dbo.PayslipLine l JOIN dbo.Payslip p ON p.PayslipId = l.PayslipId JOIN dbo.Employee e ON e.EmployeeId = p.EmployeeId
    WHERE p.PayrollRunId = @RunId AND l.IsStatutory = 1
    GROUP BY e.EmployeeCode, e.FirstName, e.LastName, e.TaxIdNumber, l.Code ORDER BY e.EmployeeCode, l.Code;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Report_Tax @FinancialYear INT, @FyStartMonth TINYINT = 4, @DepartmentId INT = NULL
AS
BEGIN
    SET NOCOUNT OFF;
    DECLARE @From DATE = DATEFROMPARTS(@FinancialYear, @FyStartMonth, 1), @To DATE = DATEADD(MONTH, 12, DATEFROMPARTS(@FinancialYear, @FyStartMonth, 1));
    SELECT e.EmployeeCode AS [Employee Code], e.FirstName + N' ' + e.LastName AS [Employee], e.TaxIdNumber AS [Tax Id],
           COUNT(DISTINCT r.PayrollRunId) AS [Months], SUM(p.GrossEarnings) AS [Gross Earnings], SUM(p.TaxableIncome) AS [Taxable Income],
           ISNULL(SUM(t.Amount), 0) AS [Tax Deducted]
    FROM dbo.Payslip p
    JOIN dbo.PayrollRun r ON r.PayrollRunId = p.PayrollRunId AND r.[Status] IN (N'Approved', N'Paid')
    JOIN dbo.Employee e ON e.EmployeeId = p.EmployeeId
    LEFT JOIN dbo.PayslipLine t ON t.PayslipId = p.PayslipId AND t.Code = N'TDS'
    WHERE DATEFROMPARTS(r.PeriodYear, r.PeriodMonth, 1) >= @From AND DATEFROMPARTS(r.PeriodYear, r.PeriodMonth, 1) < @To
      AND (@DepartmentId IS NULL OR p.DepartmentId = @DepartmentId)
    GROUP BY e.EmployeeCode, e.FirstName, e.LastName, e.TaxIdNumber ORDER BY e.EmployeeCode;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Report_LeaveBalance @Year SMALLINT, @DepartmentId INT = NULL
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT e.EmployeeCode AS [Code], e.FirstName + N' ' + e.LastName AS [Employee], d.Name AS [Department], t.Name AS [Leave Type],
           b.Opening AS [Opening], b.Accrued AS [Accrued], b.Adjusted AS [Adjusted], b.Used AS [Used], b.Balance AS [Balance]
    FROM dbo.LeaveBalance b
    JOIN dbo.Employee e ON e.EmployeeId = b.EmployeeId
    JOIN dbo.Department d ON d.DepartmentId = e.DepartmentId
    JOIN dbo.LeaveType t ON t.LeaveTypeId = b.LeaveTypeId
    WHERE b.[Year] = @Year AND (@DepartmentId IS NULL OR e.DepartmentId = @DepartmentId)
    ORDER BY e.EmployeeCode, t.Name;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Audit_Search
    @From DATETIME2 = NULL, @To DATETIME2 = NULL, @EntityName NVARCHAR(50) = NULL, @Username NVARCHAR(100) = NULL,
    @PageNumber INT = 1, @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT OFF;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 SET @PageSize = 50;
    SELECT AuditId AS [Id], EventTimeUtc AS [Time (UTC)], Username AS [User], EntityName AS [Entity], EntityId AS [Entity Id],
           [Action] AS [Action], Details AS [Details], IpAddress AS [IP], COUNT(*) OVER() AS TotalCount
    FROM dbo.AuditLog
    WHERE (@From IS NULL OR EventTimeUtc >= @From) AND (@To IS NULL OR EventTimeUtc < @To)
      AND (@EntityName IS NULL OR EntityName = @EntityName) AND (@Username IS NULL OR Username = @Username)
    ORDER BY AuditId DESC OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO
CREATE OR ALTER PROCEDURE dbo.usp_Dashboard_Summary @ManagerEmployeeId INT = NULL
AS
BEGIN
    SET NOCOUNT OFF;
    SELECT (SELECT COUNT(*) FROM dbo.Employee WHERE ExitDate IS NULL) AS ActiveEmployees,
           (SELECT COUNT(*) FROM dbo.LeaveRequest r JOIN dbo.Employee e ON e.EmployeeId = r.EmployeeId
             WHERE r.[Status] = N'Pending' AND (@ManagerEmployeeId IS NULL OR e.ManagerId = @ManagerEmployeeId)) AS PendingLeaves,
           (SELECT COUNT(*) FROM dbo.OvertimeEntry o JOIN dbo.Employee e ON e.EmployeeId = o.EmployeeId
             WHERE o.[Status] = N'Pending' AND (@ManagerEmployeeId IS NULL OR e.ManagerId = @ManagerEmployeeId)) AS PendingOvertime,
           (SELECT COUNT(*) FROM dbo.Loan WHERE [Status] = N'Active') AS ActiveLoans,
           lr.PayrollRunId AS LatestRunId, lr.PeriodYear AS LatestRunYear, lr.PeriodMonth AS LatestRunMonth,
           lr.[Status] AS LatestRunStatus, lr.TotalNet AS LatestRunNet
    FROM (SELECT 1 AS x) d
    LEFT JOIN (SELECT TOP (1) PayrollRunId, PeriodYear, PeriodMonth, [Status], TotalNet FROM dbo.PayrollRun ORDER BY PeriodYear DESC, PeriodMonth DESC) lr ON 1 = 1;
END
GO
