using System.Globalization;
namespace Payroll.Web.Infrastructure;

public static class Fmt
{
    public static string Money(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);
    public static string Num(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    public static string Date(DateOnly d) => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    public static string Date(DateOnly? d) => d is null ? "-" : Date(d.Value);
    public static string DateTime(DateTime d) => d.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC";
    public static string Month(int year, int month) => new DateTime(year, month, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture);
    public static string Cell(object? v) => v switch
    {
        null => "", decimal d => Money(d), DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"), _ => v.ToString() ?? ""
    };
    public static string Badge(string status) => status switch
    {
        "Approved" or "Paid" or "Active" => "ok", "Pending" or "Open" or "Processed" => "warn", "Rejected" or "Cancelled" or "Closed" => "off", _ => ""
    };
}
