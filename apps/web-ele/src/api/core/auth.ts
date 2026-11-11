import { useAccessStore } from '@vben/stores';

import {
  apiOrigin,
  assertFurionSuccess,
  baseRequestClient,
  requestClient,
} from '#/api/request';

/** 后端（Furion）登录类型：1=手机号码登录，2=用户名密码登录 */
const LOGIN_TYPE_USERNAME = 2;

export namespace AuthApi {
  /** 图形验证码 */
  export interface CaptchaResult {
    Key: string;
    Url: string;
  }

  /** 登录接口参数（表单值 + 验证码 Key） */
  export interface LoginParams {
    /** 验证码（图形内容） */
    code: string;
    /** 验证码存储键，来自 GetCaptcha */
    key: string;
    password: string;
    username: string;
  }

  /** 登录接口返回值 */
  export interface LoginResult {
    accessToken: string;
  }

  /** 刷新接口原始返回（baseRequestClient 不解析统一结构，需自行读取） */
  export interface RefreshTokenResult {
    data: {
      Code: number;
      Data: string;
      Succeeded: boolean;
    };
    headers: Record<string, string>;
    status: number;
  }
}

/**
 * 获取图形验证码：返回 { Url: '/Temp/xxx.png', Key }，Url 为后端根相对路径
 */
export async function getCaptchaApi() {
  const data = await requestClient.get<AuthApi.CaptchaResult>(
    '/Captcha/GetCaptcha',
  );
  return {
    ...data,
    Url: data.Url.startsWith('http') ? data.Url : `${apiOrigin}${data.Url}`,
  };
}

/**
 * 登录：访问令牌在响应体 Data 中，刷新令牌在响应头 x-access-token 中
 */
export async function loginApi(data: AuthApi.LoginParams) {
  const accessStore = useAccessStore();
  const response = await requestClient.post<AuthApi.LoginResult>(
    '/UserInfo/Login',
    {
      LoginType: LOGIN_TYPE_USERNAME,
      UserName: data.username,
      PassWord: data.password,
      Key: data.key,
      Code: data.code,
    },
    {
      responseReturn: 'raw',
    },
  );

  assertFurionSuccess(response);

  const accessToken = (response as any)?.data?.Data as string;
  const refreshToken = ((response as any)?.headers ?? {})[
    'x-access-token'
  ] as string;

  if (refreshToken) accessStore.setRefreshToken(refreshToken);
  return { accessToken };
}

/**
 * 刷新accessToken：提交已过期的访问令牌 + 刷新令牌，轮换后的刷新令牌写回 store
 */
export async function refreshTokenApi() {
  const accessStore = useAccessStore();
  const response = await baseRequestClient.post<AuthApi.RefreshTokenResult>(
    '/UserInfo/Refresh',
    {
      Token: accessStore.accessToken,
      RefreshToken: accessStore.refreshToken,
    },
    { responseReturn: 'raw' },
  );

  const body = (response as any)?.data;
  if (!body || body.Code !== 200 || body.Succeeded !== true) {
    throw new Error(body?.Errors ?? '刷新令牌失败');
  }

  const newRefreshToken = ((response as any)?.headers ?? {})[
    'x-access-token'
  ] as string;
  if (newRefreshToken) accessStore.setRefreshToken(newRefreshToken);

  return body.Data as string;
}

/**
 * 退出登录：后端会吊销访问令牌与 x-access-token 请求头随附的刷新令牌
 */
export async function logoutApi() {
  const accessStore = useAccessStore();
  await requestClient.post('/UserInfo/Logout', undefined, {
    headers: {
      'x-access-token': accessStore.refreshToken ?? '',
    },
  });
}

/**
 * 后端未提供按钮级权限码接口（GetMyMenus 已过滤按钮），返回空数组保持接口契约
 */
export async function getAccessCodesApi() {
  return [] as string[];
}
