using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Furion;
using Furion.JsonSerialization;
using Furion.Schedule;
using LogicNet.Web.Core.Job;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yitter.IdGenerator;
using ZstdSharp.Unsafe;

namespace LogicNet.Web.Core;

public class Startup : AppStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddConsoleFormatter();
        services.AddJwt<JwtHandler>();

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