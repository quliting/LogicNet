using LogicNet.Application.Menu.DTO;
using LogicNet.Application.Menu.Services;
using LogicNet.Core.Entity;
using SqlSugar;
using Xunit;
using MenuEntity = LogicNet.Core.Entity.Menu;

namespace LogicNet.Tests;

/// <summary>
///     角色-菜单绑定：父子联动（勾子补父、取消父级级联取消子级）、清空与软删除恢复
/// </summary>
public class RoleMenuServiceTests
{
    private static RoleMenuService CreateService(SqlSugarScope db)
    {
        db.CodeFirst.InitTables(typeof(MenuEntity), typeof(Role), typeof(RoleMenu));
        return new RoleMenuService(db);
    }

    private static void SeedMenu(SqlSugarScope db, long id, long parentId, string name, bool isDelete = false)
    {
        db.Insertable(new MenuEntity
        {
            Id = id,
            ParentId = parentId,
            Name = name,
            Path = $"/menu/{id}",
            Sort = (int)id,
            Type = parentId == 0 ? MenuType.Directory : MenuType.Menu,
            IsEnabled = true,
            IsVisible = true,
            IsDelete = isDelete
        }).ExecuteCommand();
    }

    private static async Task<List<long>> EffectiveIds(RoleMenuService service, long roleId)
    {
        return (await service.GetRoleMenusAsync(roleId)).OrderBy(x => x).ToList();
    }

    [Fact]
    public async Task 勾选子级自动补齐父级()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "系统");
        SeedMenu(db, 2, 1, "菜单管理");
        SeedMenu(db, 3, 2, "按钮权限");
        TestDb.SeedRole(db, 10, "User");

        await service.SaveRoleMenusAsync(new SaveRoleMenusInputDto { RoleId = 10, MenuIds = new List<long> { 3 } });

        Assert.Equal(new[] { 1L, 2L, 3L }, await EffectiveIds(service, 10));
    }

    [Fact]
    public async Task 取消父级级联取消子级()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "系统");
        SeedMenu(db, 2, 1, "菜单管理");
        SeedMenu(db, 3, 2, "按钮权限");
        TestDb.SeedRole(db, 10, "User");

        // 先绑定整棵子树
        await service.SaveRoleMenusAsync(new SaveRoleMenusInputDto { RoleId = 10, MenuIds = new List<long> { 3 } });
        Assert.Equal(new[] { 1L, 2L, 3L }, await EffectiveIds(service, 10));

        // 只保留父级：2、3 作为被取消父级的子树一并取消
        await service.SaveRoleMenusAsync(new SaveRoleMenusInputDto { RoleId = 10, MenuIds = new List<long> { 1 } });

        Assert.Equal(new[] { 1L }, await EffectiveIds(service, 10));
    }

    [Fact]
    public async Task 保存空集合清空角色授权()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "系统");
        SeedMenu(db, 2, 1, "菜单管理");
        TestDb.SeedRole(db, 10, "User");

        await service.SaveRoleMenusAsync(new SaveRoleMenusInputDto { RoleId = 10, MenuIds = new List<long> { 2 } });
        await service.SaveRoleMenusAsync(new SaveRoleMenusInputDto { RoleId = 10, MenuIds = new List<long>() });

        Assert.Empty(await service.GetRoleMenusAsync(10));
        // 软删除而非物理删除
        Assert.Equal(2, db.Queryable<RoleMenu>().ClearFilter().Count());
    }

    [Fact]
    public async Task 重新勾选时恢复软删除关联而不触发唯一索引冲突()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "系统");
        SeedMenu(db, 2, 1, "菜单管理");
        TestDb.SeedRole(db, 10, "User");

        await service.SaveRoleMenusAsync(new SaveRoleMenusInputDto { RoleId = 10, MenuIds = new List<long> { 2 } });
        await service.SaveRoleMenusAsync(new SaveRoleMenusInputDto { RoleId = 10, MenuIds = new List<long>() });
        await service.SaveRoleMenusAsync(new SaveRoleMenusInputDto { RoleId = 10, MenuIds = new List<long> { 2 } });

        Assert.Equal(new[] { 1L, 2L }, await EffectiveIds(service, 10));
        Assert.Equal(2, db.Queryable<RoleMenu>().ClearFilter().Count());
    }

    [Fact]
    public async Task 角色或菜单不存在时拒绝保存()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "系统");
        TestDb.SeedRole(db, 10, "User");

        await Assert.ThrowsAnyAsync<Exception>(() => service.SaveRoleMenusAsync(
            new SaveRoleMenusInputDto { RoleId = 999, MenuIds = new List<long> { 1 } }));
        await Assert.ThrowsAnyAsync<Exception>(() => service.SaveRoleMenusAsync(
            new SaveRoleMenusInputDto { RoleId = 10, MenuIds = new List<long> { 999 } }));
    }

    [Fact]
    public async Task 查询角色菜单不返回已软删除菜单()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "系统");
        SeedMenu(db, 2, 1, "已删菜单", isDelete: true);
        TestDb.SeedRole(db, 10, "User");
        TestDb.SeedRoleMenu(db, 10, 1);
        TestDb.SeedRoleMenu(db, 10, 2);

        Assert.Equal(new[] { 1L }, await EffectiveIds(service, 10));
    }
}
