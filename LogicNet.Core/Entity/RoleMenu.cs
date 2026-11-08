using SqlSugar;

namespace LogicNet.Core.Entity;

/// <summary>
///     角色-菜单关联：角色可见/可访问的菜单集合。
///     Admin 角色的全量权限由代码层兜底（见 MenuService.ApplyRoleFilter），本表为其它角色的授权依据。
/// </summary>
/// <remarks>
///     (RoleId, MenuId) 唯一：服务层批量保存前先查重，数据库唯一索引兜底并发写入。
///     注意软删除行仍占用唯一索引，重新勾选时按“恢复”处理而非重新插入（见 RoleMenuService）。
/// </remarks>
[SugarIndex("UX_RoleMenu_Role_Menu", nameof(RoleId), OrderByType.Asc, nameof(MenuId), OrderByType.Asc, true)]
[SugarIndex("IX_RoleMenu_MenuId", nameof(MenuId), OrderByType.Asc)]
public class RoleMenu : BaseEntity
{
    /// <summary>
    ///     角色 Id
    /// </summary>
    public long RoleId { get; set; }

    /// <summary>
    ///     菜单 Id
    /// </summary>
    public long MenuId { get; set; }
}
