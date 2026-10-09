using Payroll.Core.Domain;
using Payroll.Core.Models;
namespace Payroll.Core.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int UserId { get; }
    string Username { get; }
    string Role { get; }
    int? EmployeeId { get; }
    string? IpAddress { get; }
}
public static class CurrentUserExtensions
{
    public static bool IsInRole(this ICurrentUser u, params string[] roles) => u.IsAuthenticated && roles.Contains(u.Role, StringComparer.Ordinal);
    /// <summary>Defence in depth: services re-check authorisation even though controllers are already policy-protected.</summary>
    public static void Require(this ICurrentUser u, params string[] roles)
    {
        if (!u.IsAuthenticated) throw new ForbiddenException("Authentication is required.");
        if (!roles.Contains(u.Role, StringComparer.Ordinal)) throw new ForbiddenException();
    }
    public static int RequireEmployeeId(this ICurrentUser u) => u.EmployeeId ?? throw new BusinessRuleException("Your login is not linked to an employee record.", 50005);
}

public interface ITransactionRunner { Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct = default); }
public interface IPasswordHasher { string Hash(string password); bool Verify(string hash, string password, out bool needsRehash); }

public interface IUserRepository
{
    Task<UserRecord?> GetByUsernameAsync(string username, CancellationToken ct);
    Task RegisterLoginAsync(int userId, bool success, int maxFailures, int lockoutMinutes, string? ip, CancellationToken ct);
    Task<int> CreateAsync(string username, string passwordHash, string role, int? employeeId, bool mustChangePassword, int? actingUserId, CancellationToken ct);
    Task ChangePasswordAsync(int userId, string newHash, CancellationToken ct);
    Task<UserRecord?> GetByIdAsync(int userId, CancellationToken ct);
    Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken ct);
    Task SetActiveAsync(int userId, bool isActive, int actingUserId, CancellationToken ct);
}
public interface IEmployeeRepository
{
    Task<EmployeeLookups> GetLookupsAsync(CancellationToken ct);
    Task<PagedResult<EmployeeListItem>> SearchAsync(string? search, int? departmentId, bool? isActive, int page, int pageSize, CancellationToken ct);
    Task<EmployeeDetail?> GetByIdAsync(int id, CancellationToken ct);
    Task<int> CreateAsync(EmployeeInput input, int actingUserId, CancellationToken ct);
    Task UpdateAsync(EmployeeInput input, int actingUserId, CancellationToken ct);
    Task TerminateAsync(int id, DateOnly exitDate, int actingUserId, CancellationToken ct);
}
public interface ICompensationRepository
{
    Task<IReadOnlyList<SalaryStructureDto>> GetSalaryHistoryAsync(int employeeId, CancellationToken ct);
    Task SetSalaryAsync(SalaryInput input, int actingUserId, CancellationToken ct);
    Task<IReadOnlyList<SalaryComponentDef>> GetComponentDefsAsync(CancellationToken ct);
    Task<IReadOnlyList<EmployeeComponentDto>> GetEmployeeComponentsAsync(int employeeId, CancellationToken ct);
    Task AssignComponentAsync(ComponentAssignInput input, int actingUserId, CancellationToken ct);
    Task EndComponentAsync(int employeeComponentId, DateOnly endDate, int actingUserId, CancellationToken ct);
    Task<int> SubmitOvertimeAsync(int employeeId, OvertimeInput input, int actingUserId, CancellationToken ct);
    Task DecideOvertimeAsync(int overtimeId, bool approve, int actingUserId, int? approverEmployeeId, int? restrictToManagerId, string? remarks, CancellationToken ct);
    Task<PagedResult<OvertimeDto>> ListOvertimeAsync(string? status, int? employeeId, int? managerId, int page, int pageSize, CancellationToken ct);
    Task<int> CreateLoanAsync(LoanInput input, int actingUserId, CancellationToken ct);
    Task<IReadOnlyList<LoanDto>> ListLoansAsync(int? employeeId, string? status, CancellationToken ct);
    Task CancelLoanAsync(int loanId, int actingUserId, CancellationToken ct);
    Task<IReadOnlyList<HolidayDto>> GetHolidaysAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task UpsertHolidayAsync(DateOnly date, string name, int actingUserId, CancellationToken ct);
    Task DeleteHolidayAsync(DateOnly date, int actingUserId, CancellationToken ct);
}
public interface ILeaveRepository
{
    Task<IReadOnlyList<LeaveTypeDto>> GetTypesAsync(CancellationToken ct);
    Task<int> SubmitAsync(int employeeId, LeaveRequestInput input, decimal days, int actingUserId, CancellationToken ct);
    Task DecideAsync(int requestId, bool approve, int actingUserId, int? approverEmployeeId, int? restrictToManagerId, string? remarks, CancellationToken ct);
    Task CancelAsync(int requestId, int actingUserId, int? requestingEmployeeId, CancellationToken ct);
    Task<PagedResult<LeaveRequestDto>> GetRequestsAsync(int? employeeId, string? status, int? managerId, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<LeaveBalanceDto>> GetBalancesAsync(int employeeId, int year, CancellationToken ct);
    Task<IReadOnlyList<LeaveLedgerDto>> GetLedgerAsync(int employeeId, int year, CancellationToken ct);
    Task<int> RunMonthlyAccrualAsync(int year, int month, int actingUserId, CancellationToken ct);
    Task<int> CarryForwardAsync(int fromYear, int actingUserId, CancellationToken ct);
    Task AdjustAsync(LeaveAdjustInput input, int actingUserId, CancellationToken ct);
}
public interface IPayrollRepository
{
    Task<IReadOnlyList<PayrollRunDto>> GetRunsAsync(CancellationToken ct);
    Task<PayrollRunDto?> GetRunAsync(int runId, CancellationToken ct);
    Task<int> CreateRunAsync(int year, int month, int actingUserId, CancellationToken ct);
    Task<PayrollInputSet> GetCalculationInputsAsync(int runId, int fyStartMonth, CancellationToken ct);
    Task SavePayslipsAsync(int runId, int actingUserId, IReadOnlyList<PayslipResult> payslips, CancellationToken ct);
    Task ApproveAsync(int runId, int actingUserId, bool enforceSeparation, CancellationToken ct);
    Task MarkPaidAsync(int runId, int actingUserId, CancellationToken ct);
    Task<PayslipDetailDto?> GetPayslipAsync(int payslipId, int? requestingEmployeeId, CancellationToken ct);
    Task<IReadOnlyList<PayslipListItem>> ListPayslipsAsync(int employeeId, bool onlyFinalised, CancellationToken ct);
}
public interface IReportRepository
{
    Task<IReadOnlyList<ReportTable>> RunAsync(ReportKind kind, IReadOnlyDictionary<string, object?> args, CancellationToken ct);
    Task<DashboardDto> GetDashboardAsync(int? managerEmployeeId, CancellationToken ct);
}
