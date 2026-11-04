using LogicNet.Core.Entity;
using LogicNet.Web.Core;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     数据库初始化：CodeFirst 建表（含唯一索引）+ 角色种子幂等（P1-3 行为回归）
/// </summary>
public class DatabaseInitializerTests
{
    [Fact]
    public void Initialize创建四表与索引()
    {
        using var db = TestDb.Create();

        DatabaseInitializer.Initialize(db);

        Assert.True(db.DbMaintenance.IsAnyTable(nameof(UserInfo), false));
        Assert.True(db.DbMaintenance.IsAnyTable(nameof(Role), false));
        Assert.True(db.DbMaintenance.IsAnyTable(nameof(UserRole), false));
        Assert.True(db.DbMaintenance.IsAnyTable(nameof(Menu), false));

        var indexes = db.Ado.GetDataTable(
                "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='UserInfo'")
            .Rows.Cast<System.Data.DataRow>().Select(r => r["name"]?.ToString()).ToList();
        Assert.Contains("UX_UserInfo_UserName", indexes);
        Assert.Contains("UX_UserInfo_Mobile", indexes);

        var menuIndexes = db.Ado.GetDataTable(
                "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='Menu'")
            .Rows.Cast<System.Data.DataRow>().Select(r => r["name"]?.ToString()).ToList();
        Assert.Contains("IX_Menu_ParentId", menuIndexes);
        Assert.Contains("IX_Menu_Sort", menuIndexes);
        Assert.Contains("UX_Menu_Parent_Path", menuIndexes);
    }

    [Fact]
    public void Initialize写入最小菜单种子且幂等()
    {
        using var db = TestDb.Create();

        DatabaseInitializer.Initialize(db);
        DatabaseInitializer.Initialize(db);

        var menus = db.Queryable<Menu>().OrderBy(x => x.Sort).ToList();
        Assert.Equal(2, menus.Count);
        var system = menus.Single(x => x.Path == "/system");
        var menuManagement = menus.Single(x => x.Path == "/system/menu");
        Assert.Equal(system.Id, menuManagement.ParentId);
        Assert.Equal(MenuType.Directory, system.Type);
        Assert.Equal(MenuType.Menu, menuManagement.Type);
    }

    [Fact]
    public void 已软删除的种子菜单不会被重启复活()
    {
        using var db = TestDb.Create();
        DatabaseInitializer.Initialize(db);

        var system = db.Queryable<Menu>().Single(x => x.Path == "/system");
        db.Updateable<Menu>().SetColumns(x => x.IsDelete == true).Where(x => x.ParentId == system.Id).ExecuteCommand();
        db.Updateable<Menu>().SetColumns(x => x.IsDelete == true).Where(x => x.Id == system.Id).ExecuteCommand();

        DatabaseInitializer.Initialize(db);

        // 软删除行仍在（幂等不重复插入），但不会再生成新的启用菜单
        Assert.Equal(2, db.Queryable<Menu>().ClearFilter().Count());
        Assert.Empty(db.Queryable<Menu>().ToList());
    }

    [Fact]
    public void Initialize重复执行角色种子幂等()
    {
        using var db = TestDb.Create();

        DatabaseInitializer.Initialize(db);
        DatabaseInitializer.Initialize(db);

        var roles = db.Queryable<Role>().Select(r => r.Name).ToList();
        Assert.Equal(2, roles.Count);
        Assert.Contains("Admin", roles);
        Assert.Contains("User", roles);
    }
}
