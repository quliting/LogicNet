using Bing;
using LogicNet.Application.Hub;
using LogicNet.Application.UserInfo.DTO;
using LogicNet.Core;
using LogicNet.Core.Entity;
using LogicNet.Core.Security;
using Masuit.Tools;
using Masuit.Tools.Models;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace LogicNet.Application.UserInfo.Services;

/// <summary>
///     用户的逻辑此实例尽量包含所有的CRUD功能。以及Mapper、验证功能。
/// </summary>
public class UserInfoService(
    ISqlSugarClient db,
    Repository<Core.Entity.UserInfo> userRepository,
    ICaptcha captcha) : LogicBase
{
    private readonly ISqlSugarClient db = db;
    private readonly IHubClients<UserHub> _hubClients;

    [AllowAnonymous]
    public async Task<string> LoginAsync(LoginInputDto inputDto)
    {
        var returnTips = "用户名密码错误";
        if (!captcha.Validate(inputDto.Key, inputDto.Code, true)) throw Oops.Bah("验证码错误或已失效");

        Core.Entity.UserInfo userInfo = null;
        switch (inputDto.LoginType)
        {
            case LogicConst.LoginType.手机号码登录:
                if (inputDto.Mobile.IsNullOrEmpty() || !inputDto.Mobile.MatchPhoneNumber() ||
                    inputDto.PassWord.IsNullOrEmpty()) throw Oops.Bah(returnTips);

                userInfo = await db.Queryable<Core.Entity.UserInfo>().Where(item => item.Mobile == inputDto.Mobile)
                    .FirstAsync();
                break;
            case LogicConst.LoginType.用户名密码登录:
                if (inputDto.UserName.IsNullOrEmpty() || inputDto.PassWord.IsNullOrEmpty()) throw Oops.Bah(returnTips);

                userInfo = await db.Queryable<Core.Entity.UserInfo>().Where(item => item.UserName == inputDto.UserName)
                    .FirstAsync();
                break;
        }

        // 加盐哈希校验密码（兼容历史无盐 MD5，校验通过后透明升级为加盐哈希）
        if (userInfo is null || !PasswordHasher.Verify(inputDto.PassWord, userInfo.Password)) throw Oops.Bah(returnTips);
        if (PasswordHasher.NeedsUpgrade(userInfo.Password))
            await db.Updateable<Core.Entity.UserInfo>()
                .SetColumns(item => item.Password == PasswordHasher.Hash(inputDto.PassWord))
                .Where(item => item.Id == userInfo.Id)
                .ExecuteCommandAsync();

        // 查询用户角色写入令牌声明，供 [Authorize(Roles = ...)] 做权限控制
        var roleNames = await db.Queryable<UserRole, Role>((ur, r) => ur.RoleId == r.Id)
            .Where((ur, r) => ur.UserId == userInfo.Id)
            .Select((ur, r) => r.Name)
            .ToListAsync();

        var payload = new Dictionary<string, object>
        {
            { JwtClaimNames.UserId, userInfo.Id },
            { JwtClaimNames.Account, userInfo.UserName }
        };
        if (roleNames.Count > 0) payload.Add(ClaimTypes.Role, roleNames);

        var accessToken = JWTEncryption.Encrypt(payload);

        // 颁发刷新令牌（有效期 7 天），通过 x-access-token 响应头下发，保持登录返回体契约不变
        if (App.HttpContext != null)
            App.HttpContext.Response.Headers["x-access-token"] =
                JWTEncryption.GenerateRefreshToken(accessToken, 60 * 24 * 7);

        // _hubClients..LoginAsync(inputDto);
        return accessToken;
    }

    /// <summary>
    ///     刷新令牌：用过期的访问令牌 + 刷新令牌换取新访问令牌，刷新令牌同步轮换（经 x-access-token 响应头下发）
    /// </summary>
    [AllowAnonymous]
    public Task<string> RefreshAsync(RefreshInputDto inputDto)
    {
        if (TokenRevocationStore.IsRevoked(inputDto.RefreshToken)) throw Oops.Bah("刷新令牌已失效，请重新登录");

        // Exchange 内部校验：访问令牌必须已过期、刷新令牌签名与有效期合法、两者配对、且未重放
        var newAccessToken = JWTEncryption.Exchange(inputDto.Token, inputDto.RefreshToken);
        if (newAccessToken.IsNullOrEmpty()) throw Oops.Bah("刷新令牌无效或已过期，请重新登录");

        // 吊销旧刷新令牌并轮换签发新刷新令牌
        var refreshJwt = JWTEncryption.ReadJwtToken(inputDto.RefreshToken);
        TokenRevocationStore.Revoke(inputDto.RefreshToken, refreshJwt?.ValidTo ?? DateTime.UtcNow.AddDays(7));
        if (App.HttpContext != null)
            App.HttpContext.Response.Headers["x-access-token"] =
                JWTEncryption.GenerateRefreshToken(newAccessToken, 60 * 24 * 7);

        return Task.FromResult(newAccessToken);
    }

    /// <summary>
    ///     登出：吊销当前访问令牌（以及请求头 x-access-token 随附的刷新令牌），吊销后令牌到期前调用接口将返回 401
    /// </summary>
    public Task<bool> LogoutAsync()
    {
        if (App.HttpContext is not DefaultHttpContext httpContext) return Task.FromResult(false);

        var accessToken = JWTEncryption.GetJwtBearerToken(httpContext);
        if (!accessToken.IsNullOrEmpty())
        {
            var jwt = JWTEncryption.ReadJwtToken(accessToken);
            TokenRevocationStore.Revoke(accessToken, jwt?.ValidTo ?? DateTime.UtcNow.AddMinutes(99));
        }

        var refreshToken = httpContext.Request.Headers["x-access-token"].ToString();
        if (!refreshToken.IsNullOrEmpty())
        {
            var refreshJwt = JWTEncryption.ReadJwtToken(refreshToken);
            TokenRevocationStore.Revoke(refreshToken, refreshJwt?.ValidTo ?? DateTime.UtcNow.AddDays(7));
        }

        return Task.FromResult(true);
    }


    [AllowAnonymous]
    public async Task<bool> RegisterAsync(AddUserInfoInputDto inputDto)
    {
        if (!inputDto.Mobile.MatchPhoneNumber()) throw Oops.Bah("手机号格式错误");
        if (inputDto.UserName.IsNullOrEmpty()) throw Oops.Bah("用户名不能为空");

        CheckUserName(inputDto.UserName);
        CheckPassWord(inputDto.PassWord);
        var exist = await db.Queryable<Core.Entity.UserInfo>().Where(x => x.Mobile == inputDto.Mobile).AnyAsync();
        if (exist) throw Oops.Bah("手机号已存在");

        exist = await db.Queryable<Core.Entity.UserInfo>().Where(x => x.UserName == inputDto.UserName).AnyAsync();
        if (exist) throw Oops.Bah("用户名已存在");

        var userInfo = inputDto.Adapt<Core.Entity.UserInfo>();
        try
        {
            await db.Insertable(userInfo).ExecuteReturnSnowflakeIdAsync();
        }
        catch (Exception ex) when (IsUniqueKeyConflict(ex))
        {
            // 唯一索引兜底并发注册竞态（UX_UserInfo_UserName / UX_UserInfo_Mobile）
            throw Oops.Bah("用户名或手机号已存在");
        }

        return true;
    }

    // 不再匿名开放：用户列表属于敏感数据，全局授权要求登录后方可访问
    public async Task<PagedList<GetUserListOutputDto>> GetUserListAsync(GetUserListInputDto inputDto)
    {
        var query = db.Queryable<Core.Entity.UserInfo>()
            .WhereIF(inputDto.Mobile.NotNullOrEmpty(), x => x.Mobile == inputDto.Mobile)
            .WhereIF(inputDto.Name.NotNullOrEmpty(), x => x.Name.Contains(inputDto.Name)).OrderByDescending(item=>item.CreateTime);

        var result = await query.Select<GetUserListOutputDto>()
            .PagedList(inputDto.PageIndex, inputDto.PageSize);
        // 手机号脱敏后返回，避免明文批量外泄
        foreach (var item in result.Data) item.Mobile = MaskMobile(item.Mobile);
        return result;
    }

    private void CheckUserName(string UserName)
    {
        var returnTips = "用户名应以字母开头,8-15位";
        if (UserName.Length < 8 || UserName.Length > 15) throw Oops.Bah(returnTips);

        if (!UserName[0].IsLetter()) throw Oops.Bah(returnTips);
    }

    private static void CheckPassWord(string passWord)
    {
        if (passWord.IsNullOrEmpty() || passWord.Length < 8 ||
            !passWord.Any(char.IsLetter) || !passWord.Any(char.IsDigit))
            throw Oops.Bah("密码至少 8 位，且需同时包含字母和数字");
    }

    private static bool IsUniqueKeyConflict(Exception ex)
    {
        // MySQL 唯一索引冲突错误码 1062（兼容 SqlSugar 包装一层的情况）
        if (ex is MySqlConnector.MySqlException { Number: 1062 }) return true;
        return ex.InnerException is MySqlConnector.MySqlException { Number: 1062 };
    }

    private static string MaskMobile(string mobile)
    {
        // 手机号脱敏：11 位显示前 3 位与后 4 位，中间以 **** 代替；其他格式原样返回
        if (mobile.IsNullOrEmpty() || mobile.Length != 11) return mobile;
        return string.Concat(mobile.AsSpan(0, 3), "****", mobile.AsSpan(7));
    }
}
    
    
    
    
    
    
    
    
    
    
