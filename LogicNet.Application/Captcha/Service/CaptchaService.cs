using LogicNet.Application.Captcha.DTO;

namespace LogicNet.Application.Captcha.Service;

[AllowAnonymous]
public class CaptchaService(ICaptcha captcha) : LogicBase
{
    public async Task<GetCaptchaOutputDto> GetCaptchaAsync()
    {
        var fileName = YitIdHelper.NextId() + ".png";
        var key = YitIdHelper.NextId().ToString();
        var info = captcha.Generate(key);
        await info.Bytes.CopyToSaveAsync(Path.Combine(App.WebHostEnvironment.WebRootPath, "Temp",
            fileName));
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