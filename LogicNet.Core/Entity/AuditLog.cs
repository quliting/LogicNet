using SqlSugar;

namespace LogicNet.Core.Entity;

/// <summary>
///     操作审计日志：由中间件在请求完成后异步落库，不侵入业务 Controller。
///     时间字段直接复用 BaseEntity 的 CreateTime（对外 DTO 命名为 CreatedAt）；
///     RequestBody 与 QueryString 在写入前已完成脱敏与截断，库中不保存明文密码/令牌。
/// </summary>
/// <remarks>
///     本表只追加不修改，IsDelete 仅因继承 BaseEntity 而存在；过期数据由清理任务物理删除。
/// </remarks>
[SugarIndex("IX_AuditLog_CreateTime", nameof(CreateTime), OrderByType.Desc)]
[SugarIndex("IX_AuditLog_UserId", nameof(UserId), OrderByType.Asc)]
[SugarIndex("IX_AuditLog_Path", nameof(Path), OrderByType.Asc)]
public class AuditLog : BaseEntity
{
    /// <summary>
    ///     操作用户 Id；未认证请求为 null
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public long? UserId { get; set; }

    /// <summary>
    ///     操作用户名；未认证请求记录为 anonymous
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 100)]
    public string UserName { get; set; }

    /// <summary>
    ///     HTTP 方法
    /// </summary>
    [SugarColumn(Length = 10)]
    public string HttpMethod { get; set; }

    /// <summary>
    ///     请求路径（不含查询串）
    /// </summary>
    [SugarColumn(Length = 500)]
    public string Path { get; set; }

    /// <summary>
    ///     查询串（已脱敏、截断）
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 1024)]
    public string QueryString { get; set; }

    /// <summary>
    ///     请求体（已脱敏、截断，最多 MaxBodyLength 字符）
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 2048)]
    public string RequestBody { get; set; }

    /// <summary>
    ///     HTTP 响应状态码
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    ///     业务结果码：当前与 StatusCode 一致（统一结果下业务失败仍为 200，需结合 Succeeded 判断）
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public int? ResultCode { get; set; }

    /// <summary>
    ///     请求是否成功（状态码小于 400 且未抛出异常）
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    ///     客户端 IP：优先取 X-Forwarded-For 首值，否则 RemoteIpAddress
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string ClientIp { get; set; }

    /// <summary>
    ///     User-Agent（截断）
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 500)]
    public string UserAgent { get; set; }

    /// <summary>
    ///     请求链路 Id，与响应头 x-request-id、文件日志 TraceId 同源
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 128)]
    public string TraceId { get; set; }

    /// <summary>
    ///     请求耗时（毫秒）
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    ///     未处理异常消息（已脱敏、截断）；正常请求为 null
    /// </summary>
    [SugarColumn(IsNullable = true, Length = 1024)]
    public string ExceptionMessage { get; set; }
}
