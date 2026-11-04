using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogicNet.Application.Audit.DTO;

/// <summary>
///     审计时间输出为精确到秒的完整时间：全局 JSON 配置只保留日期，无法用于操作追溯。
/// </summary>
public sealed class AuditLogDateTimeConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd HH:mm:ss";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return DateTime.Parse(reader.GetString() ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(Format));
    }
}
