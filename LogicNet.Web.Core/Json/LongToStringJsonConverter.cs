using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogicNet.Web.Core.Json;

/// <summary>
///     long 统一序列化为 JSON 字符串：雪花 Id 已接近 2^53（JS Number.MAX_SAFE_INTEGER），
///     继续按数字输出会在前端丢精度。反序列化兼容数字与字符串，旧客户端不受影响。
/// </summary>
public sealed class LongToStringJsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetInt64();
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text)) return 0;
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    return value;
                throw new JsonException($"无法把字符串 '{text}' 解析为 long。");
            case JsonTokenType.Null:
                return 0;
            default:
                throw new JsonException($"long 反序列化不支持 {reader.TokenType} 类型。");
        }
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
