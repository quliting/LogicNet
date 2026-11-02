using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SqlSugar;

namespace LogicNet.Web.Core;

/// <summary>
///     数据库连通性健康检查：执行 SELECT 1 探活，失败返回 Unhealthy（/health 整体 503）
/// </summary>
public class DatabaseHealthCheck(ISqlSugarClient db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Ado.GetStringAsync("SELECT 1");
            return HealthCheckResult.Healthy("数据库连接正常");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("数据库连接失败", ex);
        }
    }
}
