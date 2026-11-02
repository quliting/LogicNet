using LogicNet.Web.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     /health 的数据库连通性检查：内存库可正常执行 SELECT 1；断连后返回 Unhealthy（P1-6）
/// </summary>
public class DatabaseHealthCheckTests
{
    [Fact]
    public async Task 数据库连通时返回Healthy()
    {
        using var db = TestDb.Create();
        var check = new DatabaseHealthCheck(db);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task 数据库断连时返回Unhealthy()
    {
        // 指向不存在目录的 Sqlite 库，连接必然失败（模拟 DB 不可用）
        using var db = new SqlSugar.SqlSugarScope(new SqlSugar.ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite,
            ConnectionString = "Data Source=/nonexistent-dir-9f3k2/probe.db"
        });
        var check = new DatabaseHealthCheck(db);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
