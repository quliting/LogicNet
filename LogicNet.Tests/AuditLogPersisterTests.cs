using LogicNet.Core.Entity;
using LogicNet.Web.Core.Audit;
using SqlSugar;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     后台落库：批量写入成功；数据库不可用时返回 false 且不抛出（不影响主请求）
/// </summary>
public class AuditLogPersisterTests
{
    private static AuditLog CreateLog(long id)
    {
        return new AuditLog
        {
            Id = id,
            HttpMethod = "POST",
            Path = "/Api/Menu/CreateMenu",
            StatusCode = 200,
            ResultCode = 200,
            Succeeded = true,
            DurationMs = 12,
            CreateTime = DateTime.Now
        };
    }

    [Fact]
    public async Task 批量写入成功()
    {
        using var db = TestDb.Create();
        db.CodeFirst.InitTables(typeof(AuditLog));

        var result = await AuditLogPersister.PersistAsync(db, new[] { CreateLog(1), CreateLog(2) });

        Assert.True(result);
        Assert.Equal(2, db.Queryable<AuditLog>().Count());
    }

    [Fact]
    public async Task 未指定Id时自动生成唯一雪花Id()
    {
        using var db = TestDb.Create();
        db.CodeFirst.InitTables(typeof(AuditLog));

        var logs = new[]
        {
            new AuditLog { HttpMethod = "POST", Path = "/a", StatusCode = 200 },
            new AuditLog { HttpMethod = "POST", Path = "/b", StatusCode = 200 }
        };

        var result = await AuditLogPersister.PersistAsync(db, logs);

        Assert.True(result);
        var saved = db.Queryable<AuditLog>().ToList();
        Assert.Equal(2, saved.Count);
        Assert.All(saved, x => Assert.True(x.Id > 0));
    }

    [Fact]
    public async Task 数据库不可用时写入失败但不抛出()
    {
        using var db = new SqlSugarScope(new ConnectionConfig
        {
            DbType = DbType.Sqlite,
            ConnectionString = "Data Source=/nonexistent-dir-9f3k2/audit.db"
        });

        var result = await AuditLogPersister.PersistAsync(db, new[] { CreateLog(3) });

        Assert.False(result);
    }
}
