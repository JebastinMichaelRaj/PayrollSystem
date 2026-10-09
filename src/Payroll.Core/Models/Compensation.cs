using System.ComponentModel.DataAnnotations;
namespace Payroll.Core.Models;

public sealed record SalaryStructureDto(int Id, int EmployeeId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal BasicSalary, decimal StandardHoursPerMonth, bool OvertimeEligible);
public class SalaryInput
{
    public int EmployeeId { get; set; }
    [Required, Display(Name = "Effective from")] public DateOnly? EffectiveFrom { get; set; }
    [Range(0.01, 1_000_000_000), Display(Name = "Basic salary (monthly)")] public decimal BasicSalary { get; set; }
    [Range(1, 400), Display(Name = "Standard hours / month")] public decimal StandardHoursPerMonth { get; set; } = 160;
    [Display(Name = "Overtime eligible")] public bool OvertimeEligible { get; set; }
}
public sealed record SalaryComponentDef(int ComponentId, string Code, string Name, string ComponentType, string CalcMethod, bool IsTaxable);
public sealed record EmployeeComponentDto(int EmployeeComponentId, int ComponentId, string Code, string Name, string ComponentType, string CalcMethod, decimal Value, DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public class ComponentAssignInput
{
    public int EmployeeId { get; set; }
    [Required, Display(Name = "Component")] public int ComponentId { get; set; }
    [Range(0.0001, 1_000_000_000)] public decimal Value { get; set; }
    [Required, Display(Name = "Effective from")] public DateOnly? EffectiveFrom { get; set; }
}

public sealed record OvertimeDto(int OvertimeId, int EmployeeId, string EmployeeName, DateOnly WorkDate, decimal Hours, decimal Multiplier, string Status, string? Remarks);
public class OvertimeInput
{
    [Display(Name = "Employee")] public int? EmployeeId { get; set; }
    [Required, Display(Name = "Work date")] public DateOnly? WorkDate { get; set; }
    [Range(0.25, 16)] public decimal Hours { get; set; }
    [Range(1, 3)] public decimal Multiplier { get; set; } = 1.5m;
    [StringLength(300)] public string? Remarks { get; set; }
}

public sealed record LoanDto(int LoanId, int EmployeeId, string EmployeeName, string LoanType, decimal PrincipalAmount, decimal InstallmentAmount, decimal OutstandingBalance, DateOnly StartDate, string Status);
public class LoanInput
{
    [Required, Display(Name = "Employee")] public int EmployeeId { get; set; }
    [Required, StringLength(40), Display(Name = "Loan type")] public string LoanType { get; set; } = "Salary advance";
    [Range(0.01, 1_000_000_000)] public decimal Principal { get; set; }
    [Range(0.01, 1_000_000_000)] public decimal Installment { get; set; }
    [Required, Display(Name = "First deduction month")] public DateOnly? StartDate { get; set; }
}
public sealed record HolidayDto(DateOnly HolidayDate, string Name);
