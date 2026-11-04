using System.Net;
using System.Security.Claims;
using System.Text;
using LogicNet.Core.Entity;
using LogicNet.Core.Security;
using LogicNet.Web.Core.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     采集中间件：记录范围、跳过项、未认证请求、请求体可重复读取、异常与脱敏
/// </summary>
public class AuditLogMiddlewareTests
{
    private static AuditLogQueue CreateQueue(AuditLogOptions options)
    {
        return new AuditLogQueue(Options.Create(options));
    }

    private static AuditLogMiddleware CreateMiddleware(RequestDelegate next, AuditLogQueue queue,
        AuditLogOptions options)
    {
        return new AuditLogMiddleware(next, queue, Options.Create(options));
    }

    private static DefaultHttpContext CreateContext(string method, string path, string body = null,
        string contentType = "application/json", ClaimsPrincipal user = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");

        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = contentType;
        }

        if (user is not null) context.User = user;
        return context;
    }

    private static ClaimsPrincipal CreateUser(long userId, string userName, string role = "Admin")
    {
        return new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.UserId, userId.ToString()),
            new Claim(JwtClaimNames.Account, userName),
            new Claim(ClaimTypes.Role, role)
        }, "TestAuth"));
    }

    [Fact]
    public async Task 写操作被记录且请求体仍可被业务读取()
    {
        var options = new AuditLogOptions();
        var queue = CreateQueue(options);
        var originalBody = "{\"name\":\"菜单\",\"passWord\":\"S3cretPass!987\"}";
        string bodyReadByNext = null;

        var middleware = CreateMiddleware(async context =>
        {
            bodyReadByNext = await new StreamReader(context.Request.Body).ReadToEndAsync();
            context.Response.StatusCode = StatusCodes.Status200OK;
        }, queue, options);

        await middleware.InvokeAsync(CreateContext("POST", "/Api/Menu/CreateMenu", originalBody));

        Assert.Equal(originalBody, bodyReadByNext);

        Assert.True(queue.TryDequeue(out var log));
        Assert.Equal("POST", log.HttpMethod);
        Assert.Equal("/Api/Menu/CreateMenu", log.Path);
        Assert.Equal(200, log.StatusCode);
        Assert.True(log.Succeeded);
        Assert.Equal("10.0.0.5", log.ClientIp);
        Assert.DoesNotContain("S3cretPass!987", log.RequestBody);
        Assert.Contains("***", log.RequestBody);
    }

    [Fact]
    public async Task GET默认不记录_开启IncludeGet后记录()
    {
        var options = new AuditLogOptions();
        var queue = CreateQueue(options);
        var middleware = CreateMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }, queue, options);

        await middleware.InvokeAsync(CreateContext("GET", "/Api/Menu/GetMenuList"));
        Assert.False(queue.TryDequeue(out _));

        var includeGetOptions = new AuditLogOptions { IncludeGet = true };
        var includeGetQueue = CreateQueue(includeGetOptions);
        var includeGetMiddleware = CreateMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }, includeGetQueue, includeGetOptions);

        await includeGetMiddleware.InvokeAsync(CreateContext("GET", "/Api/Menu/GetMenuList"));
        Assert.True(includeGetQueue.TryDequeue(out var log));
        Assert.Equal("GET", log.HttpMethod);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/Api/AuditLog/GetAuditLogList")]
    [InlineData("/Api/AuditLog/CleanupAuditLogs")]
    [InlineData("/assets/app.js")]
    public async Task 跳过健康检查_审计接口自身与静态文件(string path)
    {
        var options = new AuditLogOptions();
        var queue = CreateQueue(options);
        var middleware = CreateMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }, queue, options);

        await middleware.InvokeAsync(CreateContext("POST", path, "{\"a\":1}"));

        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public async Task 未认证的401请求也会被记录()
    {
        var options = new AuditLogOptions();
        var queue = CreateQueue(options);
        var middleware = CreateMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }, queue, options);

        await middleware.InvokeAsync(CreateContext("POST", "/Api/Menu/CreateMenu", "{\"name\":\"x\"}"));

        Assert.True(queue.TryDequeue(out var log));
        Assert.Equal(401, log.StatusCode);
        Assert.False(log.Succeeded);
        Assert.Null(log.UserId);
        Assert.Equal("anonymous", log.UserName);
    }

    [Fact]
    public async Task 已认证请求记录用户与角色来源信息()
    {
        var options = new AuditLogOptions();
        var queue = CreateQueue(options);
        var middleware = CreateMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }, queue, options);

        await middleware.InvokeAsync(CreateContext("DELETE", "/Api/Menu/DeleteMenu?id=1",
            user: CreateUser(1001, "admin01")));

        Assert.True(queue.TryDequeue(out var log));
        Assert.Equal(1001, log.UserId);
        Assert.Equal("admin01", log.UserName);
    }

    [Fact]
    public async Task 下游异常会记录失败并原样抛出()
    {
        var options = new AuditLogOptions();
        var queue = CreateQueue(options);
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("boom password=S3cretPass!987"),
            queue, options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(CreateContext("POST", "/Api/Menu/CreateMenu", "{\"a\":1}")));

        Assert.True(queue.TryDequeue(out var log));
        Assert.False(log.Succeeded);
        Assert.DoesNotContain("S3cretPass!987", log.ExceptionMessage);
        Assert.Contains("***", log.ExceptionMessage);
    }
}
