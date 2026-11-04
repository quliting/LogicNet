namespace LogicNet.Application.Audit.DTO;

/// <summary>
///     审计日志详情：在列表字段基础上补充已脱敏、已截断的请求体。
/// </summary>
public class AuditLogDetailOutputDto : AuditLogOutputDto
{
    public string RequestBody { get; set; }
}
