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
            if (shouldRecord)
            {
                try
                {
                    _queue.Enqueue(BuildLog(context, requestBody, stopwatch.ElapsedMilliseconds, exception));
                }
                catch (Exception ex)
                {
                    // 采集失败只记录错误日志，绝不让审计能力反向影响主流程
                    Log.Error("审计日志采集失败（已忽略）", ex);
                }
            }
        }
    }

    private AuditLog BuildLog(HttpContext context, string requestBody, long durationMs, Exception exception)
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

        return new AuditLog
        {
            UserId = userId,
            UserName = AuditLogSanitizer.Truncate(userName, 100),
            HttpMethod = request.Method,
            Path = AuditLogSanitizer.Truncate(request.Path.Value ?? "/", MaxPathLength),
            QueryString = AuditLogSanitizer.SanitizeText(request.QueryString.Value, _options.MaxQueryStringLength),
            RequestBody = AuditLogSanitizer.SanitizeBody(requestBody, _options.MaxBodyLength),
            StatusCode = statusCode,
            // 统一结果下业务失败仍是 HTTP 200，ResultCode 与状态码保持一致，由 Succeeded 区分业务结果
            ResultCode = statusCode,
            Succeeded = statusCode < 400 && exception is null,
            ClientIp = GetClientIp(context),
            UserAgent = AuditLogSanitizer.Truncate(request.Headers.UserAgent.ToString(), MaxUserAgentLength),
            TraceId = AuditLogSanitizer.Truncate(Activity.Current?.Id ?? context.TraceIdentifier, MaxTraceIdLength),
            DurationMs = durationMs,
            ExceptionMessage = exception is null
                ? null
                : AuditLogSanitizer.SanitizeText(exception.Message, _options.MaxExceptionLength),
            CreateTime = DateTime.Now
        };
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
