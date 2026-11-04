using LogicNet.Core.Entity;

namespace LogicNet.Application.Menu.DTO;

/// <summary>
///     菜单平铺输出（详情/列表）
/// </summary>
public class MenuOutputDto
{
    public long Id { get; set; }

    public long ParentId { get; set; }

    public string Name { get; set; }

    public string Path { get; set; }

    public string Component { get; set; }

    public string Icon { get; set; }

    public int Sort { get; set; }

    public MenuType Type { get; set; }

    public string PermissionCode { get; set; }

    public bool IsEnabled { get; set; }

    public bool IsVisible { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime UpdateTime { get; set; }
}
