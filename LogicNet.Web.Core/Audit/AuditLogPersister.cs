using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LogicNet.Core.Entity;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace LogicNet.Web.Core.Audit;

/// <summary>
///     审计日志落库：批量插入，任何失败都只返回 false 并记录 Error，不向调用方抛出。
/// </summary>
public static class AuditLogPersister
{
    private static int _tableReady;

    public static async Task<bool> PersistAsync(ISqlSugarClient db, IReadOnlyCollection<AuditLog> logs,
        ILogger logger = null)
    {
        if (logs is null || logs.Count == 0) return true;

        try
        {
            EnsureTable(db);
            await db.Insertable(logs.ToList()).ExecuteCommandAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "审计日志写入失败，丢弃 {Count} 条记录（不影响主请求）", logs.Count);
            return false;
        }
    }

    /// <summary>
    ///     首次写入前补建表结构：后台写入服务可能早于启动期的 DatabaseInitializer 开始工作，
    ///     幂等建表可避免首批日志因缺表全部丢失。失败时不置位，下一批自动重试。
    /// </summary>
    private static void EnsureTable(ISqlSugarClient db)
    {
        if (Volatile.Read(ref _tableReady) == 1) return;

        db.CodeFirst.InitTables(typeof(AuditLog));
        Interlocked.Exchange(ref _tableReady, 1);
    }
}
