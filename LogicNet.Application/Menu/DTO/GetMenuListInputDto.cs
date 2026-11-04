using LogicNet.Core.Entity;

namespace LogicNet.Application.Menu.DTO;

/// <summary>
///     菜单列表查询入参：名称模糊、类型与启用状态过滤，分页复用 BaseInput 的钳制规则
/// </summary>
public class GetMenuListInputDto : BaseInput
{
    public string Name { get; set; }

    public MenuType? Type { get; set; }

    public bool? IsEnabled { get; set; }
}
