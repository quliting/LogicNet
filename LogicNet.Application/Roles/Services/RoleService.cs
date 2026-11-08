using LogicNet.Application.Roles.DTO;
using Microsoft.AspNetCore.Authorization;
using SqlSugar;
using RoleEntity = LogicNet.Core.Entity.Role;

namespace LogicNet.Application.Roles.Services;

/// <summary>
///     角色基础信息：供管理端「按角色分配菜单」选择角色使用（仅 Admin）。
///     用户-角色绑定关系不在本服务维护。
/// </summary>
public class RoleService(ISqlSugarClient db) : LogicBase
{
    private readonly ISqlSugarClient db = db;

    /// <summary>
    ///     角色列表：按 Id 升序返回未删除角色；Id 由全局序列化器输出为字符串
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task<List<RoleOutputDto>> GetRoleListAsync()
    {
        return await db.Queryable<RoleEntity>()
            .OrderBy(x => x.Id, OrderByType.Asc)
            .Select<RoleOutputDto>()
            .ToListAsync();
    }
}
