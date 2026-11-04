namespace LogicNet.Application.Audit.DTO;

/// <summary>
///     审计日志详情：在列表字段基础上补充已脱敏、已截断的请求体。
/// </summary>
public class AuditLogDetailOutputDto : AuditLogOutputDto
{
    public string RequestBody { get; set; }

    /// <summary>
    ///     该请求的 SQL 执行明细（已脱敏、截断）；未采集或无明细时为空数组
    /// </summary>
    public List<AuditSqlLogOutputDto> SqlLogs { get; set; } = new();
}
