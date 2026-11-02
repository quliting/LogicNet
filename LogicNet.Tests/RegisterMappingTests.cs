using LogicNet.Application.UserInfo.DTO;
using Mapster;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     注册映射：AddUserInfoInputDto -> UserInfo，密码须经 PasswordHasher 加盐（P0-3 修复的映射漏洞回归）
/// </summary>
public class RegisterMappingTests
{
    private static TypeAdapterConfig CreateConfig()
    {
        var config = new TypeAdapterConfig();
        // 扫描 LogicNet.Application 中的 IRegister 映射配置（等效于宿主 AddObjectMapper 的扫描结果）
        config.Scan(typeof(Mapper).Assembly);
        return config;
    }

    [Fact]
    public void 注册映射时密码加盐哈希而非明文或MD5()
    {
        var dto = new AddUserInfoInputDto
        {
            UserName = "testuser8",
            Name = "测试用户",
            Mobile = "13800138000",
            PassWord = "Abc12345"
        };

        var entity = dto.Adapt<LogicNet.Core.Entity.UserInfo>(CreateConfig());

        Assert.Equal(dto.UserName, entity.UserName);
        Assert.Equal(dto.Mobile, entity.Mobile);
        Assert.StartsWith("PBKDF2$", entity.Password);
        Assert.NotEqual(dto.PassWord, entity.Password);
        Assert.True(LogicNet.Core.Security.PasswordHasher.Verify("Abc12345", entity.Password));
    }
}
