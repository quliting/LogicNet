namespace LogicNet.Application.UserInfo.DTO;

public class RefreshInputDto
{
    /// <summary>
    ///     已过期的访问令牌
    /// </summary>
    [Required(ErrorMessage = "访问令牌不能为空")]
    public string Token { get; set; }

    /// <summary>
    ///     刷新令牌
    /// </summary>
    [Required(ErrorMessage = "刷新令牌不能为空")]
    public string RefreshToken { get; set; }
}
