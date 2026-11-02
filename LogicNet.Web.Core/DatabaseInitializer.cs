using System;
using System.Collections.Generic;
using System.Linq;
using LogicNet.Core.Entity;
using SqlSugar;

namespace LogicNet.Web.Core;

/// <summary>
///     数据库初始化：CodeFirst 建表（含实体声明的索引）+ 幂等种子数据。
///     全新环境一键就绪；已有库仅同步缺失列，不改动既有列/索引（存量库的唯一索引见 Sql/ 幂等脚本）。
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    ///     基础角色种子（配合登录时写入令牌的角色声明与 [Authorize(Roles = ...)]）
    /// </summary>
    public static readonly string[] DefaultRoles = { "Admin", "User" };

    public static void Initialize(ISqlSugarClient db)
    {
        // CodeFirst 建表：新环境自动创建表结构与 SugarIndex 声明的索引
        db.CodeFirst.InitTables(typeof(UserInfo), typeof(Role), typeof(UserRole));

        // 幂等角色种子：仅插入库中尚不存在的角色，可重复执行
        var existingNames = db.Queryable<Role>().Select(x => x.Name).ToList();
        var newRoles = DefaultRoles.Except(existingNames)
            .Select(name => new Role { Name = name })
            .ToList();
        if (newRoles.Count > 0)
            db.Insertable(newRoles).ExecuteReturnSnowflakeIdList();
    }
}
