namespace LogicNet.Web.Core.Audit;

/// <summary>
///     操作审计采集配置（appsettings 节点 AuditLog）。默认只记录写操作与登录类接口，
///     避免 GET/静态资源把日志量放大；所有项都可以通过配置覆盖。
/// </summary>
public class AuditLogOptions
{
    /// <summary>
    ///     总开关：关闭后中间件直接放行，不采集任何请求
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     是否记录 GET 请求（默认关闭，需要全量留痕时开启）
    /// </summary>
    public bool IncludeGet { get; set; } = false;

    /// <summary>
    ///     是否记录登录/注册/刷新/登出等认证类接口（即使方法不是写操作）
    /// </summary>
    public bool RecordLoginEndpoints { get; set; } = true;

    /// <summary>
    ///     是否记录请求体（关闭后 RequestBody 恒为空）
    /// </summary>
    public bool RecordRequestBody { get; set; } = true;

    /// <summary>
    ///     请求体最大记录长度（字符），超出截断
    /// </summary>
    public int MaxBodyLength { get; set; } = 2048;

    /// <summary>
    ///     查询串最大记录长度（字符），超出截断
    /// </summary>
    public int MaxQueryStringLength { get; set; } = 1024;

    /// <summary>
    ///     异常消息最大记录长度（字符），超出截断
    /// </summary>
    public int MaxExceptionLength { get; set; } = 1024;

    /// <summary>
    ///     不采集的路径前缀（大小写不敏感）。健康检查与审计查询接口自身默认跳过，避免自我放大。
    /// </summary>
    public string[] SkipPaths { get; set; } =
    {
        "/health", "/Api/AuditLog", "/swagger", "/api-docs"
    };

    /// <summary>
    ///     写入队列容量：满时丢弃新记录并记 Warning，保证不阻塞请求线程
    /// </summary>
    public int QueueCapacity { get; set; } = 10000;

    /// <summary>
    ///     数据保留天数（每日清理任务使用，默认 90 天）
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    ///     每日清理执行时刻（0-23，本地时间，默认凌晨 3 点）
    /// </summary>
    public int CleanupHour { get; set; } = 3;

    /// <summary>
    ///     SQL 执行明细采集配置（节点 AuditLog.Sql）
    /// </summary>
    public AuditSqlOptions Sql { get; set; } = new();
}
