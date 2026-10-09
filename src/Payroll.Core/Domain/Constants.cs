namespace Payroll.Core.Domain;

public static class Roles
{
    public const string Admin = "Admin", Hr = "HR", Payroll = "Payroll", Manager = "Manager", Employee = "Employee";
    public static readonly string[] All = { Admin, Hr, Payroll, Manager, Employee };
}

public static class RunStatus { public const string Open = "Open", Processed = "Processed", Approved = "Approved", Paid = "Paid"; }
public static class LineType { public const string Earning = "Earning", Deduction = "Deduction", Employer = "Employer"; }
public static class CalcMethod { public const string Fixed = "Fixed", PercentOfBasic = "PercentOfBasic"; }
public static class ComponentKind { public const string Earning = "Earning", Deduction = "Deduction"; }
public static class LeaveStatus { public const string Pending = "Pending", Approved = "Approved", Rejected = "Rejected", Cancelled = "Cancelled"; }
public static class ApprovalStatus { public const string Pending = "Pending", Approved = "Approved", Rejected = "Rejected"; }
public static class LineCodes { public const string Basic = "BASIC", Overtime = "OVERTIME", Tax = "TDS", Loan = "LOAN"; }
