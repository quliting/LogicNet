using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LogicNet.Web.Core.Id;
using Xunit;
using Yitter.IdGenerator;

namespace LogicNet.Tests;

/// <summary>
///     雪花 Id 配置：WorkerId 解析/越界、并发生成唯一性、单调性、重启后不回退。
///     这些用例会改写 YitIdHelper 全局状态，故集中在本类内串行执行。
/// </summary>
public class IdGeneratorSetupTests
{
    [Fact]
    public void WorkerId_显式配置在合法范围内_原样返回()
    {
        Assert.Equal(0, IdGeneratorSetup.ResolveWorkerId("0", "host-a"));
        Assert.Equal(1023, IdGeneratorSetup.ResolveWorkerId("1023", "host-a"));
        Assert.Equal(7, IdGeneratorSetup.ResolveWorkerId(" 7 ", "host-a"));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1024")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void WorkerId_越界或非整数_抛出可读异常(string configured)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => IdGeneratorSetup.ResolveWorkerId(configured, "host-a"));
        Assert.Contains("WorkerId", ex.Message);
    }

    [Fact]
    public void WorkerId_未配置时_按主机名稳定推导且在范围内()
    {
        var first = IdGeneratorSetup.ResolveWorkerId(null, "logicnet-app");
        var second = IdGeneratorSetup.ResolveWorkerId("", "logicnet-app");

        Assert.Equal(first, second);
        Assert.InRange(first, 0, IdGeneratorSetup.MaxWorkerId);
        // 不同主机名映射到不同 WorkerId（FNV-1a 确定性结果，避免多副本共用同一编号）
        Assert.NotEqual(first, IdGeneratorSetup.ResolveWorkerId(null, "another-host"));
    }

    [Fact]
    public void Configure_生成的Id能还原出WorkerId()
    {
        IdGeneratorSetup.Configure(7);
        var id = YitIdHelper.NextId();
        Assert.Equal(7, IdGeneratorSetup.DecodeWorkerId(id));
    }

    [Fact]
    public void 串行生成Id_严格单调递增()
    {
        IdGeneratorSetup.Configure(9);

        var previous = 0L;
        for (var i = 0; i < 20_000; i++)
        {
            var id = YitIdHelper.NextId();
            Assert.True(id > previous, $"第 {i} 个 Id 未递增：{id} <= {previous}");
            previous = id;
        }
    }

    [Fact]
    public void 并发生成Id_无重复()
    {
        IdGeneratorSetup.Configure(9);

        const int tasks = 8;
        const int perTask = 10_000;
        var ids = new ConcurrentBag<long>();

        Parallel.For(0, tasks, _ =>
        {
            for (var i = 0; i < perTask; i++) ids.Add(YitIdHelper.NextId());
        });

        Assert.Equal(tasks * perTask, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void 模拟重启_固定BaseTime与WorkerId下Id不回退()
    {
        IdGeneratorSetup.Configure(3);
        var beforeRestart = YitIdHelper.NextId();

        // 至少跨过一个毫秒，模拟进程重启后重新初始化生成器
        Thread.Sleep(20);
        IdGeneratorSetup.Configure(3);
        var afterRestart = YitIdHelper.NextId();

        Assert.True(afterRestart > beforeRestart, $"重启后 Id 回退：{afterRestart} <= {beforeRestart}");
        Assert.Equal(3, IdGeneratorSetup.DecodeWorkerId(afterRestart));
    }
}
