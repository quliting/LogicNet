using System.Reflection;
using System.Security.Claims;
using LogicNet.Application.Menu.Services;
using LogicNet.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     菜单接口鉴权面：管理方法限定 Admin；未登录依赖全局认证返回 401；普通用户角色不足返回 403
/// </summary>
public class MenuAuthSurfaceTests
{
    private static readonly string[] AdminMethods =
    {
        nameof(MenuService.CreateMenuAsync),
        nameof(MenuService.UpdateMenuAsync),
        nameof(MenuService.DeleteMenuAsync),
        nameof(MenuService.GetMenuDetailAsync),
        nameof(MenuService.GetMenuListAsync),
        nameof(MenuService.GetMenuTreeAsync)
    };

    [Fact]
    public void 管理方法全部限定Admin角色()
    {
        var type = typeof(MenuService);
        foreach (var methodName in AdminMethods)
        {
            var method = type.GetMethod(methodName);
            Assert.NotNull(method);

            var attribute = method.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal("Admin", attribute.Roles);

            // 显式匿名会绕过全局认证，管理接口不允许
            Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }

    [Fact]
    public void GetMyMenus不匿名且不额外限制角色()
    {
        var method = typeof(MenuService).GetMethod(nameof(MenuService.GetMyMenusAsync));
        Assert.NotNull(method);
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("User", false)]
    public async Task 角色要求只放行Admin(string role, bool expected)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(JwtClaimNames.UserId, "1001"), new Claim(ClaimTypes.Role, role) }, "TestAuth"));
        var requirement = new RolesAuthorizationRequirement(new[] { "Admin" });
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, null);

        await requirement.HandleAsync(context);

        Assert.Equal(expected, context.HasSucceeded);
    }

    [Fact]
    public async Task 匿名主体无法通过登录校验()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var requirement = new DenyAnonymousAuthorizationRequirement();
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, null);

        await requirement.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
