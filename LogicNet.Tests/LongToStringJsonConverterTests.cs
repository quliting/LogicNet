using System.Text.Json;
using LogicNet.Web.Core.Json;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     long 序列化：输出字符串，避免雪花 Id 超过 2^53 后前端丢精度；入参兼容数字与字符串。
/// </summary>
public class LongToStringJsonConverterTests
{
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new LongToStringJsonConverter());
        return options;
    }

    [Fact]
    public void 序列化_long输出为字符串()
    {
        var options = CreateOptions();
        var json = JsonSerializer.Serialize(new { Id = 3111855743303685L }, options);

        Assert.Equal("{\"Id\":\"3111855743303685\"}", json);
    }

    [Fact]
    public void 序列化_可空long为null时保持null()
    {
        var options = CreateOptions();
        var json = JsonSerializer.Serialize(new { UserId = (long?)null }, options);

        Assert.Equal("{\"UserId\":null}", json);
    }

    [Fact]
    public void 反序列化_兼容字符串与数字()
    {
        var options = CreateOptions();

        var fromString = JsonSerializer.Deserialize<IdHolder>("{\"Id\":\"3111855743303685\"}", options);
        var fromNumber = JsonSerializer.Deserialize<IdHolder>("{\"Id\":3111855743303685}", options);

        Assert.Equal(3111855743303685L, fromString.Id);
        Assert.Equal(fromString.Id, fromNumber.Id);
    }

    [Fact]
    public void 字符串形态_可无损往返()
    {
        var options = CreateOptions();
        const long id = 3111855743303685L;

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { Id = id }, options));
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("Id").ValueKind);
        Assert.Equal(id.ToString(), document.RootElement.GetProperty("Id").GetString());
    }

    private sealed class IdHolder
    {
        public long Id { get; set; }
    }
}
