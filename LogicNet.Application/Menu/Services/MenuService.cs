using System.Security.Claims;
using LogicNet.Application.Menu.DTO;
using LogicNet.Core.Entity;
using LogicNet.Core.Security;
using Masuit.Tools;
using Masuit.Tools.Models;
using SqlSugar;
using MenuEntity = LogicNet.Core.Entity.Menu;

namespace LogicNet.Application.Menu.Services;

/// <summary>
///     菜单维护：新增/编辑/删除（软删除）/详情/列表。
///     树查询与当前用户动态加载见 GetMenuTreeAsync / GetMyMenusAsync。
/// </summary>
public class MenuService(ISqlSugarClient db) : LogicBase
{
    private readonly ISqlSugarClient db = db;

    /// <summary>
    ///     新增菜单：校验上级存在且不成环，同一上级下路由路径唯一
    /// </summary>
    public async Task<long> CreateMenuAsync(AddMenuInputDto input)
    {
        ValidateInput(input);
        await EnsureParentAsync(input.ParentId, 0);
        await EnsurePathUniqueAsync(input.ParentId, input.Path, 0);

        var entity = new MenuEntity();
        ApplyInput(input, entity);
        try
        {
            return await db.Insertable(entity).ExecuteReturnSnowflakeIdAsync();
        }
        catch (Exception ex) when (IsUniqueKeyConflict(ex))
        {
            // 数据库唯一索引兜底并发写入（服务层校验与写入之间存在窗口）
            throw Oops.Bah("同一上级下已存在相同路由路径的菜单");
        }
    }

    /// <summary>
    ///     编辑菜单：不允许把上级指向自身或自己的后代，避免形成环
    /// </summary>
    public async Task<bool> UpdateMenuAsync(UpdateMenuInputDto input)
    {
        if (input.Id <= 0) throw Oops.Bah("菜单 Id 无效");

        var entity = await db.Queryable<MenuEntity>().InSingleAsync(input.Id);
        if (entity is null) throw Oops.Bah("菜单不存在或已删除");

        ValidateInput(input);
        await EnsureParentAsync(input.ParentId, input.Id);
        await EnsurePathUniqueAsync(input.ParentId, input.Path, input.Id);

        ApplyInput(input, entity);
        try
        {
            return await db.Updateable(entity).ExecuteCommandAsync() > 0;
        }
        catch (Exception ex) when (IsUniqueKeyConflict(ex))
        {
            throw Oops.Bah("同一上级下已存在相同路由路径的菜单");
        }
    }

    /// <summary>
    ///     删除菜单（软删除）：存在子节点时拒绝并给出可读提示，需先删除子节点
    /// </summary>
    public async Task<bool> DeleteMenuAsync(long id)
    {
        if (id <= 0) throw Oops.Bah("菜单 Id 无效");

        var entity = await db.Queryable<MenuEntity>().InSingleAsync(id);
        if (entity is null) throw Oops.Bah("菜单不存在或已删除");

        var hasChildren = await db.Queryable<MenuEntity>().AnyAsync(x => x.ParentId == id);
        if (hasChildren) throw Oops.Bah("该菜单下存在子节点，请先删除子节点后再删除");

        return await db.Updateable<MenuEntity>()
            .SetColumns(x => x.IsDelete == true)
            .Where(x => x.Id == id)
            .ExecuteCommandAsync() > 0;
    }

    /// <summary>
    ///     菜单详情
    /// </summary>
    public async Task<MenuOutputDto> GetMenuDetailAsync(long id)
    {
        var result = await db.Queryable<MenuEntity>().Where(x => x.Id == id).Select<MenuOutputDto>().FirstAsync();
        if (result is null) throw Oops.Bah("菜单不存在或已删除");
        return result;
    }

    /// <summary>
    ///     菜单列表：名称模糊、类型/启用状态过滤，分页复用 BaseInput 的 PageSize 钳制
    /// </summary>
    public async Task<PagedList<MenuOutputDto>> GetMenuListAsync(GetMenuListInputDto input)
    {
        var query = db.Queryable<MenuEntity>()
            .WhereIF(input.Name.NotNullOrEmpty(), x => x.Name.Contains(input.Name))
            .WhereIF(input.Type.HasValue, x => x.Type == input.Type.Value)
            .WhereIF(input.IsEnabled.HasValue, x => x.IsEnabled == input.IsEnabled.Value)
            .OrderBy(x => x.Sort, OrderByType.Asc)
            .OrderBy(x => x.Id, OrderByType.Asc);

        return await query.Select<MenuOutputDto>().PagedList(input.PageIndex, input.PageSize);
    }

    /// <summary>
    ///     管理端菜单树：全部未删除菜单，children 嵌套，同级按 Sort 升序
    /// </summary>
    public async Task<List<MenuTreeOutputDto>> GetMenuTreeAsync()
    {
        var list = await db.Queryable<MenuEntity>().Select<MenuTreeOutputDto>().ToListAsync();
        return BuildTree(list);
    }

