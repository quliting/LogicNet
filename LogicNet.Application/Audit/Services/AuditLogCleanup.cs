using LogicNet.Core.Entity;

namespace LogicNet.Application.Audit.Services;

/// <summary>
///     审计日志清理：按创建时间物理删除（非软删除），可重复执行。
///     管理端清理接口与每日后台任务共用同一实现，避免两套删除条件不一致。
/// </summary>
public static class AuditLogCleanup
{
    public static async Task<int> DeleteBeforeAsync(ISqlSugarClient db, DateTime cutoff)
    {
        // 无外键级联：先按过期审计记录 Id 分块删除明细，再删除主记录，避免留下孤儿明细。
        // 分块是为了避免单条 DELETE 的 IN 列表过长。
        var expiredIds = await db.Queryable<AuditLog>()
            .Where(x => x.CreateTime < cutoff)
            .Select(x => x.Id)
            .ToListAsync();

        const int chunkSize = 1000;
        foreach (var ids in expiredIds.Chunk(chunkSize))
            await db.Deleteable<AuditSqlLog>()
                .Where(x => ids.Contains(x.AuditLogId))
                .ExecuteCommandAsync();

        return await db.Deleteable<AuditLog>()
            .Where(x => x.CreateTime < cutoff)
            .ExecuteCommandAsync();
    }
}
