import type { RouteRecordStringComponent } from '@vben/types';

import { requestClient } from '#/api/request';

/** 后端菜单类型：序列化为字符串，入参同时兼容 1/2/3 */
export type MenuTypeName = 'Button' | 'Directory' | 'Menu';

/** 后端菜单节点（列表/详情/树共用；Id/ParentId 为字符串，禁止转 number） */
export interface BackendMenuItem {
  Children?: BackendMenuItem[];
  Component?: null | string;
  Icon?: null | string;
  Id: string;
  IsEnabled: boolean;
  IsVisible: boolean;
  Name: string;
  ParentId: string;
  Path: string;
  PermissionCode?: null | string;
  Sort: number;
  Type: MenuTypeName;
}

/** 分页结果（Masuit.Tools PagedList 序列化后嵌在 Furion 的 Data 内） */
export interface MenuPagedResult<T> {
  CurrentCount: number;
  CurrentPage: number;
  Data: T[];
  HasNext: boolean;
  HasPrev: boolean;
  PageSize: number;
  TotalCount: number;
  TotalPages: number;
}

/** GetMenuList 查询参数（PascalCase 与后端 DTO 对齐） */
export interface MenuListQuery {
  IsEnabled?: boolean;
  Name?: string;
  PageIndex?: number;
  PageSize?: number;
  Type?: MenuTypeName;
}

/** 新增/编辑入参；编辑时额外带 Id（字符串透传） */
export interface SaveMenuParams {
  Component?: null | string;
  Icon?: null | string;
  IsEnabled: boolean;
  IsVisible: boolean;
  Name: string;
  ParentId: string;
  Path: string;
  PermissionCode?: null | string;
  Sort: number;
  Type: MenuTypeName;
}

export interface UpdateMenuParams extends SaveMenuParams {
  Id: string;
}

/**
 * 后端菜单 -> vben 后端路由契约
 * Name→meta.title，Icon→meta.icon，Sort→meta.order，Component→组件路径（相对 views）
 */
function adaptMenuItem(item: BackendMenuItem): RouteRecordStringComponent {
  const route = {
    component: item.Component ?? undefined,
    meta: {
      icon: item.Icon || undefined,
      order: item.Sort,
      hideInMenu: !item.IsVisible,
      title: item.Name,
    },
    // 后端 Id 全局唯一且为字符串，避免同名菜单造成路由冲突
    name: `Menu_${item.Id}`,
    path: item.Path.startsWith('/') ? item.Path : `/${item.Path}`,
  } as RouteRecordStringComponent;

  if (item.Children && item.Children.length > 0) {
    route.children = item.Children.map(adaptMenuItem);
  }

  return route;
}

/**
 * 获取当前用户的导航菜单树（后端已过滤：仅启用、侧栏可见、非按钮）
 */
export async function getAllMenusApi() {
  const menus = await requestClient.get<BackendMenuItem[]>('/Menu/GetMyMenus');
  return (menus ?? []).map(adaptMenuItem);
}

/** 管理端分页列表：支持名称模糊、Type、IsEnabled 过滤 */
export async function getMenuListApi(query: MenuListQuery = {}) {
  return requestClient.get<MenuPagedResult<BackendMenuItem>>(
    '/Menu/GetMenuList',
    { params: query },
  );
}

/** 管理端完整菜单树（父子嵌套，同级按 Sort 升序） */
export async function getMenuTreeApi() {
  return requestClient.get<BackendMenuItem[]>('/Menu/GetMenuTree');
}

/** 菜单详情 */
export async function getMenuDetailApi(id: string) {
  return requestClient.get<BackendMenuItem>('/Menu/GetMenuDetail', {
    params: { id },
  });
}

/** 新增菜单，返回新菜单 Id（字符串） */
export async function createMenuApi(data: SaveMenuParams) {
  return requestClient.post<string>('/Menu/CreateMenu', data);
}

/** 编辑菜单（含 Id） */
export async function updateMenuApi(data: UpdateMenuParams) {
  return requestClient.put<boolean>('/Menu/UpdateMenu', data);
}

/**
 * 删除菜单（软删除）。存在子节点时后端返回 HTTP 200 + Code 400，
 * 请求层会按 Code/Succeeded 判定为失败并抛出带 Errors 文案的错误。
 */
export async function deleteMenuApi(id: string) {
  return requestClient.delete<boolean>('/Menu/DeleteMenu', {
    params: { id },
  });
}
