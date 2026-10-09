using System.Text;
using Microsoft.AspNetCore.Mvc;
using Payroll.Core.Models;
namespace Payroll.Web.Infrastructure;

public sealed class CsvResult : IActionResult
{
    private readonly IReadOnlyList<ReportTable> _tables;
    private readonly string _fileName;
    public CsvResult(IReadOnlyList<ReportTable> tables, string fileName) => (_tables, _fileName) = (tables, fileName);

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var res = context.HttpContext.Response;
        res.ContentType = "text/csv; charset=utf-8";
        res.Headers.ContentDisposition = $"attachment; filename=\"{_fileName}\"";
        var sb = new StringBuilder("\uFEFF");
        foreach (var t in _tables)
        {
            sb.AppendLine(Escape(t.Title));
            sb.AppendLine(string.Join(',', t.Columns.Select(Escape)));
            foreach (var row in t.Rows) sb.AppendLine(string.Join(',', row.Select(Cell)));
            sb.AppendLine();
        }
        await res.WriteAsync(sb.ToString(), Encoding.UTF8);
    }

    private static string Cell(object? v) => v switch
    {
        null => "",
        decimal d => d.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => Escape(v.ToString() ?? "")
    };

    private static string Escape(string s)
    {
        if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;      // neutralise spreadsheet formula injection
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
