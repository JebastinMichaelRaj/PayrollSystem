using System.Security.Cryptography;
using Payroll.Core.Abstractions;
namespace Payroll.Core.Security;

/// <summary>PBKDF2-HMAC-SHA256, 600k iterations (OWASP 2023 guidance). Format: v1.{iterations}.{salt}.{hash}.</summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 600_000, SaltSize = 16, KeySize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string hash, string password, out bool needsRehash)
    {
        needsRehash = false;
        var parts = hash.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            var ok = CryptographicOperations.FixedTimeEquals(actual, expected);
            needsRehash = ok && iterations < Iterations;
            return ok;
        }
        catch (FormatException) { return false; }
    }
}
