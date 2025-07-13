using Bing;
using Furion.DataEncryption.Extensions;
using LogicNet.Application.UserInfo.DTO;
using LogicNet.Core;
using LogicNet.Core.Entity;
using Masuit.Tools;
using Masuit.Tools.Models;

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
        var returnTips = "用户名密码错误";
        Core.Entity.UserInfo userInfo = null;
        switch (inputDto.LoginType)
        {
            case LogicConst.LoginType.手机号码登录:
                if (inputDto.Mobile.IsNullOrEmpty() || !inputDto.Mobile.MatchPhoneNumber() ||
                    inputDto.PassWord.IsNullOrEmpty()) throw Oops.Bah(returnTips);

                userInfo = await db.Queryable<Core.Entity.UserInfo>().Where(item => item.Mobile == inputDto.Mobile)
                    .FirstAsync();
                if (userInfo is null) throw Oops.Bah(returnTips);
                break;
            case LogicConst.LoginType.用户名密码登录:
                if (inputDto.UserName.IsNullOrEmpty() || inputDto.PassWord.IsNullOrEmpty()) throw Oops.Bah(returnTips);

                userInfo = await db.Queryable<Core.Entity.UserInfo>().Where(item =>
                        item.UserName == inputDto.UserName &&
                        item.Password == inputDto.PassWord.ToMD5Encrypt(false, false))
                    .FirstAsync();

                if (userInfo is null) throw Oops.Bah(returnTips);

                break;
        }

        var accessToken = JWTEncryption.Encrypt(new Dictionary<string, object>
        {
            { "UserId", userInfo.Id },
            { "Account", userInfo.UserName }
        });
        return accessToken;
    }


    [AllowAnonymous]
    public async Task<bool> RegisterAsync(AddUserInfoInputDto inputDto)
    {
        if (!inputDto.Mobile.MatchPhoneNumber()) throw Oops.Bah("手机号格式错误");
        if (inputDto.UserName.IsNullOrEmpty()) throw Oops.Bah("用户名不能为空");

        CheckUserName(inputDto.UserName);
        var exist = await db.Queryable<Core.Entity.UserInfo>().Where(x => x.Mobile == inputDto.Mobile).AnyAsync();
        if (exist) throw Oops.Bah("手机号已存在");

        exist = await db.Queryable<Core.Entity.UserInfo>().Where(x => x.UserName == inputDto.UserName).AnyAsync();
        if (exist) throw Oops.Bah("用户名已存在");

        var userInfo = inputDto.Adapt<Core.Entity.UserInfo>();
        await db.Insertable(userInfo).ExecuteReturnSnowflakeIdAsync();
        return true;
    }

    [AllowAnonymous]
    public async Task<PagedList<GetUserListOutputDto>> GetUserListAsync(GetUserListInputDto inputDto)
    {
        var query = db.Queryable<Core.Entity.UserInfo>()
            .WhereIF(inputDto.Mobile.NotNullOrEmpty(), x => x.Mobile == inputDto.Mobile)
            .WhereIF(inputDto.Name.NotNullOrEmpty(), x => x.Name == inputDto.Name);

        var result = await query.Select<GetUserListOutputDto>()
            .PagedList(inputDto.PageIndex, inputDto.PageSize);
        return result;
    }

    private void CheckUserName(string UserName)
    {
        var returnTips = "用户名应以字母开头,8-15位";
        if (UserName.Length < 8 || UserName.Length > 15) throw Oops.Bah(returnTips);

        if (!UserName[0].IsLetter()) throw Oops.Bah(returnTips);
    }

    /// <summary>
    ///     生成表结构
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