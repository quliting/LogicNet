using LogicNet.Application.Audit.DTO;
using LogicNet.Core.Entity;
using Masuit.Tools;
using Masuit.Tools.Models;

namespace LogicNet.Application.Audit.Services;

/// <summary>
///     审计日志查询与清理（仅 Admin）。列表中不返回请求体，详情返回的请求体在采集阶段
///     已完成脱敏与截断，因此接口不会暴露明文密码或令牌。
/// </summary>
public class AuditLogService(ISqlSugarClient db) : LogicBase
{
    private const int DefaultRetentionDays = 90;
    private const int MaxRetentionDays = 3650;

    private readonly ISqlSugarClient db = db;

    /// <summary>
    ///     分页列表：时间区间、用户、路径关键字、成功状态、HTTP 方法过滤（仅 Admin）
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task<PagedList<AuditLogOutputDto>> GetAuditLogListAsync(GetAuditLogListInputDto input)
    {
        var httpMethod = input.HttpMethod?.Trim().ToUpperInvariant();

        var query = db.Queryable<AuditLog>();

        // 时间等可空条件用显式分支：避免可空 .Value 在 ORM 表达式中被忽略
        if (input.StartTime.HasValue)
        {
            var startTime = input.StartTime.Value;
            query = query.Where(x => x.CreateTime >= startTime);
        }

        if (input.EndTime.HasValue)
        {
            var endTime = input.EndTime.Value;
            query = query.Where(x => x.CreateTime <= endTime);
        }

        if (input.UserId.HasValue)
        {
            var userId = input.UserId.Value;
            query = query.Where(x => x.UserId == userId);
        }

        if (input.Succeeded.HasValue)
        {
            var succeeded = input.Succeeded.Value;
            query = query.Where(x => x.Succeeded == succeeded);
        }

        query = query
            .WhereIF(input.UserName.NotNullOrEmpty(), x => x.UserName.Contains(input.UserName))
            .WhereIF(input.Path.NotNullOrEmpty(), x => x.Path.Contains(input.Path))
            .WhereIF(!httpMethod.IsNullOrEmpty(), x => x.HttpMethod == httpMethod)
            .OrderByDescending(x => x.CreateTime)
            .OrderByDescending(x => x.Id);

        return await query
            .Select<AuditLogOutputDto>(x => new AuditLogOutputDto
            {
                Id = x.Id,
                UserId = x.UserId,
                UserName = x.UserName,
                HttpMethod = x.HttpMethod,
                Path = x.Path,
                QueryString = x.QueryString,
                StatusCode = x.StatusCode,
                ResultCode = x.ResultCode,
                Succeeded = x.Succeeded,
                ClientIp = x.ClientIp,
                UserAgent = x.UserAgent,
                TraceId = x.TraceId,
                DurationMs = x.DurationMs,
                ExceptionMessage = x.ExceptionMessage,
                SqlCount = x.SqlCount,
                SqlElapsedMs = x.SqlElapsedMs,
                SlowSqlCount = x.SlowSqlCount,
                CreatedAt = x.CreateTime
            })
            .PagedList(input.PageIndex, input.PageSize);
    }

    /// <summary>
    ///     详情：包含已脱敏的请求体（仅 Admin）
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task<AuditLogDetailOutputDto> GetAuditLogDetailAsync(long id)
    {
        if (id <= 0) throw Oops.Bah("审计日志 Id 无效");

        var result = await db.Queryable<AuditLog>()
            .Where(x => x.Id == id)
            .Select<AuditLogDetailOutputDto>(x => new AuditLogDetailOutputDto
            {
                Id = x.Id,
                UserId = x.UserId,
                UserName = x.UserName,
                HttpMethod = x.HttpMethod,
                Path = x.Path,
                QueryString = x.QueryString,
                RequestBody = x.RequestBody,
                StatusCode = x.StatusCode,
                ResultCode = x.ResultCode,
                Succeeded = x.Succeeded,
                ClientIp = x.ClientIp,
                UserAgent = x.UserAgent,
                TraceId = x.TraceId,
                DurationMs = x.DurationMs,
                ExceptionMessage = x.ExceptionMessage,
                SqlCount = x.SqlCount,
                SqlElapsedMs = x.SqlElapsedMs,
                SlowSqlCount = x.SlowSqlCount,
                CreatedAt = x.CreateTime
            })
            .FirstAsync();

        if (result is null) throw Oops.Bah("审计日志不存在");

        // 明细按主键升序返回，与 SQL 实际执行顺序一致
        result.SqlLogs = await db.Queryable<AuditSqlLog>()
            .Where(x => x.AuditLogId == id)
            .OrderBy(x => x.Id)
            .Select<AuditSqlLogOutputDto>(x => new AuditSqlLogOutputDto
            {
                Id = x.Id,
                AuditLogId = x.AuditLogId,
                TraceId = x.TraceId,
                SqlText = x.SqlText,
                Parameters = x.Parameters,
                ElapsedMs = x.ElapsedMs,
                IsSlow = x.IsSlow,
                ErrorMessage = x.ErrorMessage,
                CreatedAt = x.CreateTime
            })
            .ToListAsync();

        return result;
    }

    /// <summary>
    ///     慢 SQL 列表：按时间区间与最小耗时过滤，附带所属请求的路径与操作人（仅 Admin）
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task<PagedList<SlowSqlOutputDto>> GetSlowSqlListAsync(GetSlowSqlListInputDto input)
    {
        // 与采集默认慢阈值保持一致；显式传 0 时可查看全部明细
        var minElapsedMs = Math.Max(0, input.MinElapsedMs ?? 200);

        var query = db.Queryable<AuditSqlLog>()
            .LeftJoin<AuditLog>((sql, log) => sql.AuditLogId == log.Id);

        // 时间条件用显式分支，避免多表 WhereIF 的可空表达式在 ORM 中被忽略
        if (input.StartTime.HasValue)
        {
            var startTime = input.StartTime.Value;
            query = query.Where((sql, log) => sql.CreateTime >= startTime);
        }

        if (input.EndTime.HasValue)
        {
            var endTime = input.EndTime.Value;
            query = query.Where((sql, log) => sql.CreateTime <= endTime);
        }

        query = query.Where((sql, log) => sql.ElapsedMs >= minElapsedMs);

        return await query
            .OrderByDescending((sql, log) => sql.ElapsedMs)
            .OrderByDescending((sql, log) => sql.Id)
            .Select((sql, log) => new SlowSqlOutputDto
            {
                Id = sql.Id,
                AuditLogId = sql.AuditLogId,
                TraceId = sql.TraceId,
                SqlText = sql.SqlText,
                Parameters = sql.Parameters,
                ElapsedMs = sql.ElapsedMs,
                IsSlow = sql.IsSlow,
                ErrorMessage = sql.ErrorMessage,
                CreatedAt = sql.CreateTime,
                UserName = log.UserName,
                Path = log.Path,
                HttpMethod = log.HttpMethod
            })
            .PagedList(input.PageIndex, input.PageSize);
    }

    /// <summary>
    ///     手动清理入口（幂等）：删除 retentionDays 天前的记录，未传时默认 90 天（仅 Admin）
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task<int> CleanupAuditLogsAsync(int? retentionDays = null)
    {
        var days = retentionDays ?? DefaultRetentionDays;
        if (days < 1 || days > MaxRetentionDays) throw Oops.Bah("保留天数需在 1-3650 之间");

        return await AuditLogCleanup.DeleteBeforeAsync(db, DateTime.Now.AddDays(-days));
    }
}
