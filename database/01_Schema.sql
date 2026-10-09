/* =====================================================================
   Payroll Management System - 01 Schema  (SQL Server 2019+)
   Idempotent: safe to re-run. Run via deploy.ps1 / sqlcmd.
   ===================================================================== */
SET NOCOUNT OFF;
IF DB_ID(N'PayrollDb') IS NULL CREATE DATABASE PayrollDb;
GO
USE PayrollDb;
GO
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'seq_EmployeeCode')
    CREATE SEQUENCE dbo.seq_EmployeeCode AS INT START WITH 1 INCREMENT BY 1;
GO
/* ---------------- Organisation ---------------- */
IF OBJECT_ID(N'dbo.Department','U') IS NULL
CREATE TABLE dbo.Department(
    DepartmentId INT IDENTITY(1,1) CONSTRAINT PK_Department PRIMARY KEY,
    Code NVARCHAR(20) NOT NULL CONSTRAINT UQ_Department_Code UNIQUE,
    Name NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1);
IF OBJECT_ID(N'dbo.Designation','U') IS NULL
CREATE TABLE dbo.Designation(
    DesignationId INT IDENTITY(1,1) CONSTRAINT PK_Designation PRIMARY KEY,
    Title NVARCHAR(100) NOT NULL CONSTRAINT UQ_Designation_Title UNIQUE,
    IsActive BIT NOT NULL DEFAULT 1);
IF OBJECT_ID(N'dbo.Employee','U') IS NULL
CREATE TABLE dbo.Employee(
    EmployeeId INT IDENTITY(1,1) CONSTRAINT PK_Employee PRIMARY KEY,
    EmployeeCode NVARCHAR(20) NOT NULL CONSTRAINT UQ_Employee_Code UNIQUE,
    FirstName NVARCHAR(60) NOT NULL,
    LastName NVARCHAR(60) NOT NULL,
    Email NVARCHAR(256) NOT NULL CONSTRAINT UQ_Employee_Email UNIQUE,
    Phone NVARCHAR(30) NULL,
    DateOfBirth DATE NOT NULL,
    JoinDate DATE NOT NULL,
    ExitDate DATE NULL,
    DepartmentId INT NOT NULL CONSTRAINT FK_Employee_Department REFERENCES dbo.Department(DepartmentId),
    DesignationId INT NOT NULL CONSTRAINT FK_Employee_Designation REFERENCES dbo.Designation(DesignationId),
    ManagerId INT NULL CONSTRAINT FK_Employee_Manager REFERENCES dbo.Employee(EmployeeId),
    EmploymentType NVARCHAR(20) NOT NULL CONSTRAINT CK_Employee_Type CHECK (EmploymentType IN (N'FullTime',N'PartTime',N'Contract')),
    TaxIdNumber NVARCHAR(30) NULL,
    BankName NVARCHAR(100) NULL,
    BankAccountNumber NVARCHAR(40) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy INT NULL,
    UpdatedAt DATETIME2(0) NULL,
    UpdatedBy INT NULL,
    RowVer ROWVERSION NOT NULL,
    CONSTRAINT CK_Employee_Dates CHECK (ExitDate IS NULL OR ExitDate >= JoinDate),
    INDEX IX_Employee_Dept (DepartmentId, IsActive),
    INDEX IX_Employee_Name (LastName, FirstName),
    INDEX IX_Employee_Manager (ManagerId));
IF OBJECT_ID(N'dbo.AppUser','U') IS NULL
CREATE TABLE dbo.AppUser(
    UserId INT IDENTITY(1,1) CONSTRAINT PK_AppUser PRIMARY KEY,
    Username NVARCHAR(100) NOT NULL CONSTRAINT UQ_AppUser_Username UNIQUE,
    PasswordHash NVARCHAR(500) NOT NULL,
    [Role] NVARCHAR(30) NOT NULL CONSTRAINT CK_AppUser_Role CHECK ([Role] IN (N'Admin',N'HR',N'Payroll',N'Manager',N'Employee')),
    EmployeeId INT NULL CONSTRAINT FK_AppUser_Employee REFERENCES dbo.Employee(EmployeeId),
    IsActive BIT NOT NULL DEFAULT 1,
    FailedLoginCount INT NOT NULL DEFAULT 0,
    LockoutEndUtc DATETIME2(0) NULL,
    LastLoginUtc DATETIME2(0) NULL,
    MustChangePassword BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME());
