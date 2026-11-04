using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using Furion.Logging;
using LogicNet.Core.Entity;
using Microsoft.Extensions.Options;

namespace LogicNet.Web.Core.Audit;

/// <summary>
///     审计日志写入队列：请求线程只做一次非阻塞 TryWrite，真正的数据库写入由后台服务完成。
///     队列满时丢弃新记录并记录 Warning，绝不阻塞响应。
/// </summary>
public sealed class AuditLogQueue
{
    private readonly Channel<AuditLog> _channel;

    public AuditLogQueue(IOptions<AuditLogOptions> options)
    {
        var capacity = Math.Clamp(options.Value.QueueCapacity, 100, 100000);
        _channel = Channel.CreateBounded<AuditLog>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    ///     非阻塞入队；队列满时返回 false（记录被丢弃，调用方无需处理）
    /// </summary>
    public bool Enqueue(AuditLog log)
    {
        if (_channel.Writer.TryWrite(log)) return true;

        Log.Warning($"审计日志队列已满，丢弃记录：{log.HttpMethod} {log.Path}");
        return false;
    }

    public IAsyncEnumerable<AuditLog> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    ///     尝试取出一条记录（后台批量写入时用于攒批，测试中用于断言采集结果）
    /// </summary>
    public bool TryDequeue(out AuditLog log)
    {
        return _channel.Reader.TryRead(out log);
    }
}
