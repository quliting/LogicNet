using Furion.DataEncryption.Extensions;
using LogicNet.Core.Security;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     密码哈希：加盐 PBKDF2、历史无盐 MD5 兼容、升级判定（P0-3 / P1-4 覆盖项）
/// </summary>
public class PasswordHasherTests
{
    [Fact]
    public void Hash_产出自描述格式且可校验()
    {
        var hash = PasswordHasher.Hash("Abc12345");
        Assert.StartsWith("PBKDF2$", hash);
        Assert.True(PasswordHasher.Verify("Abc12345", hash));
    }

    [Fact]
    public void 同一密码两次哈希不同_随机盐()
    {
        Assert.NotEqual(PasswordHasher.Hash("Abc12345"), PasswordHasher.Hash("Abc12345"));
    }

    [Fact]
    public void 错误密码或篡改哈希校验失败()
    {
        var hash = PasswordHasher.Hash("Abc12345");
        Assert.False(PasswordHasher.Verify("Abc12346", hash));
        Assert.False(PasswordHasher.Verify("Abc12345", hash + "x"));
        Assert.False(PasswordHasher.Verify("Abc12345", "not-a-hash"));
        Assert.False(PasswordHasher.Verify("", hash));
        Assert.False(PasswordHasher.Verify("Abc12345", null));
    }

    [Fact]
    public void 历史无盐MD5存量可校验且需升级()
    {
        var legacyMd5 = "Abc12345".ToMD5Encrypt(false, false);
        Assert.Equal(32, legacyMd5.Length);
        Assert.True(PasswordHasher.Verify("Abc12345", legacyMd5));
        Assert.True(PasswordHasher.NeedsUpgrade(legacyMd5));
    }

    [Fact]
    public void 新格式无需升级()
    {
        Assert.False(PasswordHasher.NeedsUpgrade(PasswordHasher.Hash("Abc12345")));
    }
}
