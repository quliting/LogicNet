using System;
using System.Collections.Concurrent;

namespace LogicNet.Core.Security;

/// <summary>
///     令牌吊销存储（进程内实现）。多实例部署时需替换为分布式实现（如 Redis），否则吊销仅对单实例生效。
/// </summary>
public static class TokenRevocationStore
{
    private static readonly ConcurrentDictionary<string, DateTime> RevokedTokens = new();

    /// <summary>
    ///     吊销令牌，记录保留至令牌自身过期时间点
    /// </summary>
    /// <param name="token">完整令牌字符串</param>
    /// <param name="expireAtUtc">令牌过期时间（UTC）</param>
    public static void Revoke(string token, DateTime expireAtUtc)
    {
        Cleanup();
        RevokedTokens[token] = expireAtUtc;
    }

    /// <summary>
    ///     令牌是否已被吊销（已过期的吊销记录视为无效并顺手移除）
    /// </summary>
    public static bool IsRevoked(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        if (!RevokedTokens.TryGetValue(token, out var expireAtUtc)) return false;
        if (expireAtUtc > DateTime.UtcNow) return true;

        RevokedTokens.TryRemove(token, out _);
        return false;
    }

    // 惰性清理已过期的吊销记录，避免内存膨胀
    private static void Cleanup()
    {
        var now = DateTime.UtcNow;
        foreach (var item in RevokedTokens)
            if (item.Value <= now)
                RevokedTokens.TryRemove(item.Key, out _);
    }
}
