using Masuit.Tools.Models;

namespace LogicNet.Application;

public static class LogicBaseExtension
{
    public static PagedList<T> PagedList<T>(this List<T> list, int pageIndex, int pageSize, int total)
    {
        return new PagedList<T>(list, pageIndex, pageSize, total);
    }
}