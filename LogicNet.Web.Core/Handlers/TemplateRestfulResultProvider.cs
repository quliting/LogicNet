using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Furion;
using Furion.DataValidation;
using Furion.FriendlyException;
using Furion.Logging;
using Furion.UnifyResult;
using LogicNet.Web.Core.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LogicNet.Web.Core;

public class TemplateRestfulResult<T>
{
    public int Code { get; set; }

    /// <summary>
    ///     数据
    /// </summary>
    public T Data { get; set; }

    /// <summary>
    ///     执行成功
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    ///     错误信息
    /// </summary>
    public object Errors { get; set; }

    /// <summary>
    ///     附加数据
    /// </summary>
    public object Extras { get; set; }

    /// <summary>
    ///     时间戳
    /// </summary>
    public long Timestamp { get; set; }
}

[UnifyModel(typeof(TemplateRestfulResult<>))]
public class TemplateRestfulResultProvider : IUnifyResultProvider
{
    public IActionResult OnAuthorizeException(DefaultHttpContext context, ExceptionMetadata metadata)
    {
        // 认证/授权失败按规范返回 401/403（此前抛 NotImplementedException 会被统一包装成 500）
        var statusCode = metadata.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
            ? metadata.StatusCode
            : StatusCodes.Status401Unauthorized;
        WriteAuditResult(context, statusCode, false);
        return new JsonResult(LogicRESTfulResult(statusCode, data: metadata.Data, errors: metadata.Errors),
            UnifyContext.GetSerializerSettings(context))
        {
            StatusCode = statusCode
        };
    }

    /// <summary>
    ///     异常返回值
    /// </summary>
    /// <param name="context"></param>
    /// <param name="metadata"></param>
    /// <returns></returns>
    public IActionResult OnException(ExceptionContext context, ExceptionMetadata metadata)
    {
        // 错误日志携带请求上下文（请求 ID/方法/路径），与控制台、文件日志的 WithTraceId 输出同源，可互相检索
        Log.Error("请求处理异常，TraceId={TraceId}, Method={Method}, Path={Path}", context.Exception,
            App.GetTraceId(), context.HttpContext?.Request.Method, context.HttpContext?.Request.Path.Value);
        WriteAuditResult(context.HttpContext, metadata.StatusCode, false);
        return new JsonResult(LogicRESTfulResult(metadata.StatusCode, data: metadata.Data, errors: metadata.Errors)
            , UnifyContext.GetSerializerSettings(context));
    }

    /// <summary>
    ///     成功返回值
    /// </summary>
    /// <param name="context"></param>
    /// <param name="data"></param>
    /// <returns></returns>
    public IActionResult OnSucceeded(ActionExecutedContext context, object data)
    {
        WriteAuditResult(context.HttpContext, StatusCodes.Status200OK, true);
        return new JsonResult(LogicRESTfulResult(StatusCodes.Status200OK, true, data)
            , UnifyContext.GetSerializerSettings(context));
    }

    /// <summary>
    ///     特定状态码返回值
    /// </summary>
    /// <param name="context"></param>
    /// <param name="statusCode"></param>
    /// <param name="unifyResultSettings"></param>
    /// <returns></returns>
    public async Task OnResponseStatusCodes(HttpContext context, int statusCode,
        UnifyResultSettingsOptions unifyResultSettings)
    {
        // 设置响应状态码
        UnifyContext.SetResponseStatusCodes(context, statusCode, unifyResultSettings);
        WriteAuditResult(context, statusCode, false);
        switch (statusCode)
        {
            // 处理 401 状态码
            case StatusCodes.Status401Unauthorized:
                await context.Response.WriteAsJsonAsync(LogicRESTfulResult(statusCode, errors: "401 Unauthorized")
                    , App.GetOptions<JsonOptions>()?.JsonSerializerOptions);
                break;
            // 处理 403 状态码
            case StatusCodes.Status403Forbidden:
                await context.Response.WriteAsJsonAsync(LogicRESTfulResult(statusCode, errors: "403 Forbidden")
                    , App.GetOptions<JsonOptions>()?.JsonSerializerOptions);
                break;
        }
    }

    public IActionResult OnValidateFailed(ActionExecutingContext context, ValidationMetadata metadata)
    {
        // 聚合返回全部校验错误：字典原样返回（每个字段的所有错误），字符串原样返回，不再强转抛异常，
        // 也不再用"非法操作"掩码真实错误信息
        object errors = metadata.ValidationResult switch
        {
            string errorMessage => errorMessage,
            Dictionary<string, string[]> errorDictionary => errorDictionary,
            { } other => other.ToString(),
            _ => "请求参数校验失败"
        };
        Log.Error($"参数校验失败：{JsonSerializer.Serialize(errors)}");

        var statusCode = metadata.StatusCode ?? StatusCodes.Status400BadRequest;
        WriteAuditResult(context.HttpContext, statusCode, false);
        return new JsonResult(LogicRESTfulResult(statusCode,
                data: metadata.Data, errors: errors)
            , UnifyContext.GetSerializerSettings(context));
    }

    /// <summary>
    ///     把业务结果码与成功状态写入 HttpContext.Items，供审计中间件采集；
    ///     不改变原响应结构，未经过统一结果处理的请求仍回退到 HTTP 状态码。
    /// </summary>
    private static void WriteAuditResult(HttpContext context, int statusCode, bool succeeded)
    {
        if (context is null) return;
        context.Items[AuditLogContextKeys.ResultCode] = statusCode;
        context.Items[AuditLogContextKeys.Succeeded] = succeeded;
    }

    /// <summary>
    ///     返回 RESTful 风格结果集
    /// </summary>
    /// <param name="statusCode"></param>
    /// <param name="succeeded"></param>
    /// <param name="data"></param>
    /// <param name="errors"></param>
    /// <returns></returns>
    private static TemplateRestfulResult<object> LogicRESTfulResult(int statusCode, bool succeeded = default,
        object data = default, object errors = default)
    {
        return new TemplateRestfulResult<object>
        {
            Succeeded = succeeded,
            Code = statusCode,
            Data = data,
            Errors = errors,
            Extras = UnifyContext.Take(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }
}