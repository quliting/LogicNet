using System;
using Microsoft.AspNetCore.Http;

namespace LogicNet.Web.Core;

/// <summary>
///     匿名/高风险接口限流规则（按客户端 IP 固定窗口）。仅匹配指定路径段，其余请求不限流。
///     路径段与 DynamicApiControllerSettings（KeepName=true、LowercaseRoute=false）生成的路由保持一致。
/// </summary>
public static class HighRiskEndpointRateLimit
{
    /// <summary>
    ///     规则：路径段（不区分大小写的前缀匹配）+ 窗口内允许次数 + 窗口时长
    /// </summary>
    public static readonly (string PathSegment, int PermitLimit, TimeSpan Window)[] Rules =
    {
        // 验证码生成（每次生成都会写盘，最敏感）
        ("/Api/Captcha/GetCaptcha", 10, TimeSpan.FromMinutes(1)),
        // 验证码独立校验
        ("/Api/Captcha/Validate", 20, TimeSpan.FromMinutes(1)),
        // 登录（防爆破）
        ("/Api/UserInfo/Login", 10, TimeSpan.FromMinutes(1)),
        // 注册（防批量注册）
        ("/Api/UserInfo/Register", 5, TimeSpan.FromMinutes(1)),
        // 刷新令牌
        ("/Api/UserInfo/Refresh", 20, TimeSpan.FromMinutes(1))
    };

    /// <summary>
    ///     命中规则时返回规则索引，未命中返回 -1
    /// </summary>
    public static int Match(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value)) return -1;
        for (var i = 0; i < Rules.Length; i++)
            if (value.StartsWith(Rules[i].PathSegment, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }
}
