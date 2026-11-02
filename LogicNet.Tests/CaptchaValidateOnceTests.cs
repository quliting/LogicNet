using Lazy.Captcha.Core;
using Lazy.Captcha.Core.RateLimit;
using LogicNet.Application.Captcha.DTO;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     验证码独立校验一次性销毁（P1-7）：Validate 必须以 removeIfSuccess=true 调用，与登录链路一致
/// </summary>
public class CaptchaValidateOnceTests
{
    private sealed class FakeCaptcha : ICaptcha
    {
        public bool LastRemoveIfSuccess;
        public bool LastRemoveIfFail;

        public CaptchaData Generate(string captchaId, int? expirySeconds = null) =>
            throw new NotImplementedException();

        public bool Validate(string captchaId, string code, bool removeIfSuccess = true, bool removeIfFail = true)
        {
            LastRemoveIfSuccess = removeIfSuccess;
            LastRemoveIfFail = removeIfFail;
            return true;
        }

        public CaptchaData GenerateWithRateLimit(string captchaId, string rateLimitKey, int? expirySeconds = null) =>
            throw new NotImplementedException();

        public RateLimitResult CheckRateLimit(string rateLimitKey) => throw new NotImplementedException();
    }

    [Fact]
    public void 独立校验成功后销毁验证码()
    {
        var fake = new FakeCaptcha();
        var service = new LogicNet.Application.Captcha.Service.CaptchaService(fake);

        var result = service.ValidateAsync(new ValidateInputDto { Key = "k", Code = "1234" });

        Assert.True(result);
        Assert.True(fake.LastRemoveIfSuccess); // 成功即销毁，防重放
        Assert.True(fake.LastRemoveIfFail); // 失败也销毁（默认值），与登录链路一致
    }
}
