using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Furion;
using Furion.JsonSerialization;
using Furion.Logging;
using Furion.Schedule;
using LogicNet.Core;
using LogicNet.Core.Entity;
using LogicNet.Core.Security;
using LogicNet.Web.Core.Job;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Yitter.IdGenerator;

namespace LogicNet.Web.Core;

public class Startup : AppStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // JWT 签名密钥必须来自环境变量/用户机密（仓库内仅为占位），未配置或过短时 fail-fast 并给出配置指引
        var issuerSigningKey = App.Configuration["JWTSettings:IssuerSigningKey"];
        if (string.IsNullOrWhiteSpace(issuerSigningKey) || issuerSigningKey.Length < 16)
            throw new InvalidOperationException(
                "未配置 JWT 签名密钥（JWTSettings:IssuerSigningKey）或长度不足 16 个字符。" +
                "请通过环境变量 JWTSettings__IssuerSigningKey（双下划线）或 dotnet user-secrets 配置；" +
                "生成强随机密钥示例：openssl rand -base64 48。详见 LogicNet.Application/applicationsettings.json 注释。");

        services.AddConsoleFormatter(options =>
        {
            // 每条控制台日志携带请求 ID（Activity.Id / TraceIdentifier），实现链路贯穿
            options.WithTraceId = true;
        });
        services.AddJwt<JwtHandler>(enableGlobalAuthorize: true);
        // 供 JWTEncryption.Exchange 刷新令牌防重放黑名单使用（进程内，单实例有效）
        services.AddDistributedMemoryCache();
        services.AddCorsAccessor();
        services.AddControllers().AddJsonOptions(item =>
            {
                item.JsonSerializerOptions.AllowTrailingCommas = true;
                // item.JsonSerializerOptions.Converters.Add(new SystemTextJsonStringToEnumJsonConverter());
                // item.JsonSerializerOptions.Converters.Add(new SystemTextJsonStringToNullJsonConverter());
                // item.JsonSerializerOptions.Converters.Add(new SystemTextJsonStringToJsonConverter());
                item.JsonSerializerOptions.Converters.AddDateTimeTypeConverters("yyyy-MM-dd");
                item.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                // item.JsonSerializerOptions.Converters.Add(new SystemTextJsonStringToBoolJsonConverter());
                // item.JsonSerializerOptions.Converters.Add(new SystemTextJsonStringToPriceJsonConverter());
                item.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: true));
                item.JsonSerializerOptions.Converters.Add(new SystemTextJsonDateTimeOffsetJsonConverter());
                item.JsonSerializerOptions.PropertyNamingPolicy = null;
                item.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            }).AddInjectWithUnifyResult<TemplateRestfulResultProvider>(item =>
            {
                item.ConfigureSwaggerGen(gen => { gen.CustomSchemaIds(s => s.FullName?.Replace("+", ".")); });
            })
            .AddInjectWithUnifyResult();

        YitIdHelper.SetIdGenerator(new IdGeneratorOptions()
        {
            WorkerIdBitLength = 10,
            SeqBitLength = 6,
            BaseTime = new DateTime(2025, 5, 4),
        });

        services.AddCaptcha(App.Configuration);

        // 匿名/高风险接口限流（框架原生 RateLimiting，按客户端 IP 固定窗口，规则见 HighRiskEndpointRateLimit）
        services.AddRateLimiter(options =>
        {
            // 限流拒绝时返回规范 429 统一结果，而非 500/空响应
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                await context.HttpContext.Response.WriteAsJsonAsync(new TemplateRestfulResult<object>
                {
                    Succeeded = false,
                    Code = StatusCodes.Status429TooManyRequests,
                    Errors = "请求过于频繁，请稍后重试",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }, cancellationToken);
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var ruleIndex = HighRiskEndpointRateLimit.Match(httpContext.Request.Path);
                if (ruleIndex < 0)
                    return RateLimitPartition.GetNoLimiter("__no_limit__");
                var rule = HighRiskEndpointRateLimit.Rules[ruleIndex];
                var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter($"{rule.PathSegment}:{clientIp}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rule.PermitLimit,
                        Window = rule.Window,
                        QueueLimit = 0
                    });
            });
        });

        // 注册 Mapster 对象映射，扫描 IRegister 映射配置（如 UserInfo/Mapper）
        services.AddObjectMapper();

        services.AddSchedule(item => { item.AddJob<DeleteDirJob>(TriggerBuilder.Period(1000)); });

        // 例子一：根据日志级别输出
        services.AddFileLogging("Logs/application-{0:yyyy}-{0:MM}-{0:dd}.log",
            options =>
            {
                options.FileNameRule = item => string.Format(item, DateTime.UtcNow);
                options.FileSizeLimitBytes = 1024 * 1024 * 30;
                options.WriteFilter = logMsg => logMsg.LogLevel == LogLevel.Information;
                // 每条文件日志携带请求 ID，与响应头 x-request-id 一致（日志文件位置约定不变）
                options.WithTraceId = true;
            });

        services.AddFileLogging("Logs/Error-{0:yyyy}-{0:MM}-{0:dd}.log",
            options =>
            {
                options.FileSizeLimitBytes = 1024 * 1024 * 30;
                options.FileNameRule = item => string.Format(item, DateTime.UtcNow);
                options.WriteFilter = logMsg => logMsg.LogLevel == LogLevel.Error;
                options.WithTraceId = true;
            });
        //注册SqlSugar
        services.AddScoped<ISqlSugarClient>(s =>
        {
            var sqlSugar = new SqlSugarScope(App.GetConfig<List<ConnectionConfig>>("ConnectionConfigs"),
                db =>
                {
                    //单例参数配置，所有上下文生效
                    // 软删除全局过滤器：所有继承 BaseEntity（实现 IDeleteFilter）的实体查询自动排除已删除数据
                    db.QueryFilter.AddTableFilter<IDeleteFilter>(x => !x.IsDelete);
                    db.Aop.OnLogExecuting = (sql, pars) =>
                    {
                        //Sql 执行前
                    };
                    db.Aop.OnError = (exp) =>
                    {
                        //Sql 执行出错
                    };
                    db.Aop.DataExecuting = (oldValue, entityInfo) =>
                    {
                        // 声明名与登录颁发令牌的 JwtClaimNames.UserId 保持一致（此前读 "Id" 恒为 0）；
                        // 无 HttpContext 场景（如定时任务）App.User 为空，安全回退为 0
                        var userId = long.TryParse(App.User?.FindFirstValue(JwtClaimNames.UserId), out var parsedUserId)
                            ? parsedUserId
                            : 0;
                        switch (entityInfo.PropertyName)
                        {
                            case nameof(UserInfo.CreateTime):
                            {
                                if (entityInfo.OperationType == DataFilterType.InsertByObject)
                                {
                                    entityInfo.SetValue(DateTime.Now);
                                }

                                break;
                            }
                            case nameof(UserInfo.UpdateTime):
                            {
                                entityInfo.SetValue(DateTime.Now);
                                break;
                            }

                            case nameof(UserInfo.CreateUserId):
                            {
                                if (entityInfo.OperationType == DataFilterType.InsertByObject)
                                {
                                    entityInfo.SetValue(userId);
                                }

                                break;
                            }
                            case nameof(UserInfo.UpdateUserId):
                            {
                                entityInfo.SetValue(userId);
                                break;
                            }
                        }
                    };
                });
            return sqlSugar;
        });

        services.AddScoped(typeof(Repository<>));

        SnowFlakeSingle.WorkId = 1;
        StaticConfig.CustomSnowFlakeFunc = YitIdHelper.NextId;

        services.AddSignalR();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        // 请求 ID 贯穿：响应头回写 x-request-id（与日志 WithTraceId 输出同源），便于客户端排障关联
        app.Use(async (context, next) =>
        {
            context.Response.Headers["x-request-id"] = Activity.Current?.Id ?? context.TraceIdentifier;
            await next();
        });

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseHttpsRedirection();

        app.UseStaticFiles();
        app.UseRouting();

        app.UseCorsAccessor();

        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseInject(string.Empty);

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapHubs();
            endpoints.MapControllers();
        });
    }
}