/* ---------------- Compensation ---------------- */
IF OBJECT_ID(N'dbo.SalaryStructure','U') IS NULL
CREATE TABLE dbo.SalaryStructure(
    SalaryStructureId INT IDENTITY(1,1) CONSTRAINT PK_SalaryStructure PRIMARY KEY,
    EmployeeId INT NOT NULL CONSTRAINT FK_Salary_Employee REFERENCES dbo.Employee(EmployeeId),
    EffectiveFrom DATE NOT NULL,
    EffectiveTo DATE NULL,
    BasicSalary DECIMAL(18,2) NOT NULL CONSTRAINT CK_Salary_Basic CHECK (BasicSalary > 0),
    StandardHoursPerMonth DECIMAL(6,2) NOT NULL DEFAULT 160 CONSTRAINT CK_Salary_Hours CHECK (StandardHoursPerMonth > 0),
    OvertimeEligible BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy INT NULL,
    CONSTRAINT UQ_Salary_EmpFrom UNIQUE (EmployeeId, EffectiveFrom),
    CONSTRAINT CK_Salary_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom));
IF OBJECT_ID(N'dbo.SalaryComponent','U') IS NULL
CREATE TABLE dbo.SalaryComponent(
    ComponentId INT IDENTITY(1,1) CONSTRAINT PK_SalaryComponent PRIMARY KEY,
    Code NVARCHAR(30) NOT NULL CONSTRAINT UQ_SalaryComponent_Code UNIQUE,
    Name NVARCHAR(100) NOT NULL,
    ComponentType NVARCHAR(10) NOT NULL CONSTRAINT CK_Comp_Type CHECK (ComponentType IN (N'Earning',N'Deduction')),
    CalcMethod NVARCHAR(20) NOT NULL CONSTRAINT CK_Comp_Calc CHECK (CalcMethod IN (N'Fixed',N'PercentOfBasic')),
    IsTaxable BIT NOT NULL DEFAULT 1,
    IsActive BIT NOT NULL DEFAULT 1);
IF OBJECT_ID(N'dbo.EmployeeComponent','U') IS NULL
CREATE TABLE dbo.EmployeeComponent(
    EmployeeComponentId INT IDENTITY(1,1) CONSTRAINT PK_EmployeeComponent PRIMARY KEY,
    EmployeeId INT NOT NULL CONSTRAINT FK_EmpComp_Employee REFERENCES dbo.Employee(EmployeeId),
    ComponentId INT NOT NULL CONSTRAINT FK_EmpComp_Component REFERENCES dbo.SalaryComponent(ComponentId),
    [Value] DECIMAL(18,4) NOT NULL CONSTRAINT CK_EmpComp_Value CHECK ([Value] > 0),
    EffectiveFrom DATE NOT NULL,
    EffectiveTo DATE NULL,
    CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy INT NULL,
    CONSTRAINT CK_EmpComp_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
    INDEX IX_EmpComp_Emp (EmployeeId, EffectiveFrom));
IF OBJECT_ID(N'dbo.StatutoryRule','U') IS NULL
CREATE TABLE dbo.StatutoryRule(
    RuleId INT IDENTITY(1,1) CONSTRAINT PK_StatutoryRule PRIMARY KEY,
    Code NVARCHAR(30) NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    EmployeeRatePercent DECIMAL(7,4) NOT NULL DEFAULT 0 CHECK (EmployeeRatePercent BETWEEN 0 AND 100),
    EmployerRatePercent DECIMAL(7,4) NOT NULL DEFAULT 0 CHECK (EmployerRatePercent BETWEEN 0 AND 100),
    WageCeiling DECIMAL(18,2) NULL,
    BaseOn NVARCHAR(10) NOT NULL DEFAULT N'Basic' CHECK (BaseOn IN (N'Basic',N'Gross')),
    IsPreTax BIT NOT NULL DEFAULT 0,
    EffectiveFrom DATE NOT NULL,
    EffectiveTo DATE NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Statutory_CodeFrom UNIQUE (Code, EffectiveFrom));
IF OBJECT_ID(N'dbo.TaxParameter','U') IS NULL
CREATE TABLE dbo.TaxParameter(
    FinancialYear INT NOT NULL CONSTRAINT PK_TaxParameter PRIMARY KEY,
    AnnualStandardDeduction DECIMAL(18,2) NOT NULL DEFAULT 0,
    CessPercent DECIMAL(7,4) NOT NULL DEFAULT 0);
