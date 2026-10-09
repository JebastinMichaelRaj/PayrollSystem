using System.Data;
using Payroll.Core.Abstractions;
using Payroll.Core.Models;
using Payroll.Data.Infrastructure;
namespace Payroll.Data.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly ISqlDatabase _db;
    public UserRepository(ISqlDatabase db) => _db = db;

    private static UserRecord Map(Microsoft.Data.SqlClient.SqlDataReader r) => new(r.Int("UserId"), r.Str("Username"), r.Str("PasswordHash"), r.Str("Role"),
        r.NInt("EmployeeId"), r.Bool("IsActive"), r.Int("FailedLoginCount"), r.NUtc("LockoutEndUtc"), r.Bool("MustChangePassword"));

    public Task<UserRecord?> GetByUsernameAsync(string username, CancellationToken ct) =>
        _db.QuerySingleAsync("dbo.usp_User_GetByUsername", Map, new[] { Param.Str("Username", username, 100) }, ct);

    public Task<UserRecord?> GetByIdAsync(int userId, CancellationToken ct) =>
        _db.QuerySingleAsync("dbo.usp_User_GetById", Map, new[] { Param.In("UserId", SqlDbType.Int, userId) }, ct);

    public Task RegisterLoginAsync(int userId, bool success, int maxFailures, int lockoutMinutes, string? ip, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_User_RegisterLogin", new[]
        {
            Param.In("UserId", SqlDbType.Int, userId), Param.In("Success", SqlDbType.Bit, success), Param.In("MaxFailures", SqlDbType.Int, maxFailures),
            Param.In("LockoutMinutes", SqlDbType.Int, lockoutMinutes), Param.Str("IpAddress", ip, 45)
        }, ct);

    public async Task<int> CreateAsync(string username, string passwordHash, string role, int? employeeId, bool mustChangePassword, int? actingUserId, CancellationToken ct)
    {
        var id = Param.Out("NewUserId", SqlDbType.Int);
        await _db.ExecuteAsync("dbo.usp_User_Create", new[]
        {
            Param.Str("Username", username, 100), Param.Str("PasswordHash", passwordHash, 500), Param.Str("Role", role, 30),
            Param.In("EmployeeId", SqlDbType.Int, employeeId), Param.In("MustChangePassword", SqlDbType.Bit, mustChangePassword),
            Param.In("ActingUserId", SqlDbType.Int, actingUserId), id
        }, ct);
        return (int)id.Value!;
    }

    public Task ChangePasswordAsync(int userId, string newHash, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_User_ChangePassword", new[] { Param.In("UserId", SqlDbType.Int, userId), Param.Str("NewHash", newHash, 500) }, ct);

    public async Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken ct) =>
        await _db.QueryAsync("dbo.usp_User_List", r => new UserListItem(r.Int("UserId"), r.Str("Username"), r.Str("Role"), r.NInt("EmployeeId"),
            r.NStr("EmployeeName"), r.Bool("IsActive"), r.NUtc("LastLoginUtc")), null, ct);

    public Task SetActiveAsync(int userId, bool isActive, int actingUserId, CancellationToken ct) =>
        _db.ExecuteAsync("dbo.usp_User_SetActive", new[]
        { Param.In("UserId", SqlDbType.Int, userId), Param.In("IsActive", SqlDbType.Bit, isActive), Param.In("ActingUserId", SqlDbType.Int, actingUserId) }, ct);
}
