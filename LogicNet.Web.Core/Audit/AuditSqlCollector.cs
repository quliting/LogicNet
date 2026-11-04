using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LogicNet.Core.Audit;
using SqlSugar;

namespace LogicNet.Web.Core.Audit;

/// <summary>
///     单条 SQL 采集结果（脱敏、截断后），仅内存传递。
/// </summary>
public sealed class AuditSqlCaptureItem
{
    public string SqlText { get; init; }

    public string Parameters { get; init; }

    public long ElapsedMs { get; init; }

    public bool IsSlow { get; init; }

    /// <summary>
    ///     执行失败原因（已脱敏、截断）；成功执行为 null
    /// </summary>
    public string ErrorMessage { get; init; }
}

/// <summary>
///     请求结束时的 SQL 采集快照：汇总计数用于 AuditLog，明细用于 AuditSqlLog。
/// </summary>
public sealed class AuditSqlSnapshot
{
    public AuditSqlSnapshot(IReadOnlyList<AuditSqlCaptureItem> items, long count, long elapsedMs, int slowCount)
    {
        Items = items;
        Count = count;
        ElapsedMs = elapsedMs;
        SlowCount = slowCount;
    }

    public IReadOnlyList<AuditSqlCaptureItem> Items { get; }

    public long Count { get; }

    public long ElapsedMs { get; }

    public int SlowCount { get; }
}

/// <summary>
///     请求内 SQL 采集缓冲：AOP 钩子只做内存追加，不访问数据库，不抛异常。
/// </summary>
/// <remarks>
///     记录范围（默认）：只记录达到慢阈值的 SQL 与执行失败的 SQL；RecordAll=true 时记录全部 SQL。
///     无论是否记录明细，都会累计执行条数、净耗时与慢 SQL 条数，用于审计汇总字段。
///     明细条数达到上限后停止追加，并在快照末尾追加一条“已截断”标记。
/// </remarks>
public sealed class AuditSqlCollector
{
    private const int MaxErrorLength = 1000;

    private readonly object _gate = new();
    private readonly List<AuditSqlCaptureItem> _items = new();
    private readonly bool _recordAll;
    private readonly int _slowThresholdMs;
    private readonly int _maxSqlLength;
    private readonly int _maxParametersLength;
    private readonly int _maxItems;

    private long _count;
    private long _elapsedMs;
    private int _slowCount;
    private int _droppedCount;
    private bool _closed;

    public AuditSqlCollector(AuditSqlOptions options)
    {
        options ??= new AuditSqlOptions();
        _recordAll = options.RecordAll;
        _slowThresholdMs = Math.Clamp(options.SlowThresholdMs, 0, 60000);
        _maxSqlLength = Math.Clamp(options.MaxSqlLength, 100, 10000);
        _maxParametersLength = Math.Clamp(options.MaxParametersLength, 100, 10000);
        _maxItems = Math.Clamp(options.MaxItemsPerAuditLog, 1, 1000);
    }

    public bool IsClosed
    {
        get
        {
            lock (_gate) return _closed;
        }
    }

    /// <summary>
    ///     追加一条 SQL 执行记录；失败 SQL 通过 errorMessage 传入（不依赖是否超阈值，必记录）。
    ///     采集异常一律吞掉，绝不影响业务 SQL 执行。
    /// </summary>
    public void Add(string sql, SugarParameter[] parameters, long elapsedMs, string errorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;

        try
        {
            lock (_gate)
            {
                if (_closed) return;

                var normalizedElapsed = Math.Max(0, elapsedMs);
                _count++;
                _elapsedMs += normalizedElapsed;

                var isSlow = normalizedElapsed >= _slowThresholdMs;
                if (isSlow) _slowCount++;

                var hasError = !string.IsNullOrWhiteSpace(errorMessage);

                // 默认只关心慢 SQL 与失败 SQL；RecordAll 打开时全量记录
                if (!_recordAll && !isSlow && !hasError) return;

                if (_items.Count >= _maxItems)
                {
                    _droppedCount++;
                    return;
                }

                _items.Add(new AuditSqlCaptureItem
                {
                    SqlText = AuditLogSanitizer.SanitizeText(sql, _maxSqlLength),
                    Parameters = FormatParameters(parameters, _maxParametersLength),
                    ElapsedMs = normalizedElapsed,
                    IsSlow = isSlow,
                    ErrorMessage = hasError
                        ? AuditLogSanitizer.SanitizeText(errorMessage, MaxErrorLength)
                        : null
                });
            }
        }
        catch
        {
            // 采集失败不影响业务执行
        }
    }

    /// <summary>
    ///     关闭采集并生成快照：此后到达的 SQL（例如请求结束后仍在运行的异步任务）不会再被归属。
    /// </summary>
    public AuditSqlSnapshot Close()
    {
        lock (_gate)
        {
            _closed = true;

            if (_droppedCount > 0)
                _items.Add(new AuditSqlCaptureItem
                {
                    SqlText = $"...(已截断：另有 {_droppedCount} 条 SQL 明细未记录)",
                    ElapsedMs = 0,
                    IsSlow = false
                });

            return new AuditSqlSnapshot(_items.ToArray(), _count, _elapsedMs, _slowCount);
        }
    }

    private static string FormatParameters(SugarParameter[] parameters, int maxLength)
    {
        if (parameters is null || parameters.Length == 0) return null;

        var builder = new StringBuilder();
        foreach (var parameter in parameters)
        {
            if (parameter is null) continue;
            if (builder.Length > 0) builder.Append(", ");

            var name = string.IsNullOrWhiteSpace(parameter.ParameterName) ? "?" : parameter.ParameterName;
            builder.Append(name).Append('=');

            // 敏感参数按参数名直接掩码，避免 newPassword / accessToken 之类的变体漏过正则
            if (AuditLogSanitizer.IsSensitiveName(name))
                builder.Append(AuditLogSanitizer.Mask);
            else
                builder.Append(FormatValue(parameter.Value));
        }

        // 兜底再走一次脱敏，覆盖参数值内部出现的 password=xxx 等文本
        return AuditLogSanitizer.SanitizeText(builder.ToString(), maxLength);
    }

    private static string FormatValue(object value)
    {
        return value switch
        {
            null => "NULL",
            DBNull => "NULL",
            string text => "\"" + text.Replace("\"", "\\\"") + "\"",
            DateTime dateTime => "'" + dateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "'",
            DateTimeOffset dateTimeOffset => "'" +
                                             dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                                             "'",
            byte[] bytes => "0x" + Convert.ToHexString(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }
}
