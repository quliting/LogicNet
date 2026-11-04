namespace LogicNet.Application.Audit.DTO;

/// <summary>
///     审计日志列表输出：不含请求体，避免列表响应过大；RequestBody 仅在详情接口返回（且已脱敏）。
/// </summary>
public class AuditLogOutputDto
{
    public long Id { get; set; }

    public long? UserId { get; set; }

    public string UserName { get; set; }

    public string HttpMethod { get; set; }

    public string Path { get; set; }

    public string QueryString { get; set; }

    public int StatusCode { get; set; }

    public int? ResultCode { get; set; }

    public bool Succeeded { get; set; }

    public string ClientIp { get; set; }

    public string UserAgent { get; set; }

    public string TraceId { get; set; }

    public long DurationMs { get; set; }

    public string ExceptionMessage { get; set; }

    /// <summary>
    ///     创建时间（对应实体 CreateTime）
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