    /// <summary>
    ///     当前登录用户可访问的导航菜单树：仅已启用、侧栏可见，剔除按钮类型。
    ///     父级不在结果集内时子级一并舍弃，避免禁用父级后出现孤立的顶级菜单。
    /// </summary>
    public async Task<List<MenuTreeOutputDto>> GetMyMenusAsync()
    {
        var userId = long.TryParse(App.HttpContext?.User?.FindFirstValue(JwtClaimNames.UserId), out var parsedUserId)
            ? parsedUserId
            : 0;

        var query = db.Queryable<MenuEntity>()
            .Where(x => x.IsEnabled)
            .Where(x => x.IsVisible)
            .Where(x => x.Type != MenuType.Button);

        // 角色收窄的唯一入口，见 ApplyRoleFilter
        query = ApplyRoleFilter(query, userId);

        var list = await query.Select<MenuTreeOutputDto>().ToListAsync();
        return BuildTree(list);
    }

    /// <summary>
    ///     角色过滤扩展点：本轮未实现 RoleMenu 绑定，所有登录用户返回同一套启用菜单。
    ///     后续接入角色时只改这里（按 userId Join 菜单角色关联表收窄），不要在各查询方法散落权限条件。
    /// </summary>
    private static ISugarQueryable<MenuEntity> ApplyRoleFilter(ISugarQueryable<MenuEntity> query, long userId)
    {
        return query;
    }

    /// <summary>
    ///     按 ParentId 组装树：先按 Sort/Id 排序，再挂到父节点的 Children，同级顺序天然保持升序。
    ///     仅 ParentId=0 作为根节点，父节点缺失的子节点不提升为根。
    /// </summary>
    private static List<MenuTreeOutputDto> BuildTree(IEnumerable<MenuTreeOutputDto> source)
    {
        var ordered = source.OrderBy(x => x.Sort).ThenBy(x => x.Id).ToList();
        var byId = ordered.ToDictionary(x => x.Id);

        foreach (var item in ordered)
        {
            if (item.ParentId == 0) continue;
            if (byId.TryGetValue(item.ParentId, out var parent)) parent.Children.Add(item);
        }

        return ordered.Where(x => x.ParentId == 0).ToList();
    }

    private static void ValidateInput(AddMenuInputDto input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw Oops.Bah("菜单名称不能为空");
        if (string.IsNullOrWhiteSpace(input.Path)) throw Oops.Bah("路由路径不能为空");
        if (!input.Path.StartsWith('/')) throw Oops.Bah("路由路径需以 / 开头");
        if (input.ParentId < 0) throw Oops.Bah("上级菜单 Id 无效");
        if (!Enum.IsDefined(typeof(MenuType), input.Type)) throw Oops.Bah("菜单类型错误");
    }

    private static void ApplyInput(AddMenuInputDto input, MenuEntity entity)
    {
        entity.ParentId = input.ParentId;
        entity.Name = input.Name.Trim();
        entity.Path = input.Path.Trim();
        entity.Component = input.Component;
        entity.Icon = input.Icon;
        entity.Sort = input.Sort;
        entity.Type = input.Type;
        entity.PermissionCode = input.PermissionCode;
        entity.IsEnabled = input.IsEnabled;
        entity.IsVisible = input.IsVisible;
    }

    /// <summary>
    ///     上级合法性：必须存在（未软删除），且不能是自身或自身后代（防环）
    /// </summary>
    private async Task EnsureParentAsync(long parentId, long selfId)
    {
        if (parentId <= 0) return;
        if (parentId == selfId) throw Oops.Bah("上级菜单不能指向自身");

        var parent = await db.Queryable<MenuEntity>().InSingleAsync(parentId);
        if (parent is null) throw Oops.Bah("上级菜单不存在或已删除");

        var visited = new HashSet<long> { selfId };
        var current = parent;
        while (current is not null)
        {
            if (current.Id == selfId) throw Oops.Bah("上级菜单不能指向自身或其下级菜单");
            if (!visited.Add(current.Id)) throw Oops.Bah("菜单层级存在循环，请检查上级设置");
            if (current.ParentId <= 0) return;
            current = await db.Queryable<MenuEntity>().InSingleAsync(current.ParentId);
        }
    }

    /// <summary>
    ///     同一上级下路由路径唯一（软删除数据由全局过滤器排除）
    /// </summary>
    private async Task EnsurePathUniqueAsync(long parentId, string path, long excludeId)
    {
        var exists = await db.Queryable<MenuEntity>()
            .Where(x => x.ParentId == parentId && x.Path == path && x.Id != excludeId)
            .AnyAsync();
        if (exists) throw Oops.Bah("同一上级下已存在相同路由路径的菜单");
    }

    /// <summary>
    ///     唯一索引冲突识别：MySQL 1062 / SQLite UNIQUE constraint（并发写入兜底）
    /// </summary>
    private static bool IsUniqueKeyConflict(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("1062", StringComparison.Ordinal)
                || message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
                || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
