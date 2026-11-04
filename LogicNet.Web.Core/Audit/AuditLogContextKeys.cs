namespace LogicNet.Web.Core.Audit;

/// <summary>
///     统一结果与审计中间件之间的轻量传递键：由 IUnifyResultProvider 写入，中间件读取，
///     用于记录业务结果码与真实成功状态（HTTP 200 也可能表示业务失败）。
/// </summary>
public static class AuditLogContextKeys
{
    public const string ResultCode = "__AuditLog_ResultCode";
    public const string Succeeded = "__AuditLog_Succeeded";

    /// <summary>
    ///     当前请求的 SQL 采集缓冲区；仅本请求可见，AOP 钩子通过 HttpContext.Items 取用，
    ///     天然把同一请求内的多条 SQL 归到同一审计记录，不会串到其他请求
    /// </summary>
    public const string SqlCollector = "__AuditLog_SqlCollector";
}
