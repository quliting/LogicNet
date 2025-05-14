using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Furion;
using Furion.DataValidation;
using Furion.FriendlyException;
using Furion.Logging;
using Furion.UnifyResult;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;

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
        throw new NotImplementedException();
    }

    /// <summary>
    ///     异常返回值
    /// </summary>
    /// <param name="context"></param>
    /// <param name="metadata"></param>
    /// <returns></returns>
    public IActionResult OnException(ExceptionContext context, ExceptionMetadata metadata)
    {
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
        var errorStr = string.Empty;
        if (metadata.ValidationResult is string)
        {
            errorStr = (string)metadata.ValidationResult;
        }
        else
        {
            var errorsDic = (Dictionary<string, string[]>)metadata.ValidationResult;

            foreach (var item in errorsDic) errorStr = item.Value[0];
            Log.Error($"非法操作{errorStr}");
            if (!App.HostEnvironment.IsDevelopment()) errorStr = "非法操作";
        }

        return new JsonResult(LogicRESTfulResult(metadata.StatusCode ?? StatusCodes.Status400BadRequest,
                data: metadata.Data, errors: errorStr)
            , UnifyContext.GetSerializerSettings(context));
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