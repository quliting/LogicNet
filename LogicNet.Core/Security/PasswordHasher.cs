using System;
using System.Linq;
using System.Security.Cryptography;
using Furion.DataEncryption.Extensions;

namespace LogicNet.Core.Security;

/// <summary>
///     密码哈希（PBKDF2-SHA256 随机盐）。历史无盐 MD5 存储可校验，校验通过后应透明迁移为新格式。
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 100000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Prefix = "PBKDF2";

    /// <summary>
    ///     生成加盐哈希，格式：PBKDF2$迭代次数$盐(Base64)$哈希(Base64)
    /// </summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    ///     校验密码。兼容历史无盐 MD5（32 位十六进制）存储。
    /// </summary>
    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash)) return false;

        // 历史无盐 MD5：32 位十六进制字符串（MySQL 默认排序规则下原比较不区分大小写，此处保持一致）
        if (IsLegacyMd5(storedHash))
            return string.Equals(password.ToMD5Encrypt(false, false), storedHash, StringComparison.OrdinalIgnoreCase);

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations)) return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    ///     存储值是否需要升级为加盐哈希（历史 MD5 需迁移）
    /// </summary>
    public static bool NeedsUpgrade(string storedHash) =>
        !string.IsNullOrEmpty(storedHash) && IsLegacyMd5(storedHash);

    private static bool IsLegacyMd5(string storedHash) =>
        storedHash.Length == 32 && storedHash.All(char.IsAsciiHexDigit);
}
