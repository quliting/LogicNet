using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using SqlSugar;

namespace LogicNet.Web.Core.Audit;

/// <summary>
///     SqlSugar AOP 到审计采集之间的桥：只从当前 HttpContext 取当前请求的采集缓冲区。
/// </summary>
/// <remarks>
///     关联依据是 HttpContext.Items，因此同一请求内的 SQL 一定落到同一个缓冲区。
///     后台任务、定时任务没有 HttpContext（或请求已结束、缓冲区已关闭）时直接跳过，
///     不会误挂到其他请求的审计记录上。
/// </remarks>
public static class AuditSqlCapture
{
    /// <summary>
    ///     parameters 使用 object 以兼容 SqlSugar 异常对象的 Parametres 属性（运行时类型可能是数组或集合）。
    /// </summary>
    public static void Capture(string sql, object parameters, TimeSpan elapsed,
        IHttpContextAccessor httpContextAccessor, string errorMessage = null)
    {
        try
        {
            var context = httpContextAccessor?.HttpContext;
            if (context is null) return;

            if (context.Items[AuditLogContextKeys.SqlCollector] is not AuditSqlCollector collector) return;
            if (collector.IsClosed) return;

            collector.Add(sql, ToSugarParameters(parameters), (long)elapsed.TotalMilliseconds, errorMessage);
        }
        catch
        {
            // SQL 采集失败绝不向业务执行链路抛异常
        }
    }

    private static SugarParameter[] ToSugarParameters(object parameters)
    {
        return parameters switch
        {
            SugarParameter[] array => array,
            IEnumerable<SugarParameter> list => list.ToArray(),
            _ => null
        };
    }
}
