using LogicNet.Core.Entity;
using SqlSugar;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     软删除全局过滤：与 Startup 相同的 QueryFilter 配置下，已删数据不出现在查询结果（P0-5 行为回归）
/// </summary>
public class SoftDeleteFilterTests
{
    [Fact]
    public void 单表查询自动排除软删除数据()
    {
        using var db = TestDb.Create();
        TestDb.SeedUser(db, 1001, "normal_user");
        TestDb.SeedUser(db, 1002, "deleted_user", isDelete: true);

        var users = db.Queryable<UserInfo>().ToList();

        Assert.Single(users);
        Assert.Equal("normal_user", users[0].UserName);
    }

    [Fact]
    public void 多表联查同样携带软删除过滤()
    {
        using var db = TestDb.Create();
        db.CodeFirst.InitTables(typeof(Role), typeof(UserRole));
        TestDb.SeedUser(db, 1001, "normal_user");
        TestDb.SeedUser(db, 1002, "deleted_user", isDelete: true);
        db.Insertable(new Role { Id = 2001, Name = "Admin" }).ExecuteCommand();
        db.Insertable(new UserRole { Id = 3001, UserId = 1001, RoleId = 2001 }).ExecuteCommand();
        db.Insertable(new UserRole { Id = 3002, UserId = 1002, RoleId = 2001 }).ExecuteCommand();

        // 与登录颁发令牌时相同的角色查询形态
        var roleNames = db.Queryable<UserRole, Role>((ur, r) => ur.RoleId == r.Id)
            .Where((ur, r) => r.Name == "Admin")
            .Select((ur, r) => ur.UserId)
            .ToList();

        // UserRole 自身也是 BaseEntity：软删用户的关联行未删时仍在，但联查 UserInfo 时软删用户必须被过滤
        var visibleUserIds = db.Queryable<UserInfo>()
            .Where(u => roleNames.Contains(u.Id))
            .Select(u => u.Id)
            .ToList();
        Assert.Equal(new long[] { 1001 }, visibleUserIds);
    }

    [Fact]
    public void 按主键查软删除用户返回空_已删用户不能登录的数据基础()
    {
        using var db = TestDb.Create();
        TestDb.SeedUser(db, 1002, "deleted_user", isDelete: true);

        Assert.False(db.Queryable<UserInfo>().Any(u => u.Id == 1002));
    }
}
