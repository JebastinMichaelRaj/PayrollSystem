using System.Data;
using Payroll.Core.Abstractions;
using Payroll.Core.Models;
using Payroll.Data.Infrastructure;
namespace Payroll.Data.Repositories;

public sealed class EmployeeRepository : IEmployeeRepository
{
    private readonly ISqlDatabase _db;
    public EmployeeRepository(ISqlDatabase db) => _db = db;

    public async Task<EmployeeLookups> GetLookupsAsync(CancellationToken ct)
    {
        List<LookupItem> d = new(), g = new(), e = new();
        await _db.QueryMultipleAsync("dbo.usp_Lookup_All", null, async (r, token) =>
        {
            while (await r.ReadAsync(token)) d.Add(new LookupItem(r.Int("Id"), r.Str("Name")));
            await r.NextResultAsync(token);
            while (await r.ReadAsync(token)) g.Add(new LookupItem(r.Int("Id"), r.Str("Name")));
            await r.NextResultAsync(token);
            while (await r.ReadAsync(token)) e.Add(new LookupItem(r.Int("Id"), r.Str("Name")));
        }, ct);
        return new EmployeeLookups(d, g, e);
    }

    public Task<PagedResult<EmployeeListItem>> SearchAsync(string? search, int? departmentId, bool? isActive, int page, int pageSize, CancellationToken ct) =>
        _db.QueryPagedAsync("dbo.usp_Employee_Search",
            r => new EmployeeListItem(r.Int("EmployeeId"), r.Str("EmployeeCode"), r.Str("FullName"), r.Str("Email"), r.Str("Department"), r.Str("Designation"), r.Date("JoinDate"), r.Bool("IsActive")),
            new[] { Param.Str("Search", search, 100), Param.In("DepartmentId", SqlDbType.Int, departmentId), Param.In("IsActive", SqlDbType.Bit, isActive),
                    Param.In("PageNumber", SqlDbType.Int, page), Param.In("PageSize", SqlDbType.Int, pageSize) }, page, pageSize, ct);

    public Task<EmployeeDetail?> GetByIdAsync(int id, CancellationToken ct) =>
        _db.QuerySingleAsync("dbo.usp_Employee_GetById", r => new EmployeeDetail
        {
            EmployeeId = r.Int("EmployeeId"), EmployeeCode = r.Str("EmployeeCode"), FirstName = r.Str("FirstName"), LastName = r.Str("LastName"),
            Email = r.Str("Email"), Phone = r.NStr("Phone"), DateOfBirth = r.Date("DateOfBirth"), JoinDate = r.Date("JoinDate"), ExitDate = r.NDate("ExitDate"),
            DepartmentId = r.Int("DepartmentId"), Department = r.Str("Department"), DesignationId = r.Int("DesignationId"), Designation = r.Str("Designation"),
            ManagerId = r.NInt("ManagerId"), ManagerName = r.NStr("ManagerName"), EmploymentType = r.Str("EmploymentType"), TaxIdNumber = r.NStr("TaxIdNumber"),
            BankName = r.NStr("BankName"), BankAccountNumber = r.NStr("BankAccountNumber"), IsActive = r.Bool("IsActive"), RowVersion = r.Bytes("RowVer")
        }, new[] { Param.In("EmployeeId", SqlDbType.Int, id) }, ct);

    private static List<Param> Fields(EmployeeInput i) => new()
    {
        Param.Str("FirstName", i.FirstName, 60), Param.Str("LastName", i.LastName, 60), Param.Str("Email", i.Email, 256), Param.Str("Phone", i.Phone, 30),
        Param.Date("DateOfBirth", i.DateOfBirth), Param.Date("JoinDate", i.JoinDate), Param.In("DepartmentId", SqlDbType.Int, i.DepartmentId),
        Param.In("DesignationId", SqlDbType.Int, i.DesignationId), Param.In("ManagerId", SqlDbType.Int, i.ManagerId), Param.Str("EmploymentType", i.EmploymentType, 20),
        Param.Str("TaxIdNumber", i.TaxIdNumber, 30), Param.Str("BankName", i.BankName, 100), Param.Str("BankAccountNumber", i.BankAccountNumber, 40)
    };

    public async Task<int> CreateAsync(EmployeeInput input, int actingUserId, CancellationToken ct)
    {
        var ps = Fields(input);
        var id = Param.Out("NewEmployeeId", SqlDbType.Int);
        ps.Add(Param.In("ActingUserId", SqlDbType.Int, actingUserId)); ps.Add(id);
        await _db.ExecuteAsync("dbo.usp_Employee_Create", ps, ct);
        return (int)id.Value!;
    }

    public Task UpdateAsync(EmployeeInput input, int actingUserId, CancellationToken ct)
    {
        var ps = Fields(input);
        ps.Add(Param.In("EmployeeId", SqlDbType.Int, input.EmployeeId));
        ps.Add(Param.In("RowVer", SqlDbType.VarBinary, input.RowVersion, 8));
        ps.Add(Param.In("ActingUserId", SqlDbType.Int, actingUserId));
        return _db.ExecuteAsync("dbo.usp_Employee_Update", ps, ct);
    }

    public Task TerminateAsync(int id, DateOnly exitDate, int actingUserId, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_Employee_Terminate", new[] { Param.In("EmployeeId", SqlDbType.Int, id), Param.Date("ExitDate", exitDate), Param.In("ActingUserId", SqlDbType.Int, actingUserId) }, ct);
}
