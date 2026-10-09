using System.ComponentModel.DataAnnotations;
namespace Payroll.Core.Models;

public sealed record LeaveTypeDto(int LeaveTypeId, string Code, string Name, decimal AnnualEntitlement, string AccrualMethod, decimal MaxCarryForward, bool IsPaid, bool AllowHalfDay, int? MaxConsecutiveDays);
public sealed record LeaveBalanceDto(int LeaveTypeId, string Code, string Name, bool IsPaid, int Year, decimal Opening, decimal Accrued, decimal Adjusted, decimal Used, decimal Balance, decimal PendingDays);
public sealed record LeaveRequestDto(int LeaveRequestId, int EmployeeId, string EmployeeName, string LeaveType, DateOnly FromDate, DateOnly ToDate, bool IsHalfDay, decimal Days, string? Reason, string Status, string? DecisionRemarks, DateTime CreatedAt);
public sealed record LeaveLedgerDto(long LedgerId, string LeaveType, int Year, int? Month, string TxnType, decimal Days, int? LeaveRequestId, string? Remarks, DateTime CreatedAt);
public class LeaveRequestInput
{
    [Required, Display(Name = "Leave type")] public int LeaveTypeId { get; set; }
    [Required, Display(Name = "From")] public DateOnly? FromDate { get; set; }
    [Required, Display(Name = "To")] public DateOnly? ToDate { get; set; }
    [Display(Name = "Half day")] public bool IsHalfDay { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
}
public class LeaveAdjustInput
{
    [Required, Display(Name = "Employee")] public int EmployeeId { get; set; }
    [Required, Display(Name = "Leave type")] public int LeaveTypeId { get; set; }
    [Range(2000, 2100)] public int Year { get; set; } = DateTime.UtcNow.Year;
    [Display(Name = "Days (+/-)")] public decimal Days { get; set; }
    [Required, StringLength(300)] public string Remarks { get; set; } = "";
}
