using LogicNet.Application.Captcha.DTO;

namespace LogicNet.Application.Captcha.Service;

[AllowAnonymous]
public class CaptchaService(ICaptcha captcha) : LogicBase
{
    // 验证码图片写盘数量上限：防止突发流量在 DeleteDirJob 清理窗口内写爆磁盘
    private const int MaxCaptchaFileCount = 500;

    public async Task<GetCaptchaOutputDto> GetCaptchaAsync()
    {
        var tempDir = Path.Combine(App.WebHostEnvironment.WebRootPath, "Temp");
        if (Directory.Exists(tempDir) && Directory.GetFiles(tempDir).Length >= MaxCaptchaFileCount)
            throw Oops.Bah("验证码请求过于频繁，请稍后重试");

        var fileName = YitIdHelper.NextId() + ".png";
        var key = YitIdHelper.NextId().ToString();
        var info = captcha.Generate(key);
        await info.Bytes.CopyToSaveAsync(Path.Combine(tempDir, fileName));
        return new GetCaptchaOutputDto
        {
            Url = Path.Combine(App.GetConfig<string>("AppSettings:Host"), "Temp", fileName),
            Key = key
        };
    }

    public bool ValidateAsync(ValidateInputDto inputDto)
    {
        return captcha.Validate(inputDto.Key, inputDto.Code, false);
    }
}