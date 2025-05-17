namespace LogicNet.Application.UserInfo.DTO;

public class LoginInputDto
{
    public LogicConst.LoginType LoginType { get; set; }

    public string Mobile { get; set; }

    public string UserName { get; set; }
    public string PassWord { get; set; }

    /// <summary>
    /// 图形验证码
    /// </summary>
    public string Code { get; set; }
}