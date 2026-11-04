namespace LogicNet.Web.Core.Audit;

/// <summary>
///     统一结果与审计中间件之间的轻量传递键：由 IUnifyResultProvider 写入，中间件读取，
///     用于记录业务结果码与真实成功状态（HTTP 200 也可能表示业务失败）。
/// </summary>
public static class AuditLogContextKeys
{
    public const string ResultCode = "__AuditLog_ResultCode";
    public const string Succeeded = "__AuditLog_Succeeded";
}
