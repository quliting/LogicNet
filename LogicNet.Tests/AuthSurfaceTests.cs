using System.Reflection;
using System.Security.Claims;
using LogicNet.Application.Captcha.Service;
using LogicNet.Application.UserInfo.Services;
using LogicNet.Core.Security;
using LogicNet.Web.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     鉴权面与 JwtHandler 管道：匿名白名单不扩大；缺声明/吊销/软删/无用户均拒绝（P0-1/P0-7 行为回归）
/// </summary>
public class AuthSurfaceTests
{
    [Fact]
    public void 匿名白名单仅限必要入口()
    {
        var service = typeof(UserInfoService);
        Assert.NotNull(service.GetMethod("LoginAsync")!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(service.GetMethod("RegisterAsync")!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(service.GetMethod("RefreshAsync")!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(typeof(CaptchaService).GetCustomAttribute<AllowAnonymousAttribute>());

        // 以下不得匿名
        Assert.Null(service.GetMethod("GetUserListAsync")!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(service.GetMethod("LogoutAsync")!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(typeof(LogicNet.Application.Test.Service.TestService).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    private static (JwtHandler handler, AuthorizationHandlerContext context, DefaultHttpContext http) BuildPipeline(
        Action<IServiceCollection> configureServices = null, params Claim[] claims)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var context = new AuthorizationHandlerContext(
            new[] { new DenyAnonymousAuthorizationRequirement() }, http.User, http);
        return (new JwtHandler(), context, http);
    }

    [Fact]
    public async Task 缺少UserId声明直接拒绝()
    {
        var (handler, context, http) = BuildPipeline(claims: new Claim(JwtClaimNames.Account, "abc"));
        Assert.False(await handler.PipelineAsync(context, http));
    }

    [Fact]
    public async Task 非法UserId声明直接拒绝()
    {
        var (handler, context, http) = BuildPipeline(claims: new Claim(JwtClaimNames.UserId, "not-a-number"));
        Assert.False(await handler.PipelineAsync(context, http));
    }

    [Fact]
    public async Task 已吊销令牌直接拒绝()
    {
        TokenRevocationStore.Revoke("revoked-token-p1", DateTime.UtcNow.AddMinutes(5));
        var (handler, context, http) = BuildPipeline(claims: new Claim(JwtClaimNames.UserId, "1001"));
        http.Request.Headers.Authorization = "Bearer revoked-token-p1";
        Assert.False(await handler.PipelineAsync(context, http));
    }

    [Fact]
    public async Task 用户存在且未软删除才放行()
    {
        using var db = TestDb.Create();
        TestDb.SeedUser(db, 1001, "normal_user");
        TestDb.SeedUser(db, 1002, "deleted_user", isDelete: true);

        var (handler, context, http) = BuildPipeline(
            s => s.AddSingleton<ISqlSugarClient>(db),
            new Claim(JwtClaimNames.UserId, "1001"));
        Assert.True(await handler.PipelineAsync(context, http));

        var (handler2, context2, http2) = BuildPipeline(
            s => s.AddSingleton<ISqlSugarClient>(db),
            new Claim(JwtClaimNames.UserId, "1002"));
        Assert.False(await handler2.PipelineAsync(context2, http2));

        var (handler3, context3, http3) = BuildPipeline(
            s => s.AddSingleton<ISqlSugarClient>(db),
            new Claim(JwtClaimNames.UserId, "9999"));
        Assert.False(await handler3.PipelineAsync(context3, http3));
    }
}
