using LogicNet.Core.Entity;

namespace LogicNet.Application.Menu.DTO;

/// <summary>
///     菜单树输出：children 嵌套，同级按 Sort 升序
/// </summary>
public class MenuTreeOutputDto
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

    public List<MenuTreeOutputDto> Children { get; set; } = new();
}
