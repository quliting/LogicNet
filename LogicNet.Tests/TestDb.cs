using System.Threading;
using LogicNet.Core.Entity;
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
}
