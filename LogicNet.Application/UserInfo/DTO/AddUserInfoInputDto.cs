namespace LogicNet.Application.UserInfo.DTO;

public class AddUserInfoInputDto
{
    [Required(ErrorMessage = "手机号不能为空",  AllowEmptyStrings = false)]
    public string Mobile { get; set; }
    public string Name { get; set; } 
    
    public string UserName { get; set; }
    
    [Required(ErrorMessage = "密码不能为空")]
    public string PassWord { get; set; }
}