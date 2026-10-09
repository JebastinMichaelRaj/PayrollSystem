/* 02 - Table types (TVPs) and the audit writer */
USE PayrollDb;
GO
/* Column ORDER matters: TVPs bind by ordinal. Keep in sync with PayrollRepository.cs */
IF TYPE_ID(N'dbo.PayslipTableType') IS NULL
CREATE TYPE dbo.PayslipTableType AS TABLE(
    EmployeeId INT NOT NULL PRIMARY KEY,
    DepartmentId INT NOT NULL,
    BasicSalary DECIMAL(18,2) NOT NULL,
    CalendarDays TINYINT NOT NULL,
    WorkingDays DECIMAL(5,2) NOT NULL,
    PaidDays DECIMAL(5,2) NOT NULL,
    LopDays DECIMAL(5,2) NOT NULL,
    OvertimeHours DECIMAL(7,2) NOT NULL,
    OvertimePay DECIMAL(18,2) NOT NULL,
    GrossEarnings DECIMAL(18,2) NOT NULL,
    TaxableIncome DECIMAL(18,2) NOT NULL,
    TotalDeductions DECIMAL(18,2) NOT NULL,
    NetPay DECIMAL(18,2) NOT NULL,
    EmployerContributions DECIMAL(18,2) NOT NULL);
GO
IF TYPE_ID(N'dbo.PayslipLineTableType') IS NULL
CREATE TYPE dbo.PayslipLineTableType AS TABLE(
    EmployeeId INT NOT NULL,
    LineType NVARCHAR(10) NOT NULL,
    Code NVARCHAR(30) NOT NULL,
    [Description] NVARCHAR(120) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    IsStatutory BIT NOT NULL,
    ReferenceId INT NULL,
    SortOrder SMALLINT NOT NULL);
GO
CREATE OR ALTER PROCEDURE dbo.usp_Audit_Write
    @UserId INT = NULL, @EntityName NVARCHAR(50), @EntityId NVARCHAR(50) = NULL,
    @Action NVARCHAR(40), @Details NVARCHAR(MAX) = NULL, @IpAddress NVARCHAR(45) = NULL
AS
BEGIN
    SET NOCOUNT OFF;
    INSERT dbo.AuditLog(UserId, Username, EntityName, EntityId, [Action], Details, IpAddress)
    VALUES(@UserId, (SELECT Username FROM dbo.AppUser WHERE UserId = @UserId), @EntityName, @EntityId, @Action, @Details, @IpAddress);
END
GO
/* Error numbers raised by procedures (mapped in SqlDatabase.cs):
   50001 not found | 50002 invalid state | 50003 insufficient balance | 50004 overlap/duplicate
   50005 validation | 50010 concurrency | 50020 not authorised | 50021 self-approval | 50022 separation of duties */
