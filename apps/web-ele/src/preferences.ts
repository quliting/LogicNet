import {
  appCopyrightPreferences,
  defineOverridesPreferences,
} from '@vben/preferences';

/**
 * @description 项目配置文件
 * 只需要覆盖项目中的一部分配置，不需要的配置不用覆盖，会自动使用默认配置
 * !!! 更改配置后请清空缓存，否则可能不生效
 */
export const overridesPreferences = defineOverridesPreferences({
  // overrides
  app: {
    name: import.meta.env.VITE_APP_TITLE,
    // 菜单与路由由后端 /Api/Menu/GetMyMenus 下发
    accessMode: 'backend',
    // 刷新令牌经响应头 x-access-token 轮换，必须开启
    enableRefreshToken: true,
    // 动态菜单下仅后端菜单可访问，首页指向种子菜单「菜单管理」
    defaultHomePath: '/system/menu',
  },
  copyright: appCopyrightPreferences,
});
