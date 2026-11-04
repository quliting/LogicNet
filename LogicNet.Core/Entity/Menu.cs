using SqlSugar;

namespace LogicNet.Core.Entity;

/// <summary>
///     菜单类型：目录不承载页面，菜单对应前端路由，按钮仅承载权限标识
/// </summary>
public enum MenuType
{
    Directory = 1,
    Menu = 2,
    Button = 3
}

/// <summary>
///     菜单/路由/按钮权限定义。ParentId 为 0 表示顶级；软删除与审计字段来自 BaseEntity。
/// </summary>
/// <remarks>
///     Path 在同一上级下唯一：服务层先做可读校验，数据库唯一索引兜底并发写入。
///     注意软删除行仍占用唯一索引（与 UserInfo 的历史约定一致），删除后同路径需换路径或物理清理。
/// </remarks>
[SugarIndex("IX_Menu_ParentId", nameof(ParentId), OrderByType.Asc)]
[SugarIndex("IX_Menu_Sort", nameof(Sort), OrderByType.Asc)]
[SugarIndex("UX_Menu_Parent_Path", nameof(ParentId), OrderByType.Asc, nameof(Path), OrderByType.Asc, true)]
public class Menu : BaseEntity
{
    /// <summary>
    ///     上级菜单 Id，顶级为 0
    /// </summary>
    public long ParentId { get; set; }

    /// <summary>
    ///     显示名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     前端路由路径
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    ///     前端组件标识（目录通常为空）
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public string Component { get; set; }

    /// <summary>
    ///     图标标识
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public string Icon { get; set; }

    /// <summary>
    ///     同级排序，升序
    /// </summary>
    public int Sort { get; set; }

    /// <summary>
    ///     菜单类型
    /// </summary>
    public MenuType Type { get; set; } = MenuType.Menu;

    /// <summary>
    ///     权限标识（按钮级权限使用）
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public string PermissionCode { get; set; }

    /// <summary>
    ///     是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    ///     是否在侧栏导航中显示
    /// </summary>
    public bool IsVisible { get; set; } = true;
}
