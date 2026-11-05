using System.Security.Cryptography;

namespace XNY.Scada.Security;

/// <summary>PBKDF2-SHA256 加盐哈希，格式：迭代次数.盐.哈希（Base64）</summary>
public static class PasswordHasher
{
    /// <summary>迭代次数。</summary>
    private const int Iterations = 100_000;
    /// <summary>盐值大小。</summary>
    private const int SaltSize = 16;
    /// <summary>哈希大小。</summary>
    private const int HashSize = 32;

    /// <summary>计算哈希。</summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>校验。</summary>
    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
