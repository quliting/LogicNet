using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Furion.Logging;
using LogicNet.Core.Audit;
using LogicNet.Core.Entity;
using LogicNet.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LogicNet.Web.Core.Audit;

/// <summary>
///     操作审计中间件：在请求完成后采集方法、路径、状态码、用户、耗时与脱敏后的请求体，
///     非阻塞入队后交给后台服务落库。任何采集/入队异常都只记日志，绝不影响业务响应。
/// </summary>
public sealed class AuditLogMiddleware
{
    private const int MaxUserAgentLength = 300;
    private const int MaxTraceIdLength = 128;
    private const int MaxPathLength = 500;
    private const int MaxClientIpLength = 64;

    private static readonly string[] WriteMethods = { "POST", "PUT", "PATCH", "DELETE" };

    private static readonly string[] StaticFileExtensions =
    {
        ".js", ".css", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico",
        ".woff", ".woff2", ".ttf", ".eot", ".html", ".htm", ".txt", ".xml", ".pdf", ".webp", ".mp4"
    };

    private static readonly string[] LoginEndpointKeywords =
    {
        "/Login", "/Register", "/Refresh", "/Logout"
    };

    private readonly AuditLogOptions _options;
    private readonly AuditLogQueue _queue;
    private readonly RequestDelegate _next;

    public AuditLogMiddleware(RequestDelegate next, AuditLogQueue queue, IOptions<AuditLogOptions> options)
    {
        _next = next;
        _queue = queue;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || ShouldSkip(context.Request))
        {
            await _next(context);
            return;
        }

        var shouldRecord = ShouldRecord(context.Request);
        var slowGetTracking = !shouldRecord && IsGetRequest(context.Request) && _options.Sql.AuditSlowGet;

        // SQL 明细采集：对会被审计的请求开启；另外默认对未审计的 GET 做“慢 SQL 探测”，
        // 命中慢阈值时补一条审计记录。缓冲区挂在 HttpContext.Items 上，与请求一一对应，
        // 保证同一请求的 SQL 不会串到其他审计记录
        AuditSqlCollector sqlCollector = null;
        if (_options.Sql.Enabled && (shouldRecord || slowGetTracking))
        {
            // 探测型 GET 只保留慢 SQL/失败 SQL，避免 RecordAll 打开时为不落库的请求全量格式化
            sqlCollector = new AuditSqlCollector(_options.Sql, forceSlowOnly: !shouldRecord);
            context.Items[AuditLogContextKeys.SqlCollector] = sqlCollector;
        }

        string requestBody = null;
        if (shouldRecord && _options.RecordRequestBody)
        {
            try
            {
                requestBody = await ReadRequestBodyAsync(context.Request);
            }
            catch (Exception ex)
            {
                // 读取请求体失败不应影响业务请求，最多丢失该条的请求体内容
                Log.Warning($"审计日志读取请求体失败：{context.Request.Method} {context.Request.Path}，原因：{ex.Message}");
            }
        }

        var stopwatch = Stopwatch.StartNew();
        Exception exception = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            AuditSqlSnapshot sqlSnapshot = null;
            if (sqlCollector is not null)
            {
                try
                {
                    // 先关闭采集再组装审计记录：请求结束后仍在运行的异步任务不会再追加到本记录
                    sqlSnapshot = sqlCollector.Close();
                }
                catch (Exception ex)
                {
                    Log.Error("审计 SQL 采集快照失败（已忽略）", ex);
                }
            }

            // 默认不审计的 GET 仅在出现慢 SQL 时才补一条审计记录（开关 AuditSlowGet）；
            // 成功状态仍按请求实际结果记录，便于从慢 SQL 反查具体接口与操作人
            var shouldWrite = shouldRecord
                              || (slowGetTracking && sqlSnapshot is { SlowCount: > 0 });

            if (shouldWrite)
            {
                try
                {
                    _queue.Enqueue(BuildLog(context, requestBody, stopwatch.ElapsedMilliseconds, exception,
                        sqlSnapshot));
                }
                catch (Exception ex)
                {
                    // 采集失败只记录错误日志，绝不让审计能力反向影响主流程
                    Log.Error("审计日志采集失败（已忽略）", ex);
                }
            }

