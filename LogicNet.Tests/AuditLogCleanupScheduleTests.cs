using LogicNet.Web.Core.Audit;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     每日清理触发时间：按本地时间计算到下一次执行时刻，重启不会每天多跑
/// </summary>
public class AuditLogCleanupScheduleTests
{
    [Fact]
    public void 未到执行时刻时等待当天剩余时间()
    {
        var now = new DateTime(2026, 11, 4, 1, 0, 0);

        var delay = AuditLogCleanupHostedService.ComputeDelayUntilNextRun(now, 3);

        Assert.Equal(TimeSpan.FromHours(2), delay);
    }

    [Fact]
    public void 已过执行时刻时顺延到次日()
    {
        var now = new DateTime(2026, 11, 4, 23, 0, 0);

        var delay = AuditLogCleanupHostedService.ComputeDelayUntilNextRun(now, 3);

        Assert.Equal(TimeSpan.FromHours(4), delay);
    }

    [Fact]
    public void 恰好到达执行时刻时顺延到次日()
    {
        var now = new DateTime(2026, 11, 4, 3, 0, 0);

        var delay = AuditLogCleanupHostedService.ComputeDelayUntilNextRun(now, 3);

        Assert.Equal(TimeSpan.FromDays(1), delay);
    }
}
