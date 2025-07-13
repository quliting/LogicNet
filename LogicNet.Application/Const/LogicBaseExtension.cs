using Masuit.Tools.Models;

namespace LogicNet.Application;

public static class LogicBaseExtension
{
    public static async Task<PagedList<T>> PagedList<T>(this ISugarQueryable<T> list, int pageIndex, int pageSize)
    {
        RefAsync<int> total = 0;
        var result = await list.ToPageListAsync(pageIndex, pageSize, total);
        return new PagedList<T>(result, pageIndex, pageSize, total);
    }
}