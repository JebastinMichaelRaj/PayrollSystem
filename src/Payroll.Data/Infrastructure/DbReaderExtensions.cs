using Microsoft.Data.SqlClient;
namespace Payroll.Data.Infrastructure;

/// <summary>Name-based, type-tolerant column readers (tinyint/smallint/int/decimal all convert safely).</summary>
internal static class DbReaderExtensions
{
    public static bool IsNull(this SqlDataReader r, string c) => r.IsDBNull(r.GetOrdinal(c));
    public static string Str(this SqlDataReader r, string c) => r.GetString(r.GetOrdinal(c));
    public static string? NStr(this SqlDataReader r, string c) { var o = r.GetOrdinal(c); return r.IsDBNull(o) ? null : r.GetString(o); }
    public static int Int(this SqlDataReader r, string c) => Convert.ToInt32(r.GetValue(r.GetOrdinal(c)));
    public static int? NInt(this SqlDataReader r, string c) { var o = r.GetOrdinal(c); return r.IsDBNull(o) ? null : Convert.ToInt32(r.GetValue(o)); }
    public static long Long(this SqlDataReader r, string c) => Convert.ToInt64(r.GetValue(r.GetOrdinal(c)));
    public static decimal Dec(this SqlDataReader r, string c) => Convert.ToDecimal(r.GetValue(r.GetOrdinal(c)));
    public static decimal? NDec(this SqlDataReader r, string c) { var o = r.GetOrdinal(c); return r.IsDBNull(o) ? null : Convert.ToDecimal(r.GetValue(o)); }
    public static bool Bool(this SqlDataReader r, string c) => r.GetBoolean(r.GetOrdinal(c));
    public static DateOnly Date(this SqlDataReader r, string c) => DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal(c)));
    public static DateOnly? NDate(this SqlDataReader r, string c) { var o = r.GetOrdinal(c); return r.IsDBNull(o) ? null : DateOnly.FromDateTime(r.GetDateTime(o)); }
    public static DateTime Utc(this SqlDataReader r, string c) => DateTime.SpecifyKind(r.GetDateTime(r.GetOrdinal(c)), DateTimeKind.Utc);
    public static DateTime? NUtc(this SqlDataReader r, string c) { var o = r.GetOrdinal(c); return r.IsDBNull(o) ? null : DateTime.SpecifyKind(r.GetDateTime(o), DateTimeKind.Utc); }
    public static byte[] Bytes(this SqlDataReader r, string c) => (byte[])r.GetValue(r.GetOrdinal(c));
}
