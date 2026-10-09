using System.Data;
using Payroll.Core.Abstractions;
using Payroll.Core.Models;
using Payroll.Data.Infrastructure;
namespace Payroll.Data.Repositories;

public sealed class ReportRepository : IReportRepository
{
    private readonly ISqlDatabase _db;
    public ReportRepository(ISqlDatabase db) => _db = db;

    /// <summary>Whitelist: procedure names are never taken from user input.</summary>
    private static readonly Dictionary<ReportKind, (string Proc, string[] Titles)> Map = new()
    {
        [ReportKind.PayrollRegister] = ("dbo.usp_Report_PayrollRegister", new[] { "Payroll register" }),
        [ReportKind.DepartmentSummary] = ("dbo.usp_Report_DepartmentSummary", new[] { "Departmental payroll summary" }),
        [ReportKind.Statutory] = ("dbo.usp_Report_Statutory", new[] { "Statutory remittance summary", "Statutory detail by employee" }),
        [ReportKind.Tax] = ("dbo.usp_Report_Tax", new[] { "Tax deducted at source" }),
        [ReportKind.LeaveBalance] = ("dbo.usp_Report_LeaveBalance", new[] { "Leave balances" }),
        [ReportKind.Audit] = ("dbo.usp_Audit_Search", new[] { "Audit trail" }),
    };

    public async Task<IReadOnlyList<ReportTable>> RunAsync(ReportKind kind, IReadOnlyDictionary<string, object?> args, CancellationToken ct)
    {
        var (proc, titles) = Map[kind];
        var ps = args.Select(a => Param.Of(a.Key, a.Value)).ToList();
        var tables = new List<ReportTable>();
        await _db.QueryMultipleAsync(proc, ps, async (r, token) =>
        {
            var index = 0;
            do
            {
                var table = new ReportTable { Title = index < titles.Length ? titles[index] : $"Result {index + 1}" };
                var hidden = -1;
                for (var i = 0; i < r.FieldCount; i++)
                {
                    if (r.GetName(i) == "TotalCount") hidden = i; else table.Columns.Add(r.GetName(i));
                }
                while (await r.ReadAsync(token))
                {
                    var row = new List<object?>(r.FieldCount);
                    for (var i = 0; i < r.FieldCount; i++)
                    {
                        if (i == hidden) { table.TotalRows ??= Convert.ToInt32(r.GetValue(i)); continue; }
                        row.Add(r.IsDBNull(i) ? null : r.GetValue(i));
                    }
                    table.Rows.Add(row.ToArray());
                }
                tables.Add(table);
                index++;
            } while (await r.NextResultAsync(token));
        }, ct);
        return tables;
    }

    public async Task<DashboardDto> GetDashboardAsync(int? managerEmployeeId, CancellationToken ct) =>
        (await _db.QueryAsync("dbo.usp_Dashboard_Summary", r => new DashboardDto(r.Int("ActiveEmployees"), r.Int("PendingLeaves"), r.Int("PendingOvertime"), r.Int("ActiveLoans"),
            r.NInt("LatestRunId"), r.NInt("LatestRunYear"), r.NInt("LatestRunMonth"), r.NStr("LatestRunStatus"), r.NDec("LatestRunNet")),
            new[] { Param.In("ManagerEmployeeId", SqlDbType.Int, managerEmployeeId) }, ct)).First();
}