            if (sqlCollector is not null) context.Items.Remove(AuditLogContextKeys.SqlCollector);
        }
    }

    private AuditLog BuildLog(HttpContext context, string requestBody, long durationMs, Exception exception,
        AuditSqlSnapshot sqlSnapshot)
    {
        var request = context.Request;
        var userId = long.TryParse(context.User?.FindFirstValue(JwtClaimNames.UserId), out var parsedUserId)
            ? parsedUserId
            : (long?)null;

        var userName = context.User?.FindFirstValue(JwtClaimNames.Account)
                       ?? context.User?.FindFirstValue(ClaimTypes.Name)
                       ?? "anonymous";
        if (string.IsNullOrWhiteSpace(userName)) userName = "anonymous";

        var statusCode = context.Response.StatusCode;

        // 统一结果下业务失败仍是 HTTP 200，优先采用统一结果写入的业务码与成功状态
        var resultCode = statusCode;
        if (context.Items.TryGetValue(AuditLogContextKeys.ResultCode, out var resultCodeItem)
            && resultCodeItem is int businessCode)
            resultCode = businessCode;

        var succeeded = statusCode < 400 && exception is null;
        if (context.Items.TryGetValue(AuditLogContextKeys.Succeeded, out var succeededItem)
            && succeededItem is bool businessSucceeded)
            succeeded = businessSucceeded && exception is null;

        var traceId = AuditLogSanitizer.Truncate(Activity.Current?.Id ?? context.TraceIdentifier, MaxTraceIdLength);
        var createTime = DateTime.Now;

        var log = new AuditLog
        {
            UserId = userId,
            UserName = AuditLogSanitizer.Truncate(userName, 100),
            HttpMethod = request.Method,
            Path = AuditLogSanitizer.Truncate(request.Path.Value ?? "/", MaxPathLength),
            QueryString = AuditLogSanitizer.SanitizeText(request.QueryString.Value, _options.MaxQueryStringLength),
            RequestBody = AuditLogSanitizer.SanitizeBody(requestBody, _options.MaxBodyLength),
            StatusCode = statusCode,
            ResultCode = resultCode,
            Succeeded = succeeded,
            ClientIp = GetClientIp(context),
            UserAgent = AuditLogSanitizer.Truncate(request.Headers.UserAgent.ToString(), MaxUserAgentLength),
            TraceId = traceId,
            DurationMs = durationMs,
            ExceptionMessage = exception is null
                ? null
                : AuditLogSanitizer.SanitizeText(exception.Message, _options.MaxExceptionLength),
            SqlCount = sqlSnapshot is null ? 0 : (int)Math.Min(sqlSnapshot.Count, int.MaxValue),
            SqlElapsedMs = sqlSnapshot?.ElapsedMs ?? 0,
            SlowSqlCount = sqlSnapshot?.SlowCount ?? 0,
            CreateTime = createTime
        };

        // 明细只随审计记录在内存中传递，由后台批量落库统一写 AuditSqlLog；
        // AuditLogId 在写库时由 Persister 回填，避免此处依赖尚未生成的主键
        if (sqlSnapshot is { Items.Count: > 0 })
            log.SqlLogs = sqlSnapshot.Items
                .Select(item => new AuditSqlLog
                {
                    TraceId = traceId,
                    SqlText = item.SqlText,
                    Parameters = item.Parameters,
                    ElapsedMs = item.ElapsedMs,
                    IsSlow = item.IsSlow,
                    ErrorMessage = item.ErrorMessage,
                    CreateTime = createTime
                })
                .ToList();

        return log;
    }

    private bool ShouldSkip(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;

        foreach (var skipPath in _options.SkipPaths ?? Array.Empty<string>())
            if (!string.IsNullOrWhiteSpace(skipPath)
                && path.StartsWith(skipPath, StringComparison.OrdinalIgnoreCase))
                return true;

        // 静态文件（路由前的 UseStaticFiles 已短路大多数请求，这里作为兜底）
        var lastSlash = path.LastIndexOf('/');
        var fileName = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        var dot = fileName.LastIndexOf('.');
        if (dot > 0)
        {
            var extension = fileName[dot..];
            if (StaticFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool IsGetRequest(HttpRequest request)
    {
        return HttpMethods.IsGet(request.Method);
    }

    private bool ShouldRecord(HttpRequest request)
    {
        if (WriteMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase)) return true;

        if (_options.IncludeGet && HttpMethods.IsGet(request.Method)) return true;

        if (_options.RecordLoginEndpoints)
        {
            var path = request.Path.Value ?? string.Empty;
            if (LoginEndpointKeywords.Any(keyword =>
                    path.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        if (!request.Body.CanRead || !IsTextContentType(request.ContentType)) return null;
        if (request.ContentLength == 0) return null;

        // 允许重复读取：业务侧模型绑定仍能拿到完整请求体
        request.EnableBuffering();

        var maxChars = Math.Clamp(_options.MaxBodyLength * 4, 4096, 65536);
        var buffer = new char[maxChars];
        using var reader = new StreamReader(request.Body, Encoding.UTF8, false, maxChars, leaveOpen: true);
        try
        {
            var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            if (read <= 0) return null;

            var text = new string(buffer, 0, read);
            return read >= buffer.Length ? text + "...(raw-truncated)" : text;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static bool IsTextContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        return contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
               || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetClientIp(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',', 2)[0].Trim();
            if (first.Length > 0) return AuditLogSanitizer.Truncate(first, MaxClientIpLength);
        }

        return AuditLogSanitizer.Truncate(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", MaxClientIpLength);
    }
}
