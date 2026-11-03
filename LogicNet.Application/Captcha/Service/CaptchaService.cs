using LogicNet.Application.Captcha.DTO;

namespace LogicNet.Application.Captcha.Service;

[AllowAnonymous]
public class CaptchaService(ICaptcha captcha) : LogicBase
{
    // 验证码图片写盘数量上限：防止突发流量在 DeleteDirJob 清理窗口内写爆磁盘
    private const int MaxCaptchaFileCount = 500;

    public async Task<GetCaptchaOutputDto> GetCaptchaAsync()
    {
        // 发布/容器环境可能没有 wwwroot 目录（WebRootPath 为 null），兜底到内容根下并确保 Temp 存在，
        // 否则 Path.Combine 抛空路径异常，验证码接口直接 500。
        var webRoot = App.WebHostEnvironment.WebRootPath;
        if (string.IsNullOrEmpty(webRoot)) webRoot = Path.Combine(App.WebHostEnvironment.ContentRootPath, "wwwroot");
        var tempDir = Path.Combine(webRoot, "Temp");
        Directory.CreateDirectory(tempDir);
        if (Directory.GetFiles(tempDir).Length >= MaxCaptchaFileCount)
            throw Oops.Bah("验证码请求过于频繁，请稍后重试");

        var fileName = YitIdHelper.NextId() + ".png";
        var key = YitIdHelper.NextId().ToString();
        var info = captcha.Generate(key);
        await info.Bytes.CopyToSaveAsync(Path.Combine(tempDir, fileName));
        return new GetCaptchaOutputDto
        {
            // 返回根相对路径：避免 AppSettings:Host 在不同部署环境（容器端口/域名）下拼出不可访问的绝对地址
            Url = $"/Temp/{fileName}",
            Key = key
        };
    }

    /// <summary>
    ///     独立校验验证码：成功后立即销毁（一次性），与登录链路 captcha.Validate(key, code, true) 行为一致，
    ///     防止同一验证码被重复用于批量请求
    /// </summary>
    public bool ValidateAsync(ValidateInputDto inputDto)
    {
        return captcha.Validate(inputDto.Key, inputDto.Code, true);
    }
}