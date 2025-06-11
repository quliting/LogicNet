using LogicNet.Application.UserInfo.DTO;
using LogicNet.Core;
using LogicNet.Core.Entity;
using Masuit.Tools;

namespace LogicNet.Application.UserInfo.Services;

/// <summary>
///     用户的逻辑此实例尽量包含所有的CRUD功能。以及Mapper、验证功能。
/// </summary>
public class UserInfoService(ISqlSugarClient db, Repository<Core.Entity.UserInfo> userRepository) : LogicBase
{
    private readonly ISqlSugarClient db = db;

    [AllowAnonymous]
    public async Task<string> LoginAsync(LoginInputDto inputDto)
    {
        switch (inputDto.LoginType)
        {
            case LogicConst.LoginType.手机号码登录:
                break;
            case LogicConst.LoginType.用户名密码登录:
                break;
        }

        return string.Empty;
    }

    [AllowAnonymous]
    public async Task<bool> RegisterAsync(AddUserInfoInputDto inputDto)
    {
        if (!inputDto.Mobile.MatchPhoneNumber())
        {
            throw Oops.Bah("手机号格式错误");
        }

        var exist = await db.Queryable<Core.Entity.UserInfo>().Where(x => x.Mobile == inputDto.Mobile).AnyAsync();
        if (exist)
        {
            throw Oops.Bah("手机号已存在");
        }

        exist = await db.Queryable<Core.Entity.UserInfo>().Where(x => x.UserName == inputDto.UserName).AnyAsync();
        if (exist)
        {
            throw Oops.Bah("用户名已存在");
        }

        var userInfo = inputDto.Adapt<Core.Entity.UserInfo>();
        await db.Insertable(userInfo).ExecuteReturnSnowflakeIdAsync();
        return true;
    }

    /// <summary>
    /// 生成表结构
    /// </summary>
    /// <returns></returns>
    [AllowAnonymous]
    public async Task<bool> InitTableAsync()
    {
        var types = App.EffectiveTypes
            .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseEntity).IsAssignableFrom(t)).ToList();
        db.CodeFirst.InitTables(types.ToArray());
        return true;
    }
}