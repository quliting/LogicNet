# 菜单维护与动态导航接口

前端可仅依赖 `GetMyMenus` 渲染侧栏导航；管理端通过菜单 CRUD 维护数据。鉴权沿用全局默认认证：
匿名访问一律 401，管理类接口要求 `Admin` 角色（403 表示已登录但角色不足）。

## 接口清单

Furion 动态 API，路由前缀 `/Api/Menu`。请求体为 JSON；单参数接口（详情/删除）用查询参数 `id`。

| 方法 | 路径 | 权限 | 说明 |
| --- | --- | --- | --- |
| POST | `/Api/Menu/CreateMenu` | Admin | 新增，返回新菜单 Id |
| PUT | `/Api/Menu/UpdateMenu` | Admin | 编辑（含 `Id`） |
| DELETE | `/Api/Menu/DeleteMenu?id=` | Admin | 软删除；有子节点时返回 400 可读提示 |
| GET | `/Api/Menu/GetMenuDetail?id=` | Admin | 详情 |
| GET | `/Api/Menu/GetMenuList` | Admin | 分页列表，支持 `Name` 模糊、`Type`、`IsEnabled` 过滤 |
| GET | `/Api/Menu/GetMenuTree` | Admin | 管理端完整树 |
| GET | `/Api/Menu/GetMyMenus` | 登录 | 当前用户导航树（已启用 + 侧栏可见 + 非按钮） |

> 实际 HTTP 谓词以 Furion 动态 API 生成结果为准；若框架按动作名前缀推断不一致，以服务端路由为准。

## 字段约定

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `Id` | long | 雪花 Id |
| `ParentId` | long | 上级 Id，顶级为 `0` |
| `Name` | string | 显示名 |
| `Path` | string | 前端路由，必须以 `/` 开头；同一 `ParentId` 下唯一 |
| `Component` | string? | 前端组件标识，目录可为空 |
| `Icon` | string? | 图标标识 |
| `Sort` | int | 同级升序 |
| `Type` | int/enum | `1=Directory`、`2=Menu`、`3=Button` |
| `PermissionCode` | string? | 按钮级权限标识 |
| `IsEnabled` | bool | 是否启用 |
| `IsVisible` | bool | 是否在侧栏显示 |
| `Children` | array | 仅树接口返回，按 `Sort` 升序 |

## 树与删除策略

- 防环：编辑时沿新上级链向上回溯，命中自身即拒绝；上级必须存在且未软删除。
- 路径唯一：服务层先校验并给出可读错误，`(ParentId, Path)` 唯一索引兜底并发写入。
- 删除策略：**拒绝**删除仍有子节点的菜单，需自底向上逐级删除。软删除由全局过滤器自动从查询中排除。
- 动态树只以 `ParentId=0` 为根；父级被禁用/隐藏时其子级不会提升为顶级，避免出现孤立导航。
- 角色收窄未接入（本轮无 RoleMenu），扩展点集中在 `MenuService.ApplyRoleFilter` 一处。

## 种子数据与部署

`DatabaseInitializer` 幂等写入「系统管理」→「菜单管理」（`/system/menu`），已软删除的种子不复活。
存量库如未自动建表/建索引，执行 `Sql/2026-11-04_Menu表与索引.sql`（可重复执行）。

> 注意：软删除行仍占用 `(ParentId, Path)` 唯一索引，删除后同路径重建会被拒绝；
> 如需复用该路径，先物理清理旧行。
