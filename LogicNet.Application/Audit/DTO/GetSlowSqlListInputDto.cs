namespace LogicNet.Application.Audit.DTO;

/// <summary>
///     慢 SQL 列表查询：支持时间区间与最小耗时过滤，分页沿用 BaseInput 的 1-100 钳制。
/// </summary>
public class GetSlowSqlListInputDto : BaseInput
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
    ///     最小耗时（毫秒，含）；未传时默认 200，与采集默认慢阈值一致
    /// </summary>
    public long? MinElapsedMs { get; set; }
}
