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
using Furion.DataEncryption;
using Furion.JsonSerialization;
using Furion.Logging;
using Furion.Schedule;
using LogicNet.Core;
using LogicNet.Core.Entity;
using LogicNet.Core.Security;
using LogicNet.Web.Core.Audit;
using LogicNet.Web.Core.Id;
using LogicNet.Web.Core.Job;
using LogicNet.Web.Core.Json;
using Microsoft.AspNetCore.Authorization;
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
        // JWT 签名密钥必须来自机密配置（仓库内不存放），未配置或过短时 fail-fast 并给出配置指引。
        // 注意：Furion 在 Serve.Run 宿主下捕获的 App.Configuration 不含进程环境变量（实测），
        // 容器/本地统一通过 secrets.json（Furion 自动扫描输出目录 *.json）注入，见 DEPLOY.md。
        var issuerSigningKey = App.Configuration["JWTSettings:IssuerSigningKey"];
        if (string.IsNullOrWhiteSpace(issuerSigningKey) || issuerSigningKey.Length < 16)
            throw new InvalidOperationException(
                "未配置 JWT 签名密钥（JWTSettings:IssuerSigningKey）或长度不足 16 个字符。" +
                "请放置 secrets.json（模板见 secrets.json.example，容器挂载到 /app/secrets.json）；" +
                "生成强随机密钥示例：openssl rand -base64 48。详见 DEPLOY.md。");

        services.AddConsoleFormatter(options =>
        {
            // 每条控制台日志携带请求 ID（Activity.Id / TraceIdentifier），实现链路贯穿
            options.WithTraceId = true;
        });
        services.AddJwt<JwtHandler>(enableGlobalAuthorize: true);
        // 菜单动态加载按当前用户取 JWT 声明（服务层注入 IHttpContextAccessor，避免直接访问静态 App 上下文）
        services.AddHttpContextAccessor();
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
                // long（含雪花 Id）统一输出为字符串，避免超过 JS Number.MAX_SAFE_INTEGER 后前端丢精度
                item.JsonSerializerOptions.Converters.Add(new LongToStringJsonConverter());
                item.JsonSerializerOptions.PropertyNamingPolicy = null;
                item.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            }).AddInjectWithUnifyResult<TemplateRestfulResultProvider>(item =>
            {
                item.ConfigureSwaggerGen(gen => { gen.CustomSchemaIds(s => s.FullName?.Replace("+", ".")); });
            })
            .AddInjectWithUnifyResult();

        // 雪花 Id：WorkerId 必须每实例唯一且重启稳定。优先取环境变量（多副本按容器注入），
        // 其次配置文件 IdGenerator:WorkerId；都未配置时按主机名稳定推导，避免所有实例共用 0 号。
        var workerIdSetting = Environment.GetEnvironmentVariable("IdGenerator__WorkerId");
        var workerIdSource = "环境变量 IdGenerator__WorkerId";
        if (string.IsNullOrWhiteSpace(workerIdSetting))
        {
            workerIdSetting = App.Configuration["IdGenerator:WorkerId"];
            workerIdSource = "配置 IdGenerator:WorkerId";
        }

        if (string.IsNullOrWhiteSpace(workerIdSetting))
        {
            workerIdSetting = null;
            workerIdSource = $"主机名 {Environment.MachineName} 推导";
        }

        var workerId = IdGeneratorSetup.ResolveWorkerId(workerIdSetting, Environment.MachineName);
        IdGeneratorSetup.Configure(workerId);
        // SqlSugar 的雪花 Id 入口统一委托给 Yitter，业务插入无需手工赋值
        StaticConfig.CustomSnowFlakeFunc = YitIdHelper.NextId;
        Log.Information($"雪花 Id 初始化：WorkerId={workerId}（来源：{workerIdSource}），" +
                        $"BaseTime={IdGeneratorSetup.BaseTime:yyyy-MM-dd}，" +
                        $"WorkerIdBitLength={IdGeneratorSetup.WorkerIdBitLength}，" +
                        $"SeqBitLength={IdGeneratorSetup.SeqBitLength}");

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

        // 临时文件清理：每分钟执行一次即可（10 分钟过期阈值不受触发频率影响），避免每秒扫盘的 IO 开销
        services.AddSchedule(item => { item.AddJob<DeleteDirJob>(TriggerBuilder.Period(60_000)); });

        // 操作审计：配置可覆盖；请求线程只入队，后台服务批量落库，避免阻塞响应
        services.Configure<AuditLogOptions>(App.Configuration.GetSection("AuditLog"));
        services.AddSingleton<AuditLogQueue>();
        services.AddHostedService<AuditLogWriterHostedService>();
        // 每日清理过期审计日志（独立于 60 秒周期的临时文件清理任务）
        services.AddHostedService<AuditLogCleanupHostedService>();

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

        services.AddSignalR();

        // 健康检查：/health 供容器探活，包含数据库连通性
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
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

        // 操作审计：放在认证之后、授权/吊销检查之前。
        // 认证后 User 已填充，能记录真实用户；授权拒绝（401/403）与吊销令牌的 401 也会经过本中间件，
        // 保证未认证请求同样留痕。
        app.UseMiddleware<AuditLogMiddleware>();

        // 已登出/吊销的访问令牌按未认证处理：授权失败默认返回 403，前端无法据此判断需要重新登录，
        // 这里在进入授权中间件前直接返回 401（匿名端点不处理）。
        app.Use(async (context, next) =>
        {
            if (context is DefaultHttpContext httpContext
                && httpContext.User.Identity?.IsAuthenticated == true
                && httpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() == null
                && TokenRevocationStore.IsRevoked(JWTEncryption.GetJwtBearerToken(httpContext)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        });

        app.UseAuthorization();

        app.UseInject(string.Empty);

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapHubs();
            endpoints.MapControllers();
            // 匿名健康检查端点（非 MVC 端点，不受全局授权影响；统一返回 JSON 明细）
            endpoints.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                ResponseWriter = async (context, report) =>
                {
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        status = report.Status.ToString(),
                        checks = report.Entries.ToDictionary(
                            item => item.Key,
                            item => new { status = item.Value.Status.ToString(), item.Value.Description })
                    });
                }
            });
        });

        // 数据库初始化（CodeFirst 建表 + 角色种子，幂等）：失败仅记录错误日志不阻断启动，
        // 保持"无库也可启动"的既有行为，错误详情见 Logs/Error-*.log
        try
        {
            using var scope = app.ApplicationServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            DatabaseInitializer.Initialize(db);
        }
        catch (Exception ex)
        {
            Log.Error("数据库初始化失败，请检查连接串与账号权限（表结构/种子数据未就绪）。", ex);
        }
    }
}