namespace LogicNet.Application;

public class BaseInput
{
    private int _pageIndex = 1;

    public int PageSize { get; } = 10;

    /// <summary>
    ///     当前页码
    /// </summary>
    public int PageIndex
    {
        get => _pageIndex;
        set => _pageIndex = Math.Max(1, value);
    }
}