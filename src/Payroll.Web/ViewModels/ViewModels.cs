using System.ComponentModel.DataAnnotations;
using Payroll.Core.Models;
namespace Payroll.Web.ViewModels;

public class LoginViewModel
{
    [Required] public string Username { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
}
public class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")] public string CurrentPassword { get; set; } = "";
    [Required, DataType(DataType.Password), Display(Name = "New password")] public string NewPassword { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword)), Display(Name = "Confirm new password")] public string ConfirmPassword { get; set; } = "";
}

public sealed record PagerModel(int Page, int TotalPages, string Action, string Controller, IDictionary<string, string?> Query);

public sealed class EmployeeListViewModel
{
    public string? Search { get; set; }
    public int? DepartmentId { get; set; }
    public bool? IsActive { get; set; } = true;
    public int Page { get; set; } = 1;
    public PagedResult<EmployeeListItem> Result { get; set; } = new(Array.Empty<EmployeeListItem>(), 0, 1, 25);
    public EmployeeLookups Lookups { get; set; } = new(Array.Empty<LookupItem>(), Array.Empty<LookupItem>(), Array.Empty<LookupItem>());
}
public sealed class EmployeeFormViewModel
{
    public EmployeeInput Input { get; set; } = new();
    public EmployeeLookups Lookups { get; set; } = new(Array.Empty<LookupItem>(), Array.Empty<LookupItem>(), Array.Empty<LookupItem>());
    public bool IsEdit => Input.EmployeeId != 0;
}
public sealed class EmployeeDetailsViewModel
{
    public EmployeeDetail Employee { get; set; } = new();
    public IReadOnlyList<SalaryStructureDto> Salary { get; set; } = Array.Empty<SalaryStructureDto>();
}
public sealed class SalaryManageViewModel
{
    public EmployeeDetail Employee { get; set; } = new();
    public IReadOnlyList<SalaryStructureDto> History { get; set; } = Array.Empty<SalaryStructureDto>();
    public IReadOnlyList<EmployeeComponentDto> Components { get; set; } = Array.Empty<EmployeeComponentDto>();
    public IReadOnlyList<SalaryComponentDef> Definitions { get; set; } = Array.Empty<SalaryComponentDef>();
    public SalaryInput Salary { get; set; } = new();
    public ComponentAssignInput Assign { get; set; } = new();
}
public sealed class OvertimeListViewModel
{
    public string? Status { get; set; }
    public PagedResult<OvertimeDto> Result { get; set; } = new(Array.Empty<OvertimeDto>(), 0, 1, 25);
    public OvertimeInput Input { get; set; } = new();
    public IReadOnlyList<LookupItem> Employees { get; set; } = Array.Empty<LookupItem>();
}
public sealed class LoansViewModel
{
    public string? Status { get; set; }
    public IReadOnlyList<LoanDto> Loans { get; set; } = Array.Empty<LoanDto>();
    public LoanInput Input { get; set; } = new();
    public IReadOnlyList<LookupItem> Employees { get; set; } = Array.Empty<LookupItem>();
}
public sealed class HolidaysViewModel
{
    public int Year { get; set; }
    public IReadOnlyList<HolidayDto> Holidays { get; set; } = Array.Empty<HolidayDto>();
}
public sealed class LeaveIndexViewModel
{
    public int Year { get; set; }
    public IReadOnlyList<LeaveBalanceDto> Balances { get; set; } = Array.Empty<LeaveBalanceDto>();
    public PagedResult<LeaveRequestDto> Requests { get; set; } = new(Array.Empty<LeaveRequestDto>(), 0, 1, 25);
    public IReadOnlyList<LeaveTypeDto> Types { get; set; } = Array.Empty<LeaveTypeDto>();
    public LeaveRequestInput Input { get; set; } = new();
}
public sealed class LeaveAdminViewModel
{
    public IReadOnlyList<LookupItem> Employees { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LeaveTypeDto> Types { get; set; } = Array.Empty<LeaveTypeDto>();
    public LeaveAdjustInput Adjust { get; set; } = new();
    public int Year { get; set; } = DateTime.UtcNow.Year;
    public int Month { get; set; } = DateTime.UtcNow.Month;
}
public sealed class LedgerViewModel
{
    public int EmployeeId { get; set; }
    public int Year { get; set; }
    public IReadOnlyList<LeaveLedgerDto> Entries { get; set; } = Array.Empty<LeaveLedgerDto>();
    public IReadOnlyList<LeaveBalanceDto> Balances { get; set; } = Array.Empty<LeaveBalanceDto>();
}
public sealed class PayrollIndexViewModel
{
    public IReadOnlyList<PayrollRunDto> Runs { get; set; } = Array.Empty<PayrollRunDto>();
    public int Year { get; set; } = DateTime.UtcNow.Year;
    public int Month { get; set; } = DateTime.UtcNow.Month;
}
public sealed class PayrollRunViewModel
{
    public PayrollRunDto Run { get; set; } = null!;
    public ReportTable? Register { get; set; }
    public PagerModel? Pager { get; set; }
    public List<string> Warnings { get; set; } = new();
}
public sealed class ReportsIndexViewModel
{
    public IReadOnlyList<PayrollRunDto> Runs { get; set; } = Array.Empty<PayrollRunDto>();
    public IReadOnlyList<LookupItem> Departments { get; set; } = Array.Empty<LookupItem>();
    public int FinancialYear { get; set; }
    public int Year { get; set; } = DateTime.UtcNow.Year;
}
public sealed class ReportPageViewModel
{
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public IReadOnlyList<ReportTable> Tables { get; set; } = Array.Empty<ReportTable>();
    public PagerModel? Pager { get; set; }
    public string? ExportUrl { get; set; }
}
public sealed class UsersViewModel
{
    public IReadOnlyList<UserListItem> Users { get; set; } = Array.Empty<UserListItem>();
    public UserInput Input { get; set; } = new();
    public IReadOnlyList<LookupItem> Employees { get; set; } = Array.Empty<LookupItem>();
}
public sealed class ErrorViewModel { public string? RequestId { get; set; } public int? StatusCode { get; set; } }
