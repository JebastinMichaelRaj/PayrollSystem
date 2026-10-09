# Ledgerline - Employee Payroll Management System

An enterprise-style payroll system built with **ASP.NET Core MVC (C#, .NET 10)**, **SQL Server** and **stored procedures only** (no Entity Framework, no inline SQL).
It covers salary calculation (base, allowances, overtime), deductions (tax, insurance/statutory, loan recovery, other), leave management (accrual, usage, balances),
and payroll reporting (payslips, tax, departmental, statutory compliance), with role-based access, audit trails and transactional integrity.

> **Read this first - what has and has not been verified**
> * Verified: the C# solution (Core, Data, Web incl. Razor views) compiles; the 24 payroll/tax/leave unit tests in `tests/` pass.
> * **Not verified:** the T-SQL scripts and the live application against a real SQL Server (none was available while this was written). Expect to run `database/deploy.*`, fix any small syntax slip it reports, and smoke-test the workflows below before relying on it.
> * **Illustrative configuration:** statutory rates (PF 12%/12% capped, insurance), tax slabs, standard deduction and cess in `08_SeedData.sql` are *sample data*. Replace them with your jurisdiction's current rules and have payroll/legal sign off before real use.

---

## 1. Architecture

```
Browser --> Payroll.Web (MVC controllers, views, auth, filters)
                |  ICurrentUser / services (interfaces)
                v
            Payroll.Core (business logic: calculators, services, validation, authorization checks, models, repository interfaces)
                ^  implements repository interfaces
            Payroll.Data (ADO.NET + stored procedures, error translation, transactions)
                v
            SQL Server (tables, TVPs, stored procedures)
```

| Layer | Project | Responsibility | Depends on |
|---|---|---|---|
| Presentation | `Payroll.Web` | Controllers, Razor views, cookie auth, policies, security headers, CSV export | Core, Data (only for DI wiring) |
| Business | `Payroll.Core` | Pure calculators (`PayrollCalculator`, `ProgressiveTaxCalculator`, `LeaveCalculator`, `WorkingCalendar`), services, validation, domain exceptions, repository **interfaces** | nothing (BCL + logging/options abstractions) |
| Data access | `Payroll.Data` | `SqlDatabase` (the only class touching ADO.NET), repositories, TVP building | Core, Microsoft.Data.SqlClient |
| Tests | `Payroll.Tests` | xUnit tests for calculators | Core |

SOLID in practice: services depend on repository *interfaces* (DIP); calculators are pure and independently testable (SRP); `ITaxCalculator`/`IPayrollCalculator` can be swapped for country-specific rules (OCP); `ICurrentUser` hides HTTP from the business layer.

### Folder structure
```
PayrollSystem/
  PayrollSystem.sln
  database/                      01_Schema ... 08_SeedData, deploy.ps1, deploy.sh
  src/
    Payroll.Core/
      Abstractions/              ICurrentUser, repository + ITransactionRunner + IPasswordHasher interfaces
      Calculation/               PayrollCalculator, TaxCalculator, LeaveCalculator, WorkingCalendar, Money, Validation
      Configuration/             PayrollOptions (business policy settings)
      Domain/                    Roles, status constants, exceptions
      Models/                    DTOs/inputs (DataAnnotations validation), calculation input/output models
      Security/                  Pbkdf2PasswordHasher
      Services/                  Auth, Employee, Compensation, Leave, Payroll, Report services
    Payroll.Data/
      Infrastructure/            SqlDatabase, Param, reader extensions
      Repositories/              one repository per aggregate
    Payroll.Web/
      Controllers/ ViewModels/ Views/ Infrastructure/ wwwroot/
  tests/Payroll.Tests/
```
Naming: `usp_<Entity>_<Action>` for procedures; `<Entity>Service` / `<Entity>Repository`; one controller per feature area.

---

## 2. Database schema

Run order is the file numbering. All scripts are idempotent (`IF OBJECT_ID ... IS NULL`, `CREATE OR ALTER`).

| Area | Tables | Notes |
|---|---|---|
| Organisation | `Department`, `Designation`, `Employee` | `RowVer` (rowversion) gives optimistic concurrency; `seq_EmployeeCode` generates `EMP00001`; manager hierarchy via `ManagerId` |
| Identity | `AppUser` | PBKDF2 hash, lockout counters, `MustChangePassword`; one role per user (`Admin`, `HR`, `Payroll`, `Manager`, `Employee`), optionally linked to an employee |
| Compensation | `SalaryStructure`, `SalaryComponent`, `EmployeeComponent` | Effective-dated. Filtered unique indexes guarantee a single open record per employee/component. Components are `Fixed` or `PercentOfBasic`, earning or deduction, taxable or not |
| Rules | `StatutoryRule`, `TaxSlab`, `TaxParameter` | Rates/ceilings/pre-tax flag effective-dated; slabs and standard deduction/cess per financial year - all data, no code change when rules change |
| Time & money | `OvertimeEntry`, `Loan`, `LoanRepayment`, `Holiday` | Overtime has an approval workflow; loans track outstanding balance; repayments are posted on run approval |
| Payroll | `PayrollRun`, `Payslip`, `PayslipLine` | One run per month (unique). `Payslip` snapshots department and totals; `PayslipLine` is an itemised ledger (`Earning`/`Deduction`/`Employer`) |
| Leave | `LeaveType`, `LeaveBalance`, `LeaveRequest`, `LeaveLedger` | Balance table for fast reads + append-only signed ledger for audit/reconciliation. Filtered unique indexes make accrual and carry-forward idempotent |
| Audit | `AuditLog` | Written by every mutating procedure (who, what, when, entity, details, optional IP) |

Table types: `PayslipTableType`, `PayslipLineTableType` (bulk payslip save). Column order is part of the contract with `PayrollRepository.SavePayslipsAsync`.

### Stored procedures (by file)
* **03** identity & employees: `usp_User_*`, `usp_Lookup_All` (3 result sets, 1 round trip), `usp_Employee_Create/Update/GetById/Search/Terminate`
* **04** compensation: `usp_Salary_*`, `usp_EmployeeComponent_*`, `usp_Overtime_*`, `usp_Loan_*`, `usp_Holiday_*`
* **05** leave: `usp_Leave_Submit/Decide/Cancel/GetRequests/GetBalances/GetLedger/RunMonthlyAccrual/YearEndCarryForward/Adjust`
* **06** payroll: `usp_Payroll_CreateRun/GetCalculationInputs/SavePayslips/Approve/MarkPaid`, `usp_Payslip_Get/ListByEmployee`
* **07** reports: `usp_Report_PayrollRegister/DepartmentSummary/Statutory/Tax/LeaveBalance`, `usp_Audit_Search`, `usp_Dashboard_Summary`

Error contract (T-SQL `THROW`, mapped in `SqlDatabase.Translate`): `50001` not found, `50002` invalid state, `50003` insufficient balance, `50004` overlap/duplicate, `50005` validation, `50010` concurrency conflict, `50020/21/22` not authorised / self-approval / separation of duties. Business messages reach the user; anything else is logged and replaced by a generic message.

### Scalability choices (no N+1)
* Payroll for N employees = **1 read** (`usp_Payroll_GetCalculationInputs`, 10 result sets) + in-memory calculation + **1 write** (`usp_Payroll_SavePayslips` with two TVPs). Cost is O(1) round trips regardless of headcount.
* Accrual and year-end carry-forward are single set-based statements (`INSERT ... OUTPUT ... INTO`) for all employees.
* Lists are paged in SQL (`OFFSET/FETCH`, `COUNT(*) OVER()`); reports are generic result sets rendered/CSV-exported without per-row queries.
* Indexes cover the hot paths (payslip by employee/run, leave by employee/status, ledger by employee/year, audit by time/entity, payslip lines by code).

---

## 3. Key business logic

### Payroll calculation (`PayrollCalculator`, pure and unit-tested)
For each employee in the period:
1. **Attendance.** `WorkingDays` = Mon-Fri (configurable weekend) minus holidays. Employed window = max(join date, month start) .. min(exit date, month end). `LOP days` = approved *unpaid* leave overlapping that window (working days only; half-day = 0.5). `ratio = (employed working days - LOP) / month working days`.
2. **Earnings.** Basic x ratio. Allowances: `Fixed` x ratio, `PercentOfBasic` x earned basic. Each line is flagged taxable or not.
3. **Overtime** (if eligible): `basic / standard hours x sum(hours x multiplier)`, capped by `MaxOvertimeHoursPerMonth`; ineligible overtime is ignored with a warning.
4. **Statutory.** For each active rule: base = Basic or Gross, limited by `WageCeiling`; employee share becomes a deduction, employer share an `Employer` line (not deducted from pay). Pre-tax rules reduce taxable income.
5. **Income tax (TDS).** `monthly taxable x 12 - standard deduction` -> progressive slabs -> + cess -> `/ 12`.
6. **Other deductions** (fixed / % of basic).
7. **Loan recovery** last, limited to `LoanRecoveryCapPercentOfNet`% of net-before-loan and never above the outstanding balance, so pay can't go negative from loans.
8. Net = gross - deductions. All amounts use `Money.Round` (2 dp, away from zero).

Any employee failing calculation aborts the **whole** run (no partial payroll). Employees without a salary structure are skipped with a warning.

**Simplifications to be aware of:** TDS is a flat monthly projection (annualised current month) with no year-to-date true-up, rebates or investment declarations; salary structure/components are taken as effective on the last day of the period (or exit date); working-day proration (not calendar-day). Extend `ITaxCalculator`/`PayrollCalculator` for your jurisdiction.

### Run lifecycle & controls
`Open -> Processed (re-runnable) -> Approved (locked) -> Paid`.
* **Approve** posts loan repayments, reduces balances, closes finished loans, locks the period, and makes payslips visible to employees.
* **Separation of duties** (`RequireSeparateApprover`): the user who processed a run cannot approve it.
* Once a month is approved/paid, backdated salary changes, overtime changes, unpaid-leave approvals/cancellations and holiday deletions for that month are rejected by the procedures.

### Leave
* **Day counting** is done in C# (`LeaveCalculator`) using the same `WorkingCalendar` as payroll; the user can't supply the day count. Rules: single calendar year, back-dating limit, half-day only for one day and eligible types, max consecutive days.
* **Submit** (proc): per-employee `sp_getapplock` serialises requests; rejects overlaps; paid types must have `balance - pending >= days` (pending requests reserve balance).
* **Decide** (proc): locks the request and balance row, re-checks balance, writes `Used` + ledger `Usage`; managers may only decide for direct reports; nobody can decide their own request.
* **Cancel:** pending anytime; approved only before it starts (self-service) - HR can cancel later; writes a ledger `Reversal`.
* **Accrual:** `Monthly` types credit `entitlement/12` (joiners after the 15th start next month); `Annual` types credit in January (pro-rated, rounded to 0.5, for mid-year joiners). **Idempotent.**
* **Year-end:** carry-forward up to each type's cap + January accrual, executed in one `ITransactionRunner` transaction. (Balance above the cap simply lapses; add `Lapse` ledger rows if you need to record it.)

---

## 4. Security
* **SQL injection:** all data access is `CommandType.StoredProcedure` with typed parameters; report procedure names come from a whitelist; search wildcards are escaped.
* **AuthN:** cookie auth (`__Host-` prefix, HttpOnly, Secure, SameSite=Strict, 30 min sliding); PBKDF2-SHA256 600k iterations; lockout after 5 failures (15 min); generic login errors + timing equalisation; login rate-limited (10/min/IP); forced password change for new/bootstrap users; password policy (12+ chars, mixed classes).
* **AuthZ:** secure-by-default fallback policy; policy per controller/action **and** re-checked in every service (`ICurrentUser.Require`); ownership enforced in procedures (payslips, leave cancel, manager scoping).
* **Web hardening:** global anti-forgery validation on all POSTs, strict CSP (`default-src 'self'`, no inline script/style), `X-Frame-Options`, `nosniff`, `no-store` caching, HSTS + HTTPS redirect, CSV formula-injection neutralisation, open-redirect-safe flash redirects, bank account masked on payslips.
* **Concurrency/integrity:** rowversion on employees, `UPDLOCK/HOLDLOCK` on balance/run rows, unique indexes for run period/accrual/open salary structure, deadlock retry (1205) for non-transactional calls.
* **Audit:** every state change writes `AuditLog` inside the same transaction; login success/failure recorded with IP. Admins browse/export it under *Audit trail*.
* Known gap: a deactivated user keeps an existing session until the cookie expires (<= 30 min). Add an `OnValidatePrincipal` check if you need immediate revocation. For high-sensitivity columns (`BankAccountNumber`, `TaxIdNumber`) consider Always Encrypted or TDE.

---

## 5. Setup & running

**Prerequisites:** .NET 10 SDK (to use .NET 8/9 instead, change `<TargetFramework>` in the four csproj files), SQL Server 2019+ (LocalDB, Docker or full), `sqlcmd`.

```bash
# 1. Database (creates PayrollDb, tables, procedures, reference data)
cd database
./deploy.sh                      # needs SQLSERVER, SQLUSER, SQLPASS env vars
#   or on Windows:  ./deploy.ps1 -Server localhost            (Windows auth)
#                   ./deploy.ps1 -Server localhost -User sa -Password '...'
# Docker example:
#   docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Str0ng!Passw0rd' -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

# 2. Configure the connection string (user-secrets keeps it out of source control)
cd ../src/Payroll.Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:PayrollDb" "Server=localhost;Database=PayrollDb;User Id=sa;Password=Str0ng!Passw0rd;TrustServerCertificate=true"
dotnet user-secrets set "Seed:AdminPassword" "ChangeMe!Now-2026"      # first-run admin (username: admin)

# 3. Trust the dev certificate and run
dotnet dev-certs https --trust
dotnet run                        # https://localhost:7080
```
Sign in as `admin` with the seed password; you'll be forced to choose a new one. Then:
1. *Users*: create logins and link them to employees. 2. *Employees*: add people, then set **salary structures** (without one they are skipped by payroll). 3. *Holidays*: add the year's holidays. 4. *Leave administration*: run accruals. 5. Review `StatutoryRule`, `TaxSlab`, `TaxParameter`, `LeaveType` data for your jurisdiction.

Tests: `dotnet test` (from the solution root).

### Configuration (`appsettings.json` -> `Payroll`)
`FinancialYearStartMonth` (4 = April), `WeekendDays`, `MaxOvertimeHoursPerMonth`, `LoanRecoveryCapPercentOfNet`, `RequireSeparateApprover`, `LeaveBackdateLimitDays`, `MaxFailedLogins`, `LockoutMinutes`, `MinPasswordLength`. Environment overrides use `Payroll__Name` syntax.

---

## 6. Controllers & endpoints

All routes are `/{controller}/{action}/{id?}`; every POST needs the anti-forgery token (forms include it automatically).

| Controller | Endpoints | Access |
|---|---|---|
| Account | `GET/POST Login`, `POST Logout`, `GET/POST ChangePassword`, `GET AccessDenied` | anonymous (login) / any signed-in |
| Home | `GET /` dashboard, `GET Error`, `GET Status/{code}` | any |
| Employees | `GET Index` (search/page), `GET Details/{id}`, `GET/POST Create`, `GET/POST Edit/{id}`, `POST Terminate/{id}` | Admin, HR |
| Salary | `GET Manage?employeeId`, `POST SetSalary`, `POST AssignComponent`, `POST EndComponent` | Admin, HR, Payroll |
| Overtime | `GET Index`, `POST Submit`, `POST Decide` | any (scoped); decide: Admin/HR/Payroll/Manager (managers: direct reports) |
| Loans | `GET Index`, `POST Create`, `POST Cancel` | view: any (own only for non-finance); manage: Admin/HR/Payroll |
| Holidays | `GET Index?year`, `POST Upsert`, `POST Delete` | view: any; edit: Admin/HR |
| Leave | `GET Index`, `POST Apply`, `POST Cancel`, `GET Pending`, `POST Decide`, `GET Admin`, `POST Accrue`, `POST YearEnd`, `POST Adjust`, `GET Ledger` | self-service: any linked employee; approvals: Admin/HR/Manager; admin: Admin/HR |
| Payroll | `GET Index`, `POST Create`, `GET Run/{id}`, `POST Process/{id}`, `POST Approve/{id}`, `POST MarkPaid/{id}` | view: Admin/HR/Payroll; actions: Admin/Payroll |
| Payslips | `GET My`, `GET Details/{id}` (printable) | own payslips after approval; Admin/HR/Payroll any |
| Reports | `GET Index`, `Register`, `Department`, `Statutory`, `Tax`, `LeaveBalance` (add `export=true` for CSV), `Audit` | Admin/HR/Payroll; Audit: Admin |
| Users | `GET Index`, `POST Create`, `POST SetActive` | Admin |
| (health) | `GET /health` | anonymous |

---

## 7. Example workflows

**Process a month of payroll** (Payroll user "Priya", Admin "Arun")
1. Priya: *Payroll runs -> Start a run* (year/month). 2. Make sure leave for that month is decided, overtime approved, loans entered.
3. Priya: *Process payroll*. Read the warnings panel (skipped employees, capped overtime, reduced loan instalments). Fix data and *Re-process* as often as needed.
4. Review the register, *Department summary* and *Statutory report*.
5. Arun (a different user): *Approve & lock*. Loan recoveries post, payslips become visible to employees.
6. After bank transfer: *Mark as paid*. Export the statutory report CSV for remittance.

**Approve leave**
1. Employee: *My leave -> Request leave* (dates, type, optional half-day). Balance minus pending is checked immediately.
2. Manager (or HR): *Leave requests* -> approve/reject with a note. Approval re-checks the balance under lock, updates `Used`, writes the ledger.
3. If the leave is unpaid, it reduces pay as LOP in the next payroll run.
4. HR: *Leave administration -> Employee ledger* shows every accrual/usage/reversal for audits.

**Generate reports**: *Reports* -> choose run / financial year / year + department -> view -> *Export CSV*. Payslips: *Payroll -> Run -> Payslip* (HR/payroll) or *My payslips* (employee) -> *Print* (browser print-to-PDF).

---

## 8. Deployment considerations
* **HTTPS and reverse proxy:** terminate TLS at IIS/nginx/ingress; if proxied, add `UseForwardedHeaders` so IP addresses in audit/rate-limit are correct. The cookies use the `__Host-` prefix, which requires HTTPS and path `/`.
* **Scaling out:** sessions/TempData use in-memory distributed cache - switch to Redis or SQL (`AddStackExchangeRedisCache`/`AddDistributedSqlServerCache`) and share Data Protection keys (`PersistKeysToDbContext` is EF-based; use `PersistKeysToFileSystem`/Azure Blob/Redis) across instances.
* **Secrets:** keep connection strings and `Seed__AdminPassword` in environment variables / Key Vault; remove the seed password after first run. Use a least-privilege SQL login with `EXECUTE` on the schema, **not** `db_owner`.
* **Database:** schedule backups; consider partitioning/archiving `AuditLog`, `PayslipLine`, `LeaveLedger` after several years; monitor deadlocks (`1205`) and slow procedures (Query Store).
* **Operations:** logs go to console and rolling files (`logs/payroll-*.log`, Serilog); ship them to your SIEM. `/health` for load balancers. Schedule the monthly leave accrual via an operator routine or a small hosted job calling `ILeaveService.RunMonthlyAccrualAsync` (idempotent).
* **Compliance:** retain payroll data per local law; payslip/tax rules are your responsibility to validate; restrict access to the audit trail and exports.
* **Next steps worth considering:** YTD-aware tax true-up, bank file (NACHA/SEPA) export, PDF payslips and email delivery, multi-currency/entity, leave policies by grade, employee self-service profile edits, API surface + integration tests against a disposable SQL Server (e.g. Testcontainers).
