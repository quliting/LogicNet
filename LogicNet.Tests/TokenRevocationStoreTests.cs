using LogicNet.Core.Security;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     令牌吊销存储：登出/吊销机制核心组件（进程内实现，多实例不互通为已知取舍）
/// </summary>
public class TokenRevocationStoreTests
{
    [Fact]
    public void 吊销后判定为已吊销()
    {
        TokenRevocationStore.Revoke("test-token-a", DateTime.UtcNow.AddMinutes(5));
        Assert.True(TokenRevocationStore.IsRevoked("test-token-a"));
        Assert.False(TokenRevocationStore.IsRevoked("test-token-b"));
    }

    [Fact]
    public void 已过期的吊销记录自动失效()
    {
        TokenRevocationStore.Revoke("test-token-expired", DateTime.UtcNow.AddMinutes(-1));
        Assert.False(TokenRevocationStore.IsRevoked("test-token-expired"));
    }
}
