namespace LogicNet.Core.Entity;

/// <summary>
/// 软删除标记接口。实现该接口的实体在查询时会被全局过滤器自动追加 IsDelete = false 条件
/// （过滤器注册见 LogicNet.Web.Core/Startup.cs 的 SqlSugar 配置）。
/// </summary>
public interface IDeleteFilter
{
    bool IsDelete { get; set; }
}
