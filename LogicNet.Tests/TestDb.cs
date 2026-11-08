using System.Security.Claims;
using System.Threading;
using LogicNet.Core.Entity;
using LogicNet.Core.Security;
using Microsoft.AspNetCore.Http;
using SqlSugar;

namespace LogicNet.Tests;

/// <summary>
///     测试用内存数据库（SqlSugar + Sqlite :memory:），全局过滤器配置与 Startup 保持一致。
///     内存库在连接关闭后数据即消失，故 IsAutoCloseConnection=false 保持单连接。
/// </summary>
public static class TestDb
{
    private static long _id;

    static TestDb()
    {
        // 雪花 Id 用单调递增计数器代替，避免 YitIdHelper 依赖宿主环境
        StaticConfig.CustomSnowFlakeFunc = () => Interlocked.Increment(ref _id);
    }

    public static SqlSugarScope Create()
    {
        return new SqlSugarScope(new ConnectionConfig
        {
            DbType = DbType.Sqlite,
            ConnectionString = "Data Source=:memory:",
            IsAutoCloseConnection = false
        }, db =>
        {
            // 与 Startup 一致：软删除全局过滤
            db.QueryFilter.AddTableFilter<IDeleteFilter>(x => !x.IsDelete);
        });
    }

    /// <summary>
    ///     建表并写入一个用户（默认正常；isDelete=true 时为软删除用户）
    /// </summary>
    public static UserInfo SeedUser(SqlSugarScope db, long id, string userName, bool isDelete = false)
    {
        db.CodeFirst.InitTables(typeof(UserInfo));
        var user = new UserInfo
        {
            Id = id,
            UserName = userName,
            Name = userName,
            Mobile = $"138{id % 100000000:D8}",
            Password = "x",
            IsDelete = isDelete
        };
        db.Insertable(user).ExecuteCommand();
        return user;
    }

    /// <summary>
    ///     建角色/用户角色/角色菜单表并写入角色
    /// </summary>
    public static Role SeedRole(SqlSugarScope db, long id, string name)
    {
        db.CodeFirst.InitTables(typeof(Role), typeof(UserRole), typeof(RoleMenu));
        var role = new Role { Id = id, Name = name };
        db.Insertable(role).ExecuteCommand();
        return role;
    }

    /// <summary>
    ///     绑定用户与角色
    /// </summary>
    public static void SeedUserRole(SqlSugarScope db, long userId, long roleId)
    {
        db.CodeFirst.InitTables(typeof(UserRole));
        db.Insertable(new UserRole { UserId = userId, RoleId = roleId }).ExecuteCommand();
    }

    /// <summary>
    ///     绑定角色与菜单（isDelete=true 时写入软删除关联，用于唯一索引与恢复场景）
    /// </summary>
    public static void SeedRoleMenu(SqlSugarScope db, long roleId, long menuId, bool isDelete = false)
    {
        db.CodeFirst.InitTables(typeof(RoleMenu));
        db.Insertable(new RoleMenu { RoleId = roleId, MenuId = menuId, IsDelete = isDelete })
            .ExecuteReturnSnowflakeId();
    }

    /// <summary>
    ///     构造带登录用户声明的 HttpContextAccessor（角色由数据库 UserRole 决定）
    /// </summary>
    public static IHttpContextAccessor CreateAccessor(long userId)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(JwtClaimNames.UserId, userId.ToString()) }, "TestAuth"));
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }
}
