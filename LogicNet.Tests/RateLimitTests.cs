using System.Threading.RateLimiting;
using LogicNet.Web.Core;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     高风险接口限流：路径匹配规则 + 固定窗口限流行为（P1-1 行为回归）
/// </summary>
public class RateLimitTests
{
    [Theory]
    [InlineData("/Api/Captcha/GetCaptcha", 0)]
    [InlineData("/Api/Captcha/Validate", 1)]
    [InlineData("/Api/UserInfo/Login", 2)]
    [InlineData("/Api/UserInfo/Register", 3)]
    [InlineData("/Api/UserInfo/Refresh", 4)]
    [InlineData("/api/userinfo/login", 2)] // 不区分大小写
    public void 高风险路径命中对应规则(string path, int expectedRule)
    {
        Assert.Equal(expectedRule, HighRiskEndpointRateLimit.Match(path));
    }

    [Theory]
    [InlineData("/Api/UserInfo/GetUserList")]
    [InlineData("/Api/Captcha")]
    [InlineData("/Api/Other/Anything")]
    public void 普通路径不限流(string path)
    {
        Assert.Equal(-1, HighRiskEndpointRateLimit.Match(path));
    }

    [Fact]
    public void 固定窗口限流器超限拒绝()
    {
        var rule = HighRiskEndpointRateLimit.Rules[2]; // 登录：10 次/分钟
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = rule.PermitLimit,
            Window = rule.Window,
            QueueLimit = 0
        });

        for (var i = 0; i < rule.PermitLimit; i++)
            Assert.True(limiter.AttemptAcquire(1).IsAcquired, $"第 {i + 1} 次应放行");
        Assert.False(limiter.AttemptAcquire(1).IsAcquired);
    }
}
