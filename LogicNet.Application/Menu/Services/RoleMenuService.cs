using LogicNet.Application.Menu.DTO;
using LogicNet.Core.Entity;
using Microsoft.AspNetCore.Authorization;
using SqlSugar;
using MenuEntity = LogicNet.Core.Entity.Menu;

namespace LogicNet.Application.Menu.Services;

/// <summary>
///     角色-菜单绑定的查询与批量保存（仅 Admin）。
///     父子联动规则集中在 SaveRoleMenusAsync：勾子级自动补父级，取消父级级联取消子级。
/// </summary>
public class RoleMenuService(ISqlSugarClient db) : LogicBase
{
    private readonly ISqlSugarClient db = db;

    /// <summary>
    ///     查询角色已绑定的菜单 Id 集合（仅 Admin）；菜单软删除后不再返回
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task<List<long>> GetRoleMenusAsync(long roleId)
    {
        if (roleId <= 0) throw Oops.Bah("角色 Id 无效");
        await EnsureRoleExistsAsync(roleId);

        return await db.Queryable<RoleMenu, MenuEntity>((link, menu) => link.MenuId == menu.Id)
            .Where((link, menu) => link.RoleId == roleId)
            .Select((link, menu) => link.MenuId)
            .ToListAsync();
    }

    /// <summary>
    ///     批量保存角色菜单（仅 Admin）：MenuIds 为最终勾选集合，未勾选的绑定会被取消。
    ///     联动规则：
    ///     - 勾选子级自动补齐所有祖先，避免出现“有子无父”的不可达菜单；
    ///     - 相对已有绑定被取消的菜单，其整棵子树一并取消（取消父级级联取消子级）。
    ///     返回 true 表示保存成功；无变化时同样返回 true（幂等）。
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task<bool> SaveRoleMenusAsync(SaveRoleMenusInputDto input)
    {
        if (input.RoleId <= 0) throw Oops.Bah("角色 Id 无效");
        await EnsureRoleExistsAsync(input.RoleId);

        var requested = (input.MenuIds ?? new List<long>())
            .Where(id => id > 0)
            .Distinct()
            .ToHashSet();

        var menus = await db.Queryable<MenuEntity>().ToListAsync();
        var byId = menus.ToDictionary(x => x.Id);
        if (requested.Any(id => !byId.ContainsKey(id)))
            throw Oops.Bah("提交的菜单中包含不存在或已删除的项");

        var childrenByParent = menus
            .GroupBy(x => x.ParentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        // 勾子级自动补父级
        var effective = new HashSet<long>(requested);
        foreach (var id in requested)
            AddAncestors(effective, byId, id);

        var current = await db.Queryable<RoleMenu>()
            .Where(x => x.RoleId == input.RoleId)
            .Select(x => x.MenuId)
            .ToListAsync();
        var currentSet = current.ToHashSet();

        // 取消父级级联取消子级：相对已有绑定被取消的菜单，整棵子树从最终集合剔除
        foreach (var removedId in currentSet.Where(id => !requested.Contains(id)))
            RemoveSubtree(effective, removedId, childrenByParent);

        var toAdd = effective.Except(currentSet).ToList();
        var toRemove = currentSet.Except(effective).ToList();

        await SaveDiffAsync(input.RoleId, toAdd, toRemove);
        return true;
    }

    /// <summary>
    ///     差量落库：取消绑定走软删除；新增时若命中软删除旧行则恢复，避免唯一索引冲突。
    ///     批量操作放在同一事务内，避免部分成功导致授权数据不一致。
    /// </summary>
    private async Task SaveDiffAsync(long roleId, List<long> toAdd, List<long> toRemove)
    {
        try
        {
            await db.Ado.BeginTranAsync();

            if (toRemove.Count > 0)
                await db.Updateable<RoleMenu>()
                    .SetColumns(x => x.IsDelete == true)
                    .Where(x => x.RoleId == roleId && toRemove.Contains(x.MenuId))
                    .ExecuteCommandAsync();

            if (toAdd.Count > 0)
            {
                var existingRows = await db.Queryable<RoleMenu>().ClearFilter()
                    .Where(x => x.RoleId == roleId && toAdd.Contains(x.MenuId))
                    .ToListAsync();
                var restoreIds = existingRows.Select(x => x.MenuId).ToList();

                if (restoreIds.Count > 0)
                    await db.Updateable<RoleMenu>()
                        .SetColumns(x => x.IsDelete == false)
                        .Where(x => x.RoleId == roleId && restoreIds.Contains(x.MenuId))
                        .ExecuteCommandAsync();

                var missing = toAdd.Except(restoreIds)
                    .Select(menuId => new RoleMenu { RoleId = roleId, MenuId = menuId })
                    .ToList();
                // BaseEntity.Id 非自增，插入需显式生成雪花 Id
                if (missing.Count > 0)
                    await db.Insertable(missing).ExecuteReturnSnowflakeIdListAsync();
            }

            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task EnsureRoleExistsAsync(long roleId)
    {
        var exists = await db.Queryable<Role>().AnyAsync(x => x.Id == roleId);
        if (!exists) throw Oops.Bah("角色不存在或已删除");
    }

    /// <summary>
    ///     从指定菜单向上补齐所有祖先；防御性记录已访问节点，避免异常数据成环时死循环。
    /// </summary>
    private static void AddAncestors(HashSet<long> effective, Dictionary<long, MenuEntity> byId, long menuId)
    {
        var visited = new HashSet<long> { menuId };
        var parentId = byId[menuId].ParentId;
        while (parentId > 0 && byId.TryGetValue(parentId, out var parent) && visited.Add(parentId))
        {
            effective.Add(parentId);
            parentId = parent.ParentId;
        }
    }

    /// <summary>
    ///     从最终集合中剔除指定菜单及其所有后代
    /// </summary>
    private static void RemoveSubtree(HashSet<long> effective, long rootId,
        Dictionary<long, List<long>> childrenByParent)
    {
        var stack = new Stack<long>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            effective.Remove(id);
            if (childrenByParent.TryGetValue(id, out var children))
                foreach (var childId in children)
                    stack.Push(childId);
        }
    }
}