IF OBJECT_ID(N'dbo.TaxSlab','U') IS NULL
CREATE TABLE dbo.TaxSlab(
    TaxSlabId INT IDENTITY(1,1) CONSTRAINT PK_TaxSlab PRIMARY KEY,
    FinancialYear INT NOT NULL,
    FromAmount DECIMAL(18,2) NOT NULL,
    ToAmount DECIMAL(18,2) NULL,
    RatePercent DECIMAL(7,4) NOT NULL CHECK (RatePercent BETWEEN 0 AND 100),
    CONSTRAINT UQ_TaxSlab UNIQUE (FinancialYear, FromAmount),
    CONSTRAINT CK_TaxSlab_Range CHECK (ToAmount IS NULL OR ToAmount > FromAmount));
IF OBJECT_ID(N'dbo.OvertimeEntry','U') IS NULL
CREATE TABLE dbo.OvertimeEntry(
    OvertimeId INT IDENTITY(1,1) CONSTRAINT PK_OvertimeEntry PRIMARY KEY,
    EmployeeId INT NOT NULL CONSTRAINT FK_OT_Employee REFERENCES dbo.Employee(EmployeeId),
    WorkDate DATE NOT NULL,
    Hours DECIMAL(5,2) NOT NULL CONSTRAINT CK_OT_Hours CHECK (Hours > 0 AND Hours <= 16),
    Multiplier DECIMAL(4,2) NOT NULL DEFAULT 1.5 CHECK (Multiplier >= 1),
    [Status] NVARCHAR(10) NOT NULL DEFAULT N'Pending' CHECK ([Status] IN (N'Pending',N'Approved',N'Rejected')),
    Remarks NVARCHAR(300) NULL,
    DecidedBy INT NULL,
    DecidedAt DATETIME2(0) NULL,
    CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    INDEX IX_OT_EmpDate (EmployeeId, WorkDate) INCLUDE ([Status], Hours, Multiplier),
    INDEX IX_OT_Status ([Status], WorkDate));
IF OBJECT_ID(N'dbo.Loan','U') IS NULL
CREATE TABLE dbo.Loan(
    LoanId INT IDENTITY(1,1) CONSTRAINT PK_Loan PRIMARY KEY,
    EmployeeId INT NOT NULL CONSTRAINT FK_Loan_Employee REFERENCES dbo.Employee(EmployeeId),
    LoanType NVARCHAR(40) NOT NULL,
    PrincipalAmount DECIMAL(18,2) NOT NULL CHECK (PrincipalAmount > 0),
    InstallmentAmount DECIMAL(18,2) NOT NULL CHECK (InstallmentAmount > 0),
    OutstandingBalance DECIMAL(18,2) NOT NULL CONSTRAINT CK_Loan_Outstanding CHECK (OutstandingBalance >= 0),
    StartDate DATE NOT NULL,
    [Status] NVARCHAR(10) NOT NULL DEFAULT N'Active' CHECK ([Status] IN (N'Active',N'Closed',N'Cancelled')),
    CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy INT NULL,
    INDEX IX_Loan_Emp (EmployeeId, [Status]));
IF OBJECT_ID(N'dbo.Holiday','U') IS NULL
CREATE TABLE dbo.Holiday(HolidayDate DATE NOT NULL CONSTRAINT PK_Holiday PRIMARY KEY, Name NVARCHAR(100) NOT NULL);
/* ---------------- Payroll ---------------- */
IF OBJECT_ID(N'dbo.PayrollRun','U') IS NULL
CREATE TABLE dbo.PayrollRun(
    PayrollRunId INT IDENTITY(1,1) CONSTRAINT PK_PayrollRun PRIMARY KEY,
    PeriodYear SMALLINT NOT NULL,
    PeriodMonth TINYINT NOT NULL CHECK (PeriodMonth BETWEEN 1 AND 12),
    [Status] NVARCHAR(10) NOT NULL DEFAULT N'Open' CHECK ([Status] IN (N'Open',N'Processed',N'Approved',N'Paid')),
    EmployeeCount INT NOT NULL DEFAULT 0,
    TotalGross DECIMAL(18,2) NOT NULL DEFAULT 0,
    TotalDeductions DECIMAL(18,2) NOT NULL DEFAULT 0,
    TotalNet DECIMAL(18,2) NOT NULL DEFAULT 0,
    TotalEmployerCost DECIMAL(18,2) NOT NULL DEFAULT 0,
    CreatedBy INT NULL, CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    ProcessedBy INT NULL, ProcessedAt DATETIME2(0) NULL,
    ApprovedBy INT NULL, ApprovedAt DATETIME2(0) NULL,
    PaidBy INT NULL, PaidAt DATETIME2(0) NULL,
    CONSTRAINT UQ_PayrollRun_Period UNIQUE (PeriodYear, PeriodMonth));
