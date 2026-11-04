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
        return await db.Deleteable<AuditLog>()
            .Where(x => x.CreateTime < cutoff)
            .ExecuteCommandAsync();
    }
}
