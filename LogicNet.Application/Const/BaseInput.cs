namespace LogicNet.Application;

public class BaseInput
{
    private int _pageIndex = 1;
    private int _pageSize = 10;

    /// <summary>
    ///     每页条数：默认 10，允许调用方配置，钳制在 1-100 防止分页被无限放大拖垮查询
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, 100);
    }

    /// <summary>
    ///     当前页码
    /// </summary>
    public int PageIndex
    {
        get => _pageIndex;
        set => _pageIndex = Math.Max(1, value);
    }
}