IF OBJECT_ID(N'dbo.Payslip','U') IS NULL
CREATE TABLE dbo.Payslip(
    PayslipId INT IDENTITY(1,1) CONSTRAINT PK_Payslip PRIMARY KEY,
    PayrollRunId INT NOT NULL CONSTRAINT FK_Payslip_Run REFERENCES dbo.PayrollRun(PayrollRunId),
    EmployeeId INT NOT NULL CONSTRAINT FK_Payslip_Employee REFERENCES dbo.Employee(EmployeeId),
    DepartmentId INT NOT NULL,                       -- snapshot at processing time
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
    EmployerContributions DECIMAL(18,2) NOT NULL,
    CONSTRAINT UQ_Payslip_RunEmp UNIQUE (PayrollRunId, EmployeeId),
    INDEX IX_Payslip_Emp (EmployeeId) INCLUDE (PayrollRunId));
IF OBJECT_ID(N'dbo.PayslipLine','U') IS NULL
CREATE TABLE dbo.PayslipLine(
    PayslipLineId BIGINT IDENTITY(1,1) CONSTRAINT PK_PayslipLine PRIMARY KEY,
    PayslipId INT NOT NULL CONSTRAINT FK_Line_Payslip REFERENCES dbo.Payslip(PayslipId),
    LineType NVARCHAR(10) NOT NULL CHECK (LineType IN (N'Earning',N'Deduction',N'Employer')),
    Code NVARCHAR(30) NOT NULL,
    [Description] NVARCHAR(120) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    IsStatutory BIT NOT NULL DEFAULT 0,
    ReferenceId INT NULL,                            -- e.g. LoanId for loan recoveries
    SortOrder SMALLINT NOT NULL DEFAULT 0,
    INDEX IX_Line_Payslip (PayslipId),
    INDEX IX_Line_Code (Code, LineType) INCLUDE (PayslipId, Amount));
IF OBJECT_ID(N'dbo.LoanRepayment','U') IS NULL
CREATE TABLE dbo.LoanRepayment(
    LoanRepaymentId INT IDENTITY(1,1) CONSTRAINT PK_LoanRepayment PRIMARY KEY,
    LoanId INT NOT NULL CONSTRAINT FK_Repay_Loan REFERENCES dbo.Loan(LoanId),
    PayrollRunId INT NOT NULL CONSTRAINT FK_Repay_Run REFERENCES dbo.PayrollRun(PayrollRunId),
    Amount DECIMAL(18,2) NOT NULL CHECK (Amount > 0),
    PostedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Repay_LoanRun UNIQUE (LoanId, PayrollRunId));
/* ---------------- Leave ---------------- */
IF OBJECT_ID(N'dbo.LeaveType','U') IS NULL
CREATE TABLE dbo.LeaveType(
    LeaveTypeId INT IDENTITY(1,1) CONSTRAINT PK_LeaveType PRIMARY KEY,
    Code NVARCHAR(10) NOT NULL CONSTRAINT UQ_LeaveType_Code UNIQUE,
    Name NVARCHAR(60) NOT NULL,
    AnnualEntitlement DECIMAL(5,2) NOT NULL DEFAULT 0,
    AccrualMethod NVARCHAR(10) NOT NULL DEFAULT N'Monthly' CHECK (AccrualMethod IN (N'Monthly',N'Annual')),
    MaxCarryForward DECIMAL(5,2) NOT NULL DEFAULT 0,
    IsPaid BIT NOT NULL DEFAULT 1,
    AllowHalfDay BIT NOT NULL DEFAULT 1,
    MaxConsecutiveDays INT NULL,
    IsActive BIT NOT NULL DEFAULT 1);
IF OBJECT_ID(N'dbo.LeaveBalance','U') IS NULL
CREATE TABLE dbo.LeaveBalance(
    EmployeeId INT NOT NULL CONSTRAINT FK_LB_Employee REFERENCES dbo.Employee(EmployeeId),
    LeaveTypeId INT NOT NULL CONSTRAINT FK_LB_Type REFERENCES dbo.LeaveType(LeaveTypeId),
    [Year] SMALLINT NOT NULL,
    Opening DECIMAL(6,2) NOT NULL DEFAULT 0,
    Accrued DECIMAL(6,2) NOT NULL DEFAULT 0,
    Adjusted DECIMAL(6,2) NOT NULL DEFAULT 0,
    Used DECIMAL(6,2) NOT NULL DEFAULT 0,
    Balance AS (Opening + Accrued + Adjusted - Used) PERSISTED,
    CONSTRAINT PK_LeaveBalance PRIMARY KEY (EmployeeId, LeaveTypeId, [Year]));
