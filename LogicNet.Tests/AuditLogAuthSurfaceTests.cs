using System.Reflection;
using LogicNet.Application.Audit.Services;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     审计接口鉴权面：查询、详情、清理全部限定 Admin；不允许匿名绕过
/// </summary>
public class AuditLogAuthSurfaceTests
{
    private static readonly string[] AdminMethods =
    {
        nameof(AuditLogService.GetAuditLogListAsync),
        nameof(AuditLogService.GetAuditLogDetailAsync),
        nameof(AuditLogService.CleanupAuditLogsAsync)
    };

    [Fact]
    public void 审计方法全部限定Admin角色()
    {
        var type = typeof(AuditLogService);

        foreach (var methodName in AdminMethods)
        {
            var method = type.GetMethod(methodName);
            Assert.NotNull(method);

            var attribute = method.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal("Admin", attribute.Roles);

            Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }
}
