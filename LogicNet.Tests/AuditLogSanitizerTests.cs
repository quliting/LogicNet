using LogicNet.Core.Audit;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     脱敏与截断：密码、访问/刷新令牌、授权头、验证码不得以明文进入审计内容
/// </summary>
public class AuditLogSanitizerTests
{
    private const string Password = "S3cretPass!987";
    private const string AccessToken = "AT-xyz-555";
    private const string RefreshToken = "RT-xyz-777";
    private const string Authorization = "Bearer AUTH-999";
    private const string CaptchaCode = "C0DE-246";

    [Fact]
    public void JSON请求体中的密码与令牌被掩码()
    {
        var body = $"{{\"userName\":\"u1\",\"passWord\":\"{Password}\",\"accessToken\":\"{AccessToken}\"," +
                   $"\"refreshToken\":\"{RefreshToken}\",\"authorization\":\"{Authorization}\"," +
                   $"\"code\":\"{CaptchaCode}\",\"name\":\"保留字段\"}}";

        var result = AuditLogSanitizer.SanitizeBody(body, 2048);

        Assert.DoesNotContain(Password, result);
        Assert.DoesNotContain(AccessToken, result);
        Assert.DoesNotContain(RefreshToken, result);
        Assert.DoesNotContain(Authorization, result);
        Assert.DoesNotContain(CaptchaCode, result);
        Assert.Contains("保留字段", result);
        Assert.Contains("\"passWord\":\"***\"", result);
        Assert.Contains("\"code\":\"***\"", result);
    }

    [Fact]
    public void 表单请求体中的密码与验证码被掩码()
    {
        var body = $"userName=u1&password={Password}&accessToken={AccessToken}&code={CaptchaCode}";

        var result = AuditLogSanitizer.SanitizeBody(body, 2048);

        Assert.DoesNotContain(Password, result);
        Assert.DoesNotContain(AccessToken, result);
        Assert.DoesNotContain(CaptchaCode, result);
        Assert.Contains("password=***", result);
        Assert.Contains("code=***", result);
    }

    [Fact]
    public void 非法JSON仍按正则掩码()
    {
        var body = $"{{\"password\":\"{Password}\",\"code\":\"{CaptchaCode}\"";

        var result = AuditLogSanitizer.SanitizeBody(body, 2048);

        Assert.DoesNotContain(Password, result);
        Assert.DoesNotContain(CaptchaCode, result);
    }

    [Fact]
    public void 查询串中的令牌与验证码被掩码()
    {
        var query = $"?userId=1&accessToken={AccessToken}&code={CaptchaCode}";

        var result = AuditLogSanitizer.SanitizeText(query, 1024);

        Assert.DoesNotContain(AccessToken, result);
        Assert.DoesNotContain(CaptchaCode, result);
        Assert.Contains("accessToken=***", result);
        Assert.Contains("code=***", result);
        Assert.Contains("userId=1", result);
    }

    [Fact]
    public void 异常消息中的密码被掩码()
    {
        var message = $"登录失败：password={Password}";

        var result = AuditLogSanitizer.SanitizeText(message, 1024);

        Assert.DoesNotContain(Password, result);
        Assert.Contains("password=***", result);
    }

    [Fact]
    public void 超长内容按上限截断并带标记()
    {
        var value = new string('a', 5000);

        var result = AuditLogSanitizer.SanitizeBody(value, 2048);

        Assert.True(result.Length <= 2048);
        Assert.EndsWith("...(truncated)", result);
    }

    [Fact]
    public void 截断上限小于标记长度时仍不超过上限()
    {
        var result = AuditLogSanitizer.Truncate(new string('a', 100), 3);

        Assert.Equal(3, result.Length);
    }
}