IF OBJECT_ID(N'dbo.LeaveRequest','U') IS NULL
CREATE TABLE dbo.LeaveRequest(
    LeaveRequestId INT IDENTITY(1,1) CONSTRAINT PK_LeaveRequest PRIMARY KEY,
    EmployeeId INT NOT NULL CONSTRAINT FK_LR_Employee REFERENCES dbo.Employee(EmployeeId),
    LeaveTypeId INT NOT NULL CONSTRAINT FK_LR_Type REFERENCES dbo.LeaveType(LeaveTypeId),
    FromDate DATE NOT NULL,
    ToDate DATE NOT NULL,
    IsHalfDay BIT NOT NULL DEFAULT 0,
    Days DECIMAL(5,2) NOT NULL CHECK (Days > 0),
    Reason NVARCHAR(500) NULL,
    [Status] NVARCHAR(10) NOT NULL DEFAULT N'Pending' CHECK ([Status] IN (N'Pending',N'Approved',N'Rejected',N'Cancelled')),
    DecidedBy INT NULL,
    DecidedAt DATETIME2(0) NULL,
    DecisionRemarks NVARCHAR(500) NULL,
    CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_LR_Dates CHECK (ToDate >= FromDate),
    INDEX IX_LR_Emp (EmployeeId, [Status], FromDate),
    INDEX IX_LR_Status ([Status], FromDate));
IF OBJECT_ID(N'dbo.LeaveLedger','U') IS NULL
CREATE TABLE dbo.LeaveLedger(
    LedgerId BIGINT IDENTITY(1,1) CONSTRAINT PK_LeaveLedger PRIMARY KEY,
    EmployeeId INT NOT NULL,
    LeaveTypeId INT NOT NULL,
    [Year] SMALLINT NOT NULL,
    [Month] TINYINT NULL,
    TxnType NVARCHAR(12) NOT NULL CHECK (TxnType IN (N'Accrual',N'Usage',N'Reversal',N'Adjustment',N'CarryForward')),
    Days DECIMAL(6,2) NOT NULL,                      -- signed, balance perspective
    LeaveRequestId INT NULL,
    Remarks NVARCHAR(300) NULL,
    CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy INT NULL,
    INDEX IX_Ledger_Emp (EmployeeId, [Year], LeaveTypeId));
/* ---------------- Audit ---------------- */
IF OBJECT_ID(N'dbo.AuditLog','U') IS NULL
CREATE TABLE dbo.AuditLog(
    AuditId BIGINT IDENTITY(1,1) CONSTRAINT PK_AuditLog PRIMARY KEY,
    EventTimeUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UserId INT NULL,
    Username NVARCHAR(100) NULL,
    EntityName NVARCHAR(50) NOT NULL,
    EntityId NVARCHAR(50) NULL,
    [Action] NVARCHAR(40) NOT NULL,
    Details NVARCHAR(MAX) NULL,
    IpAddress NVARCHAR(45) NULL,
    INDEX IX_Audit_Time (EventTimeUtc DESC),
    INDEX IX_Audit_Entity (EntityName, EntityId));
GO
/* ---------------- Filtered unique indexes ---------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Salary_Open')
    CREATE UNIQUE INDEX UX_Salary_Open ON dbo.SalaryStructure(EmployeeId) WHERE EffectiveTo IS NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_EmpComp_Open')
    CREATE UNIQUE INDEX UX_EmpComp_Open ON dbo.EmployeeComponent(EmployeeId, ComponentId) WHERE EffectiveTo IS NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Ledger_Accrual')
    CREATE UNIQUE INDEX UX_Ledger_Accrual ON dbo.LeaveLedger(EmployeeId, LeaveTypeId, [Year], [Month]) WHERE TxnType = N'Accrual';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Ledger_Carry')
    CREATE UNIQUE INDEX UX_Ledger_Carry ON dbo.LeaveLedger(EmployeeId, LeaveTypeId, [Year]) WHERE TxnType = N'CarryForward';
GO
