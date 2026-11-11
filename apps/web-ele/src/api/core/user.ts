import type { Recordable, UserInfo } from '@vben/types';

import { useAccessStore } from '@vben/stores';

/** .NET ClaimTypes.Role 在 JWT 中的完整声明名 */
const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

/** 解析 JWT 载荷（仅读取用于展示的声明，不校验签名，签名由后端负责） */
function decodeJwtPayload(token: string): Recordable<any> {
  const payload = token.split('.')[1];
  if (!payload) return {};
  try {
    // JWT 使用 base64url 且省略填充，需补全后再解码
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const binary = atob(
      base64.padEnd(Math.ceil(base64.length / 4) * 4, '='),
    );
    const json = decodeURIComponent(
      Array.prototype.map
        .call(binary, (char: string) => {
          return `%${`00${char.charCodeAt(0).toString(16)}`.slice(-2)}`;
        })
        .join(''),
    );
    return JSON.parse(json) as Recordable<any>;
  } catch {
    return {};
  }
}

/**
 * 获取用户信息
 *
 * 后端未提供独立的用户信息接口，从访问令牌声明（UserId / Account / role）中还原，
 * 避免为展示信息新增后端契约。
 */
export async function getUserInfoApi(): Promise<UserInfo> {
  const accessStore = useAccessStore();
  const token = accessStore.accessToken ?? '';
  const payload = decodeJwtPayload(token);
  const roleValue = payload[ROLE_CLAIM] ?? payload.role;
  const roles = (Array.isArray(roleValue) ? roleValue : [roleValue])
    .filter(Boolean)
    .map(String);

  const username = String(payload.Account ?? '');
  const userId = String(payload.UserId ?? '');

  return {
    avatar: '',
    desc: '',
    homePath: '',
    realName: username,
    roles,
    token,
    userId,
    username,
  };
}
