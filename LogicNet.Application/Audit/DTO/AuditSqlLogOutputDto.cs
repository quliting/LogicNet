using System.Text.Json.Serialization;

namespace LogicNet.Application.Audit.DTO;

/// <summary>
///     SQL 执行明细输出：SqlText 与 Parameters 在采集阶段已脱敏、截断。
/// </summary>
public class AuditSqlLogOutputDto
{
    public long Id { get; set; }

    public long AuditLogId { get; set; }

    public string TraceId { get; set; }

    public string SqlText { get; set; }

    public string Parameters { get; set; }

    public long ElapsedMs { get; set; }

    public bool IsSlow { get; set; }

    /// <summary>
    ///     执行失败原因（已脱敏、截断）；成功执行为 null
    /// </summary>
    public string ErrorMessage { get; set; }

    [JsonConverter(typeof(AuditLogDateTimeConverter))]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
///     慢 SQL 列表输出：在明细字段基础上补充所属审计记录的请求路径与操作人，便于定位来源。
/// </summary>
public class SlowSqlOutputDto : AuditSqlLogOutputDto
{
    public string UserName { get; set; }

    public string Path { get; set; }

    public string HttpMethod { get; set; }
}
