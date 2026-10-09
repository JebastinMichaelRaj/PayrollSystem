using System.ComponentModel.DataAnnotations;
using Payroll.Core.Domain;
namespace Payroll.Core.Models;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}
public sealed record LookupItem(int Id, string Name);
public sealed record EmployeeLookups(IReadOnlyList<LookupItem> Departments, IReadOnlyList<LookupItem> Designations, IReadOnlyList<LookupItem> Employees);

/// <summary>Generic tabular result used by all reports (rendered as HTML table or exported as CSV).</summary>
public sealed class ReportTable
{
    public string Title { get; init; } = "";
    public List<string> Columns { get; } = new();
    public List<object?[]> Rows { get; } = new();
    public int? TotalRows { get; set; }
}
public enum ReportKind { PayrollRegister, DepartmentSummary, Statutory, Tax, LeaveBalance, Audit }

public sealed record DashboardDto(int ActiveEmployees, int PendingLeaves, int PendingOvertime, int ActiveLoans,
    int? LatestRunId, int? LatestRunYear, int? LatestRunMonth, string? LatestRunStatus, decimal? LatestRunNet);

// ---- Identity ----
public sealed record UserRecord(int UserId, string Username, string PasswordHash, string Role, int? EmployeeId, bool IsActive,
    int FailedLoginCount, DateTime? LockoutEndUtc, bool MustChangePassword);
public sealed record UserListItem(int UserId, string Username, string Role, int? EmployeeId, string? EmployeeName, bool IsActive, DateTime? LastLoginUtc);
public sealed record AuthResult(bool Succeeded, UserRecord? User, string? Error);
public class UserInput
{
    [Required, StringLength(100, MinimumLength = 3)] public string Username { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required] public string Role { get; set; } = Roles.Employee;
    [Display(Name = "Linked employee")] public int? EmployeeId { get; set; }
}
