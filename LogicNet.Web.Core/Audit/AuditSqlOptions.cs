namespace LogicNet.Web.Core.Audit;

/// <summary>
///     SQL 执行明细采集配置（appsettings 节点 AuditLog.Sql）。
///     默认只记录超过慢阈值的语句与执行失败的语句，避免正常接口把明细表放大；
///     临时排查接口时可打开 RecordAll 记录全部 SQL，无需发版。
/// </summary>
public class AuditSqlOptions
{
    /// <summary>
    ///     明细采集开关：关闭后仍保留 AuditLog 的汇总字段（为 0），不产生 AuditSqlLog 记录
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     是否记录全部 SQL（默认关闭）。关闭时只记录慢 SQL 与执行失败的 SQL；
    ///     临时排查接口问题时可改为 true，但会显著增加明细数据量
    /// </summary>
    public bool RecordAll { get; set; } = false;

    /// <summary>
    ///     慢 SQL 阈值（毫秒）：达到或超过该值的语句标记为慢 SQL 并写入明细（不限请求方法）。
    ///     默认 500ms：NAS 上常规写菜单约 220-300ms，200ms 会产生大量无意义慢 SQL 噪音；
    ///     排查时可临时调低（如 100ms），改完即可观察命中情况
    /// </summary>
    public int SlowThresholdMs { get; set; } = 500;

    /// <summary>
    ///     慢 GET 入审计（默认开启）：GET 请求本身不生成审计主记录（除非 IncludeGet=true），
    ///     但当请求内出现达到慢阈值的 SQL 时，单独生成一条审计记录以便追溯慢查询。
    ///     false 时保持旧行为：GET 完全不记录。仅在 Sql.Enabled=true 时生效
    /// </summary>
    public bool AuditSlowGet { get; set; } = true;

    /// <summary>
    ///     单条 SQL 文本最大长度（字符），超出截断
    /// </summary>
    public int MaxSqlLength { get; set; } = 2000;

    /// <summary>
    ///     单条 SQL 参数列表最大长度（字符），超出截断
    /// </summary>
    public int MaxParametersLength { get; set; } = 2000;

    /// <summary>
    ///     单条审计记录的明细条数上限；超出部分不再记录，并追加一条“已截断”标记明细
    /// </summary>
    public int MaxItemsPerAuditLog { get; set; } = 50;
}
