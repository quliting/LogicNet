using System;
using System.Globalization;
using System.Text;
using Yitter.IdGenerator;

namespace LogicNet.Web.Core.Id;

/// <summary>
///     雪花 Id 的唯一初始化入口。
///     WorkerId 必须“每实例唯一且重启稳定”：优先读取显式配置/环境变量，
///     未配置时按主机名做稳定哈希推导（不要用 string.GetHashCode，进程间随机化）。
///     BaseTime 固定，重建容器或重启进程后时间戳基准不漂移。
/// </summary>
public static class IdGeneratorSetup
{
    /// <summary>WorkerId 位数（与既有数据一致，不可随意改动，否则新旧 Id 可能重叠）。</summary>
    public const int WorkerIdBitLength = 10;

    /// <summary>序列号位数：64 个/毫秒，超出后由 Yitter 漂移算法顺延毫秒，不会重复。</summary>
    public const int SeqBitLength = 6;

    /// <summary>可用 WorkerId 个数：1024。</summary>
    public const int WorkerIdCount = 1 << WorkerIdBitLength;

    /// <summary>WorkerId 合法上限：1023。</summary>
    public const int MaxWorkerId = WorkerIdCount - 1;

    /// <summary>固定时间基准（沿用 2025-05-04，不可修改）。</summary>
    public static readonly DateTime BaseTime = new(2025, 5, 4);

    /// <summary>
    ///     解析 WorkerId：显式值优先并做范围校验，未配置时按主机名推导。
    ///     越界/非整数直接抛出，避免带病启动（多副本时重复 WorkerId 会造成主键冲突）。
    /// </summary>
    public static int ResolveWorkerId(string configuredValue, string machineName)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            var text = configuredValue.Trim();
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                throw new InvalidOperationException(
                    $"雪花 Id 的 WorkerId 配置不是整数：'{text}'，合法范围 0-{MaxWorkerId}。");

            if (parsed < 0 || parsed > MaxWorkerId)
                throw new InvalidOperationException(
                    $"雪花 Id 的 WorkerId={parsed} 越界，合法范围 0-{MaxWorkerId}。" +
                    "多副本部署请为每个实例配置不同值。");

            return parsed;
        }

        return DeriveWorkerId(machineName);
    }

    /// <summary>按主机名做 FNV-1a 稳定哈希，映射到 0-1023。</summary>
    public static int DeriveWorkerId(string machineName)
    {
        var name = string.IsNullOrWhiteSpace(machineName) ? "logicnet" : machineName;
        var bytes = Encoding.UTF8.GetBytes($"LogicNet:{name}");
        var hash = 2166136261u;
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= 16777619u;
        }

        return (int)(hash % WorkerIdCount);
    }

    /// <summary>初始化 Yitter 全局生成器（应用启动时调用一次）。</summary>
    public static void Configure(int workerId)
    {
        if (workerId < 0 || workerId > MaxWorkerId)
            throw new InvalidOperationException(
                $"雪花 Id 的 WorkerId={workerId} 越界，合法范围 0-{MaxWorkerId}。");

        YitIdHelper.SetIdGenerator(new IdGeneratorOptions
        {
            WorkerId = (ushort)workerId,
            WorkerIdBitLength = WorkerIdBitLength,
            SeqBitLength = SeqBitLength,
            BaseTime = BaseTime
        });
    }

    /// <summary>从生成的 Id 中还原 WorkerId（用于自检与测试）。</summary>
    public static int DecodeWorkerId(long id) => (int)((id >> SeqBitLength) & MaxWorkerId);
}
