namespace Payroll.Core.Models;

public sealed record PayrollRunDto(int PayrollRunId, int PeriodYear, int PeriodMonth, string Status, int EmployeeCount, decimal TotalGross,
    decimal TotalDeductions, decimal TotalNet, decimal TotalEmployerCost, DateTime? ProcessedAt, DateTime? ApprovedAt, DateTime? PaidAt)
{
    public string Period => $"{new DateTime(PeriodYear, PeriodMonth, 1):MMMM yyyy}";
}
public sealed record PayslipListItem(int PayslipId, int PeriodYear, int PeriodMonth, decimal GrossEarnings, decimal TotalDeductions, decimal NetPay, string RunStatus);
public sealed record PayslipLineDto(string LineType, string Code, string Description, decimal Amount, bool IsStatutory);
public sealed class PayslipDetailDto
{
    public int PayslipId { get; set; }
    public int PayrollRunId { get; set; }
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public string RunStatus { get; set; } = "";
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Department { get; set; } = "";
    public string Designation { get; set; } = "";
    public string? TaxIdNumber { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountMasked { get; set; }
    public DateOnly JoinDate { get; set; }
    public decimal BasicSalary { get; set; }
    public int CalendarDays { get; set; }
    public decimal WorkingDays { get; set; }
    public decimal PaidDays { get; set; }
    public decimal LopDays { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal OvertimePay { get; set; }
    public decimal GrossEarnings { get; set; }
    public decimal TaxableIncome { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetPay { get; set; }
    public decimal EmployerContributions { get; set; }
    public List<PayslipLineDto> Lines { get; set; } = new();
    public string Period => $"{new DateTime(PeriodYear, PeriodMonth, 1):MMMM yyyy}";
}
public sealed record PayrollProcessingResult(int RunId, int Processed, decimal TotalGross, decimal TotalNet, IReadOnlyList<string> Warnings);

// ---- calculation inputs / outputs (pure data, no DB types) ----
public sealed class PayrollPeriod
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int FinancialYear { get; init; }
    public DateOnly FirstDay => new(Year, Month, 1);
    public DateOnly LastDay => new(Year, Month, DateTime.DaysInMonth(Year, Month));
    public int CalendarDays => DateTime.DaysInMonth(Year, Month);
}
public sealed record CalcComponent(string Code, string Name, string ComponentType, string CalcMethod, bool IsTaxable, decimal Value);
public sealed record CalcLoan(int LoanId, string LoanType, decimal Installment, decimal Outstanding);
public sealed record CalcLeave(DateOnly From, DateOnly To, bool IsHalfDay, decimal Days);
public sealed record StatutoryRuleDto(string Code, string Name, decimal EmployeeRatePercent, decimal EmployerRatePercent, decimal? WageCeiling, string BaseOn, bool IsPreTax);
public sealed record TaxSlabDto(decimal From, decimal? To, decimal RatePercent);
public sealed record TaxParameterDto(decimal AnnualStandardDeduction, decimal CessPercent);

public sealed class EmployeePayrollInput
{
    public int EmployeeId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public int DepartmentId { get; init; }
    public DateOnly JoinDate { get; init; }
    public DateOnly? ExitDate { get; init; }
    public decimal? BasicSalary { get; init; }
    public decimal StandardHoursPerMonth { get; init; } = 160;
    public bool OvertimeEligible { get; init; }
    public decimal OvertimeHours { get; set; }
    public decimal OvertimeWeightedHours { get; set; }
    public List<CalcComponent> Components { get; } = new();
    public List<CalcLoan> Loans { get; } = new();
    public List<CalcLeave> UnpaidLeaves { get; } = new();
}
public sealed class PayrollInputSet
{
    public int RunId { get; init; }
    public string RunStatus { get; init; } = "";
    public PayrollPeriod Period { get; init; } = new();
    public List<EmployeePayrollInput> Employees { get; } = new();
    public HashSet<DateOnly> Holidays { get; } = new();
    public List<StatutoryRuleDto> Statutory { get; } = new();
    public List<TaxSlabDto> Slabs { get; } = new();
    public TaxParameterDto? TaxParameter { get; set; }
}
public sealed record PayslipLineResult(string LineType, string Code, string Description, decimal Amount, bool IsStatutory, int? ReferenceId, short SortOrder);
public sealed class PayslipResult
{
    public int EmployeeId { get; init; }
    public string EmployeeCode { get; init; } = "";
    public int DepartmentId { get; init; }
    public decimal BasicSalary { get; set; }
    public int CalendarDays { get; set; }
    public decimal WorkingDays { get; set; }
    public decimal PaidDays { get; set; }
    public decimal LopDays { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal OvertimePay { get; set; }
    public decimal GrossEarnings { get; set; }
    public decimal TaxableIncome { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetPay { get; set; }
    public decimal EmployerContributions { get; set; }
    public List<PayslipLineResult> Lines { get; } = new();
    public List<string> Warnings { get; } = new();
}
