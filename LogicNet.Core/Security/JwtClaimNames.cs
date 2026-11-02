namespace LogicNet.Core.Security;

/// <summary>
///     JWT 声明名统一定义（登录颁发、鉴权校验、审计字段读取均以此处常量为准）
/// </summary>
public static class JwtClaimNames
{
    /// <summary>
    ///     用户 Id
    /// </summary>
    public const string UserId = "UserId";

    /// <summary>
    ///     登录账号
    /// </summary>
    public const string Account = "Account";
}
