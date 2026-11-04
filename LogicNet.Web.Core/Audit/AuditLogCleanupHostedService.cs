using System;
using System.Threading;
using System.Threading.Tasks;
using LogicNet.Application.Audit.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace LogicNet.Web.Core.Audit;

/// <summary>
///     审计日志每日清理：本地时间 CleanupHour 点执行一次，按 RetentionDays 物理删除过期记录。
///     独立于 IDeleteDirJob（后者 60 秒一次、只清理临时文件），不会每分钟扫审计表。
/// </summary>
public sealed class AuditLogCleanupHostedService : BackgroundService
{
    private readonly ILogger<AuditLogCleanupHostedService> _logger;
    private readonly AuditLogOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    public AuditLogCleanupHostedService(IOptions<AuditLogOptions> options, IServiceScopeFactory scopeFactory,
        ILogger<AuditLogCleanupHostedService> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextRun(DateTime.Now, _options.CleanupHour);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var days = Math.Clamp(_options.RetentionDays, 1, 3650);
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
                var cutoff = DateTime.Now.AddDays(-days);
                var deleted = await AuditLogCleanup.DeleteBeforeAsync(db, cutoff);
                _logger.LogInformation("审计日志清理完成：删除 {Count} 条，保留 {Days} 天（早于 {Cutoff}）",
                    deleted, days, cutoff);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "审计日志清理失败，下一周期自动重试");
            }
        }
    }

    /// <summary>
    ///     计算距离下一次清理时刻的等待时长；已过当天时刻则顺延到次日。
    /// </summary>
    public static TimeSpan ComputeDelayUntilNextRun(DateTime now, int cleanupHour)
    {
        var next = now.Date.AddHours(Math.Clamp(cleanupHour, 0, 23));
        if (now >= next) next = next.AddDays(1);
        return next - now;
    }
}
