using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payroll.Core.Abstractions;
using Payroll.Core.Calculation;
using Payroll.Core.Configuration;
using Payroll.Core.Domain;
using Payroll.Core.Models;
namespace Payroll.Core.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string username, string password, string? ip, CancellationToken ct = default);
    Task ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default);
    Task<int> CreateUserAsync(UserInput input, CancellationToken ct = default);
    Task<int> CreateBootstrapAdminAsync(string username, string password, CancellationToken ct = default);
    Task<IReadOnlyList<UserListItem>> ListUsersAsync(CancellationToken ct = default);
    Task SetActiveAsync(int userId, bool active, CancellationToken ct = default);
}

public sealed class AuthService : IAuthService
{
    private const string GenericFailure = "Invalid username or password.";
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ICurrentUser _current;
    private readonly PayrollOptions _opt;
    private readonly ILogger<AuthService> _log;
    private static string? _dummyHash;

    public AuthService(IUserRepository users, IPasswordHasher hasher, ICurrentUser current, IOptions<PayrollOptions> opt, ILogger<AuthService> log)
        => (_users, _hasher, _current, _opt, _log) = (users, hasher, current, opt.Value, log);

    public async Task<AuthResult> LoginAsync(string username, string password, string? ip, CancellationToken ct = default)
    {
        username = (username ?? "").Trim();
        var user = await _users.GetByUsernameAsync(username, ct);
        if (user is null)
        {
            // equalise timing so unknown usernames cannot be enumerated
            _hasher.Verify(_dummyHash ??= _hasher.Hash("dummy-password"), password ?? "", out _);
            _log.LogWarning("Login failed for unknown user {Username} from {Ip}", username, ip);
            return new AuthResult(false, null, GenericFailure);
        }
        if (user.LockoutEndUtc is { } until && until > DateTime.UtcNow)
        {
            _log.LogWarning("Login blocked: user {UserId} is locked until {Until}", user.UserId, until);
            return new AuthResult(false, null, "This account is temporarily locked. Try again later.");
        }
        var ok = _hasher.Verify(user.PasswordHash, password ?? "", out var rehash);
        if (!ok || !user.IsActive)
        {
            await _users.RegisterLoginAsync(user.UserId, false, _opt.MaxFailedLogins, _opt.LockoutMinutes, ip, ct);
            _log.LogWarning("Login failed for user {UserId} from {Ip}", user.UserId, ip);
            return new AuthResult(false, null, GenericFailure);
        }
        await _users.RegisterLoginAsync(user.UserId, true, _opt.MaxFailedLogins, _opt.LockoutMinutes, ip, ct);
        if (rehash) await _users.ChangePasswordAsync(user.UserId, _hasher.Hash(password!), ct);   // transparent upgrade of old hashes
        return new AuthResult(true, user, null);
    }

    public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException();
        if (!_hasher.Verify(user.PasswordHash, currentPassword ?? "", out _)) throw new ValidationFailedException("CurrentPassword", "Current password is incorrect.");
        ValidatePasswordStrength(newPassword, "NewPassword");
        await _users.ChangePasswordAsync(userId, _hasher.Hash(newPassword), ct);
    }

    public async Task<int> CreateUserAsync(UserInput input, CancellationToken ct = default)
    {
        _current.Require(Roles.Admin);
        var errors = ModelValidator.Validate(input);
        if (!Roles.All.Contains(input.Role)) errors.Add(nameof(input.Role), "Unknown role.");
        errors.ThrowIfAny();
        ValidatePasswordStrength(input.Password, nameof(input.Password));
        return await _users.CreateAsync(input.Username.Trim(), _hasher.Hash(input.Password), input.Role, input.EmployeeId, true, _current.UserId, ct);
    }

    /// <summary>Used once by the start-up seeder (no authenticated user exists yet).</summary>
    public Task<int> CreateBootstrapAdminAsync(string username, string password, CancellationToken ct = default)
    {
        ValidatePasswordStrength(password, "Password");
        return _users.CreateAsync(username, _hasher.Hash(password), Roles.Admin, null, true, null, ct);
    }

    public async Task<IReadOnlyList<UserListItem>> ListUsersAsync(CancellationToken ct = default)
    {
        _current.Require(Roles.Admin);
        return await _users.ListAsync(ct);
    }

    public Task SetActiveAsync(int userId, bool active, CancellationToken ct = default)
    {
        _current.Require(Roles.Admin);
        return _users.SetActiveAsync(userId, active, _current.UserId, ct);
    }

    private void ValidatePasswordStrength(string? pw, string field)
    {
        var errors = new ValidationErrors();
        pw ??= "";
        if (pw.Length < _opt.MinPasswordLength) errors.Add(field, $"Password must be at least {_opt.MinPasswordLength} characters.");
        if (!pw.Any(char.IsUpper) || !pw.Any(char.IsLower) || !pw.Any(char.IsDigit) || pw.All(char.IsLetterOrDigit))
            errors.Add(field, "Password must contain upper-case, lower-case, a digit and a symbol.");
        errors.ThrowIfAny();
    }
}
