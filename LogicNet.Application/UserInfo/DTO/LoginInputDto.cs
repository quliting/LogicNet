namespace LogicNet.Application.UserInfo.DTO;

public class LoginInputDto
{
    [Required(ErrorMessage = "登录类型不能为空"), Range(1, 2, ErrorMessage = "登录类型错误")]
    public LogicConst.LoginType LoginType { get; set; }

    public string Mobile { get; set; }

    public string UserName { get; set; }

    public string PassWord { get; set; }

    /// <summary>
    /// 图形验证码
    /// </summary>
    [Required(ErrorMessage = "验证码不能为空")]
    public string Code { get; set; }
}