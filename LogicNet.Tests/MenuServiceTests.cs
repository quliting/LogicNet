using LogicNet.Application.Menu.DTO;
using LogicNet.Application.Menu.Services;
using LogicNet.Core.Entity;
using Microsoft.AspNetCore.Http;
using SqlSugar;
using Xunit;
using MenuEntity = LogicNet.Core.Entity.Menu;

namespace LogicNet.Tests;

/// <summary>
///     菜单 CRUD / 树构建 / 动态加载：层级排序、防环、路径唯一、软删除与按钮剔除
/// </summary>
public class MenuServiceTests
{
    private static MenuService CreateService(SqlSugarScope db)
    {
        db.CodeFirst.InitTables(typeof(MenuEntity));
        return new MenuService(db, new HttpContextAccessor());
    }

    private static MenuEntity SeedMenu(SqlSugarScope db, long id, long parentId, string name, string path,
        int sort, MenuType type = MenuType.Menu, bool isEnabled = true, bool isVisible = true, bool isDelete = false)
    {
        var menu = new MenuEntity
        {
            Id = id,
            ParentId = parentId,
            Name = name,
            Path = path,
            Sort = sort,
            Type = type,
            IsEnabled = isEnabled,
            IsVisible = isVisible,
            IsDelete = isDelete
        };
        db.Insertable(menu).ExecuteCommand();
        return menu;
    }

    private static AddMenuInputDto Input(long parentId, string name, string path, int sort = 0,
        MenuType type = MenuType.Menu, bool isEnabled = true, bool isVisible = true)
    {
        return new AddMenuInputDto
        {
            ParentId = parentId,
            Name = name,
            Path = path,
            Sort = sort,
            Type = type,
            IsEnabled = isEnabled,
            IsVisible = isVisible
        };
    }

    private static UpdateMenuInputDto UpdateInput(MenuEntity menu, long parentId, string path)
    {
        return new UpdateMenuInputDto
        {
            Id = menu.Id,
            ParentId = parentId,
            Name = menu.Name,
            Path = path,
            Sort = menu.Sort,
            Type = menu.Type
        };
    }

    [Fact]
    public async Task 管理树按Sort升序且children嵌套()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "目录A", "/a", 2, MenuType.Directory);
        SeedMenu(db, 2, 0, "目录B", "/b", 1, MenuType.Directory);
        SeedMenu(db, 3, 1, "A子2", "/a/2", 2);
        SeedMenu(db, 4, 1, "A子1", "/a/1", 1);

        var tree = await service.GetMenuTreeAsync();

        Assert.Equal(new[] { "目录B", "目录A" }, tree.Select(x => x.Name).ToArray());
        var a = tree.Single(x => x.Id == 1);
        Assert.Equal(new[] { "A子1", "A子2" }, a.Children.Select(x => x.Name).ToArray());
        Assert.Empty(tree.Single(x => x.Id == 2).Children);
    }

    [Fact]
    public async Task 上级不能指向自身或自己的后代()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "根", "/root", 0);
        SeedMenu(db, 2, 1, "子", "/root/child", 0);
        SeedMenu(db, 3, 2, "孙", "/root/child/grand", 0);

        var self = UpdateInput(SeedMenu(db, 4, 0, "自环", "/self", 0), 4, "/self");
        await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateMenuAsync(self));

        var toChild = UpdateInput(db.Queryable<MenuEntity>().InSingle(1), 2, "/root");
        await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateMenuAsync(toChild));

        var toGrandChild = UpdateInput(db.Queryable<MenuEntity>().InSingle(1), 3, "/root");
        await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateMenuAsync(toGrandChild));
    }

    [Fact]
    public async Task 新增时上级不存在被拒绝()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);

        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateMenuAsync(Input(999, "孤儿", "/orphan")));
    }

    [Fact]
    public async Task 同一父级下路径唯一_不同父级可重复()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "目录1", "/one", 0, MenuType.Directory);
        SeedMenu(db, 2, 0, "目录2", "/two", 0, MenuType.Directory);

        await service.CreateMenuAsync(Input(1, "菜单A", "/one/menu"));
        await service.CreateMenuAsync(Input(2, "菜单B", "/two/menu"));

        // 同一父级重复路径：服务层拒绝
        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateMenuAsync(Input(1, "菜单C", "/one/menu")));
    }

    [Fact]
    public async Task 存在子节点时删除被拒绝()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "根", "/root", 0, MenuType.Directory);
        SeedMenu(db, 2, 1, "子", "/root/child", 0);

        await Assert.ThrowsAnyAsync<Exception>(() => service.DeleteMenuAsync(1));
        Assert.True(await service.DeleteMenuAsync(2));
        // 子节点软删除后，父级可删除
        Assert.True(await service.DeleteMenuAsync(1));
    }

    [Fact]
    public async Task 软删除后不出现在管理树与详情()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "根", "/root", 0, MenuType.Directory);
        SeedMenu(db, 2, 1, "已删子", "/root/deleted", 0, isDelete: true);
        SeedMenu(db, 3, 1, "正常子", "/root/normal", 1);

        var tree = await service.GetMenuTreeAsync();
        var root = Assert.Single(tree);
        Assert.Equal(new[] { 3L }, root.Children.Select(x => x.Id).ToArray());

        await Assert.ThrowsAnyAsync<Exception>(() => service.GetMenuDetailAsync(2));
    }

    [Fact]
    public async Task GetMyMenus仅返回已启用可见且非按钮()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "系统", "/system", 0, MenuType.Directory);
        SeedMenu(db, 2, 1, "可见菜单", "/system/menu", 1);
        SeedMenu(db, 3, 1, "禁用菜单", "/system/disabled", 2, isEnabled: false);
        SeedMenu(db, 4, 1, "隐藏菜单", "/system/invisible", 3, isVisible: false);
        SeedMenu(db, 5, 1, "按钮", "/system/btn", 4, MenuType.Button, isEnabled: true, isVisible: true);
        SeedMenu(db, 6, 0, "禁用目录", "/disabled-root", 5, MenuType.Directory, isEnabled: false);
        SeedMenu(db, 7, 6, "禁用目录的子级", "/disabled-root/child", 0);

        var tree = await service.GetMyMenusAsync();

        var root = Assert.Single(tree);
        Assert.Equal(1, root.Id);
        Assert.Equal(new[] { 2L }, root.Children.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task 列表支持名称模糊与类型过滤且分页钳制()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedMenu(db, 1, 0, "系统管理", "/system", 0, MenuType.Directory);
        SeedMenu(db, 2, 0, "用户管理", "/user", 0);
        SeedMenu(db, 3, 0, "角色管理", "/role", 0);

        var byName = await service.GetMenuListAsync(new GetMenuListInputDto { Name = "管理", PageSize = 100 });
        Assert.Equal(3, byName.Data.Count);

        var clamped = await service.GetMenuListAsync(new GetMenuListInputDto { Name = "管理", PageSize = 0 });
        Assert.Equal(1, clamped.PageSize);
        Assert.Single(clamped.Data);

        var byType = await service.GetMenuListAsync(new GetMenuListInputDto { Type = MenuType.Directory });
        Assert.Single(byType.Data);
        Assert.Equal("系统管理", byType.Data[0].Name);
    }
}
