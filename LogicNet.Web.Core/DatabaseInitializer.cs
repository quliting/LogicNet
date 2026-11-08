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
        db.CodeFirst.InitTables(typeof(UserInfo), typeof(Role), typeof(UserRole), typeof(Menu),
            typeof(RoleMenu), typeof(AuditLog), typeof(AuditSqlLog));

        // 幂等角色种子：仅插入库中尚不存在的角色，可重复执行
        var existingNames = db.Queryable<Role>().Select(x => x.Name).ToList();
        var newRoles = DefaultRoles.Except(existingNames)
            .Select(name => new Role { Name = name })
            .ToList();
        if (newRoles.Count > 0)
            db.Insertable(newRoles).ExecuteReturnSnowflakeIdList();

        SeedMenus(db);
        SeedAdminRoleMenus(db);
    }

    /// <summary>
    ///     Admin 角色关联全部存量菜单：数据层与代码层兜底保持一致。
    ///     仅补插缺失关联（含软删除行的判断），不复活管理员主动取消的绑定；
    ///     后续新增菜单由 MenuService.ApplyRoleFilter 的 Admin 兜底保证可见。
    /// </summary>
    private static void SeedAdminRoleMenus(ISqlSugarClient db)
    {
        var admin = db.Queryable<Role>().Where(x => x.Name == "Admin").First();
        if (admin is null) return;

        var menuIds = db.Queryable<Menu>().Select(x => x.Id).ToList();
        if (menuIds.Count == 0) return;

        // ClearFilter 让软删除关联参与判断，避免唯一索引冲突与误复活
        var linkedIds = db.Queryable<RoleMenu>().ClearFilter()
            .Where(x => x.RoleId == admin.Id)
            .Select(x => x.MenuId)
            .ToList();

        var missing = menuIds.Except(linkedIds)
            .Select(menuId => new RoleMenu { RoleId = admin.Id, MenuId = menuId })
            .ToList();
        // BaseEntity.Id 非自增，必须走雪花 Id 插入（与角色/菜单种子一致）
        if (missing.Count > 0)
            db.Insertable(missing).ExecuteReturnSnowflakeIdList();
    }

    /// <summary>
    ///     最小菜单种子：系统管理（目录）→ 菜单管理（菜单），可重复执行。
    ///     已软删除的种子菜单不会被重新插入，避免重启覆盖管理员删除意图。
    /// </summary>
    private static void SeedMenus(ISqlSugarClient db)
    {
        // ClearFilter 让软删除行参与存在性判断（默认全局过滤器会把它们隐藏）
        var system = db.Queryable<Menu>().ClearFilter()
            .Where(x => x.ParentId == 0 && x.Path == "/system")
            .First();
        if (system is { IsDelete: true }) return;
        if (system is null)
        {
            system = new Menu
            {
                ParentId = 0,
                Name = "系统管理",
                Path = "/system",
                Icon = "setting",
                Sort = 1,
                Type = MenuType.Directory,
                IsEnabled = true,
                IsVisible = true
            };
            system.Id = db.Insertable(system).ExecuteReturnSnowflakeId();
        }

        var menuManagement = db.Queryable<Menu>().ClearFilter()
            .Where(x => x.ParentId == system.Id && x.Path == "/system/menu")
            .First();
        if (menuManagement is null)
            db.Insertable(new Menu
            {
                ParentId = system.Id,
                Name = "菜单管理",
                Path = "/system/menu",
                Component = "system/menu/index",
                Icon = "menu",
                Sort = 1,
                Type = MenuType.Menu,
                PermissionCode = "system:menu:list",
                IsEnabled = true,
                IsVisible = true
            }).ExecuteReturnSnowflakeId();
    }
}
