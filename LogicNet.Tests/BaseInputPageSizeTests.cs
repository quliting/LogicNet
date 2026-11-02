using LogicNet.Application;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     分页参数钳制（P2-1）：PageSize 默认 10，可配置并限制在 1-100
/// </summary>
public class BaseInputPageSizeTests
{
    [Fact]
    public void 默认值保持不变()
    {
        var input = new BaseInput();
        Assert.Equal(10, input.PageSize);
        Assert.Equal(1, input.PageIndex);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(101, 100)]
    [InlineData(int.MaxValue, 100)]
    public void PageSize钳制在合理区间(int input, int expected)
    {
        Assert.Equal(expected, new BaseInput { PageSize = input }.PageSize);
    }
}
