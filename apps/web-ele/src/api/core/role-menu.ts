import { requestClient } from '#/api/request';

/** 角色列表项（Id 为字符串，禁止转 number 后回传；保存 Authorization 时再按需转数值） */
export interface RoleItem {
  Id: string;
  Name: string;
}

/** 保存最终勾选集合；父子联动由后端补齐/级联，前端只提交所见勾选 */
export interface SaveRoleMenusParams {
  MenuIds: number[];
  RoleId: number;
}

/** 角色列表（Admin 可见），用于「按角色分配菜单」选择角色 */
export async function getRoleListApi() {
  return requestClient.get<RoleItem[]>('/Role/GetRoleList');
}

/** 该角色已绑定的菜单 Id 集合（字符串形式返回） */
export async function getRoleMenusApi(roleId: string) {
  return requestClient.get<string[]>('/RoleMenu/GetRoleMenus', {
    params: { roleId },
  });
}

/** 保存角色菜单绑定（最终勾选集合） */
export async function saveRoleMenusApi(data: SaveRoleMenusParams) {
  return requestClient.post<boolean>('/RoleMenu/SaveRoleMenus', data);
}
