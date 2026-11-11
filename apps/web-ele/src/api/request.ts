/**
 * 该文件可自行根据业务逻辑进行调整
 *
 * 对接 LogicNet 后端（.NET 9 + Furion）：
 * - 统一返回结构 { Code, Data, Succeeded, Errors, Extras, Timestamp }，业务失败时 HTTP 仍可能为 200
 * - 成功判定：Code === 200 且 Succeeded === true
 * - 刷新令牌经响应头 x-access-token 下发（登录与刷新时均带），访问令牌在 Data 中
 */
import type { RequestClientOptions } from '@vben/request';

import { useAppConfig } from '@vben/hooks';
import { preferences } from '@vben/preferences';
import {
  authenticateResponseInterceptor,
  errorMessageResponseInterceptor,
  RequestClient,
} from '@vben/request';
import { useAccessStore } from '@vben/stores';

import { ElMessage } from 'element-plus';

import { useAuthStore } from '#/store';

import { refreshTokenApi } from './core';

const { apiURL } = useAppConfig(import.meta.env, import.meta.env.PROD);

/** 后端静态资源（验证码图片等）根地址，由接口地址去掉 /Api 前缀得到 */
export const apiOrigin = apiURL.replace(/\/api\/?$/i, '');

/** Furion 统一返回结构 */
interface FurionResult<T = any> {
  Code: number;
  Data: T;
  Errors?: any;
  Extras?: any;
  Succeeded: boolean;
  Timestamp: string;
}

function isFurionSuccess(body: any) {
  return body?.Code === 200 && body?.Succeeded === true;
}

/** 把业务失败包装成 fetch 风格的错误对象，交给后续错误拦截器统一提示 */
function toFurionError(response: any) {
  return Object.assign({}, response, { response, __furionBusinessError: true });
}

/**
 * 校验 Furion 业务结果（供 responseReturn: 'raw' 的调用方使用，
 * raw 模式不会走统一拦截器的业务码判断，必须显式校验）
 */
function assertFurionSuccess(response: any) {
  const body = response?.data;
  if (!isFurionSuccess(body)) {
    // 已在 axios 拦截器之外，抛出带可读文案的 Error 供调用方提示
    const errors = Array.isArray(body?.Errors)
      ? body.Errors.map((item: any) => item?.Message ?? item).join('；')
      : body?.Errors;
    throw new Error(errors ? String(errors) : '请求失败');
  }
  return body;
}

function createRequestClient(baseURL: string, options?: RequestClientOptions) {
  const client = new RequestClient({
    ...options,
    baseURL,
  });

  /**
   * 重新认证逻辑
   */
  async function doReAuthenticate() {
    console.warn('Access token or refresh token is invalid or expired. ');
    const accessStore = useAccessStore();
    const authStore = useAuthStore();
    accessStore.setAccessToken(null);
    accessStore.setRefreshToken(null);
    if (
      preferences.app.loginExpiredMode === 'modal' &&
      accessStore.isAccessChecked
    ) {
      accessStore.setLoginExpired(true);
    } else {
      await authStore.logout();
    }
  }

  /**
   * 刷新token逻辑：后端要求提交 { Token: 已过期访问令牌, RefreshToken }，
   * 新访问令牌在响应体 Data 中，轮换后的刷新令牌在响应头 x-access-token 中
   */
  async function doRefreshToken() {
    const accessStore = useAccessStore();
    const newToken = await refreshTokenApi();
    accessStore.setAccessToken(newToken);
    return newToken;
  }

  function formatToken(token: null | string) {
    return token ? `Bearer ${token}` : null;
  }

  // 请求头处理
  client.addRequestInterceptor({
    fulfilled: async (config) => {
      const accessStore = useAccessStore();

      config.headers.Authorization = formatToken(accessStore.accessToken);
      config.headers['Accept-Language'] = preferences.app.locale;
      return config;
    },
  });

  // Furion 业务成功判定：Code === 200 且 Succeeded === true（HTTP 200 不代表成功）
  client.addResponseInterceptor({
    fulfilled: (response: any) => {
      const { config, data: responseData, status } = response;

      if (config.responseReturn === 'raw') {
        return response;
      }

      if (status >= 200 && status < 400) {
        if (config.responseReturn === 'body') {
          return responseData;
        }
        if (isFurionSuccess(responseData)) {
          return responseData.Data;
        }
      }
      throw toFurionError(response);
    },
    rejected: (error) => Promise.reject(error),
  });

  // token过期的处理
  client.addResponseInterceptor(
    authenticateResponseInterceptor({
      client,
      doReAuthenticate,
      doRefreshToken,
      enableRefreshToken: preferences.app.enableRefreshToken,
      formatToken,
    }),
  );

  // 通用的错误处理，如果没有进入上面的错误处理逻辑，就会进入这里
  client.addResponseInterceptor(
    errorMessageResponseInterceptor((msg: string, error) => {
      const responseData = error?.response?.data ?? {};
      // 后端业务失败：Errors 为可读字符串（参数校验失败时可能是数组）
      const furionErrors = Array.isArray(responseData?.Errors)
        ? responseData.Errors.map((item: any) => item?.Message ?? item).join('；')
        : responseData?.Errors;
      // 业务失败（含 HTTP 非 200）优先展示后端 Errors 文案，避免只显示通用状态文案
      const errorMessage = furionErrors
        ? String(furionErrors)
        : (responseData?.error ??
          responseData?.message ??
          (error as any)?.message ??
          '');

      ElMessage.error(errorMessage || msg);
    }),
  );

  return client;
}

export const requestClient = createRequestClient(apiURL, {
  responseReturn: 'data',
});

export const baseRequestClient = new RequestClient({ baseURL: apiURL });

export { assertFurionSuccess };
export type { FurionResult };
