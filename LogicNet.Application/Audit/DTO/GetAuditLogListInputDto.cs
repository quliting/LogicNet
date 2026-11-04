namespace LogicNet.Application.Audit.DTO;

/// <summary>
///     审计日志列表查询：支持时间区间、用户、路径关键字、成功状态与 HTTP 方法过滤。
///     分页沿用 BaseInput 的 PageSize 1-100 钳制。
/// </summary>
public class GetAuditLogListInputDto : BaseInput
{
    /// <summary>
    ///     起始时间（含）
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    ///     结束时间（含）
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    ///     操作用户 Id（精确匹配）
    /// </summary>
    public long? UserId { get; set; }

    /// <summary>
    ///     用户名（模糊匹配）
    /// </summary>
    public string UserName { get; set; }

    /// <summary>
    ///     请求路径关键字（模糊匹配）
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    ///     是否成功
    /// </summary>
    public bool? Succeeded { get; set; }

    /// <summary>
    ///     HTTP 方法（不区分大小写，如 POST/GET）
    /// </summary>
    public string HttpMethod { get; set; }
}
