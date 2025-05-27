using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bing.Extensions;
using Furion;
using Furion.JsonSerialization;
using Furion.Logging;
using Furion.Schedule;
using LogicNet.Core;
using LogicNet.Core.Entity;
using LogicNet.Web.Core.Job;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
        services.AddConsoleFormatter();
        services.AddJwt<JwtHandler>(enableGlobalAuthorize: true);
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

        services.AddSchedule(item => { item.AddJob<DeleteDirJob>(TriggerBuilder.Period(1000)); });

        // 例子一：根据日志级别输出
        services.AddFileLogging("Logs/application-{0:yyyy}-{0:MM}-{0:dd}.log",
            options =>
            {
                options.FileNameRule = item => string.Format(item, DateTime.UtcNow);
                options.FileSizeLimitBytes = 1024 * 1024 * 30;
                options.WriteFilter = logMsg => logMsg.LogLevel == LogLevel.Information;
            });

        services.AddFileLogging("Logs/Error-{0:yyyy}-{0:MM}-{0:dd}.log",
            options =>
            {
                options.FileSizeLimitBytes = 1024 * 1024 * 30;
                options.FileNameRule = item => string.Format(item, DateTime.UtcNow);
                options.WriteFilter = logMsg => logMsg.LogLevel == LogLevel.Error;
            });
        //注册SqlSugar
        services.AddScoped<ISqlSugarClient>(s =>
        {
            var sqlSugar = new SqlSugarScope(App.GetConfig<List<ConnectionConfig>>("ConnectionConfigs"),
                db =>
                {
                    //单例参数配置，所有上下文生效
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
                        var userId = App.User.FindFirstValue("Id").ToLong();
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
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseHttpsRedirection();

        app.UseStaticFiles();
        app.UseRouting();

        app.UseCorsAccessor();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseInject(string.Empty);

        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
    }
}