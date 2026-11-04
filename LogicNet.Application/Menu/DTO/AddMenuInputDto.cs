using System.ComponentModel.DataAnnotations;
using LogicNet.Core.Entity;

namespace LogicNet.Application.Menu.DTO;

/// <summary>
///     新增菜单入参
/// </summary>
public class AddMenuInputDto
{
    /// <summary>
    ///     上级菜单 Id，顶级为 0
    /// </summary>
    public long ParentId { get; set; }

    [Required(ErrorMessage = "菜单名称不能为空")]
    public string Name { get; set; }

    [Required(ErrorMessage = "路由路径不能为空")]
    public string Path { get; set; }

    /// <summary>
    ///     前端组件标识（目录通常为空）
    /// </summary>
    public string Component { get; set; }

    public string Icon { get; set; }

    public int Sort { get; set; }

    [EnumDataType(typeof(MenuType), ErrorMessage = "菜单类型错误")]
    public MenuType Type { get; set; } = MenuType.Menu;

    /// <summary>
    ///     按钮级权限标识
    /// </summary>
    public string PermissionCode { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsVisible { get; set; } = true;
}
