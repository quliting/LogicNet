using SqlSugar;

namespace LogicNet.Core.Entity;

/// <summary>
///     审计 SQL 执行明细：与 AuditLog 一请求一对多，随审计记录一起异步落库与清理。
///     SqlText 与 Parameters 在采集阶段已完成脱敏与截断，库中不保存明文密码/令牌。
/// </summary>
/// <remarks>
///     AuditLogId 关联 AuditLog.Id；TraceId 冗余便于按链路直接检索。时间复用 BaseEntity 的
///     CreateTime（对外 DTO 命名为 CreatedAt），并与所属审计记录保持同一时刻。
/// </remarks>
[SugarIndex("IX_AuditSqlLog_AuditLogId", nameof(AuditLogId), OrderByType.Asc)]
[SugarIndex("IX_AuditSqlLog_TraceId", nameof(TraceId), OrderByType.Asc)]
[SugarIndex("IX_AuditSqlLog_CreateTime", nameof(CreateTime), OrderByType.Desc)]
public class AuditSqlLog : BaseEntity
{
    /// <summary>
    ///     所属审计记录 Id
    /// </summary>
    public long AuditLogId { get; set; }

    /// <summary>
    ///     请求链路 Id，与所属 AuditLog.TraceId 一致
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 128)]
    public string TraceId { get; set; }

    /// <summary>
    ///     执行的 SQL 文本（已脱敏、截断）
    /// </summary>
    [SugarColumn(Length = 2000)]
    public string SqlText { get; set; }

    /// <summary>
    ///     参数列表（键值对形式，敏感参数值已掩码，整体截断）
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 2000)]
    public string Parameters { get; set; }

    /// <summary>
    ///     单条 SQL 净耗时（毫秒）
    /// </summary>
    public long ElapsedMs { get; set; }

    /// <summary>
    ///     是否超过慢阈值（AuditLog.Sql.SlowThresholdMs）
    /// </summary>
    public bool IsSlow { get; set; }

    /// <summary>
    ///     执行失败原因（已脱敏、截断）；成功执行为 null
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 1000)]
    public string ErrorMessage { get; set; }
}
