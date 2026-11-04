using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LogicNet.Core.Entity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace LogicNet.Web.Core.Audit;

/// <summary>
///     审计日志后台写入：从队列攒批（最多 100 条）后在独立作用域内落库，
///     数据库异常只记日志，不影响请求线程；进程退出时队列中未消费的记录允许丢失。
/// </summary>
public sealed class AuditLogWriterHostedService : BackgroundService
{
    private const int MaxBatchSize = 100;

    private readonly ILogger<AuditLogWriterHostedService> _logger;
    private readonly AuditLogQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;

    public AuditLogWriterHostedService(AuditLogQueue queue, IServiceScopeFactory scopeFactory,
        ILogger<AuditLogWriterHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var first in _queue.ReadAllAsync(stoppingToken))
        {
            var batch = new List<AuditLog>(MaxBatchSize) { first };
            while (batch.Count < MaxBatchSize && _queue.TryDequeue(out var next)) batch.Add(next);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
                await AuditLogPersister.PersistAsync(db, batch, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "审计日志后台写入失败：{Count} 条", batch.Count);
            }
        }
    }
}
