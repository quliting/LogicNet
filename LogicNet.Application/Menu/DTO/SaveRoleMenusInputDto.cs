namespace LogicNet.Application.Menu.DTO;

/// <summary>
///     角色-菜单批量保存入参：MenuIds 为角色最终勾选的菜单集合（未勾选的会取消绑定）。
/// </summary>
public class SaveRoleMenusInputDto
{
    /// <summary>
    ///     角色 Id
    /// </summary>
    public long RoleId { get; set; }

    /// <summary>
    ///     最终勾选的菜单 Id 集合；空集合表示清空该角色的菜单授权
    /// </summary>
    public List<long> MenuIds { get; set; } = new();
}
