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
    public void Initialize创建三表与唯一索引()
    {
        using var db = TestDb.Create();

        DatabaseInitializer.Initialize(db);

        Assert.True(db.DbMaintenance.IsAnyTable(nameof(UserInfo), false));
        Assert.True(db.DbMaintenance.IsAnyTable(nameof(Role), false));
        Assert.True(db.DbMaintenance.IsAnyTable(nameof(UserRole), false));

        var indexes = db.Ado.GetDataTable(
                "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='UserInfo'")
            .Rows.Cast<System.Data.DataRow>().Select(r => r["name"]?.ToString()).ToList();
        Assert.Contains("UX_UserInfo_UserName", indexes);
        Assert.Contains("UX_UserInfo_Mobile", indexes);
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
