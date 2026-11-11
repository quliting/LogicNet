<script lang="ts" setup>
import type { VbenFormSchema } from '@vben/common-ui';
import type { Recordable } from '@vben/types';

import { computed, markRaw, onMounted } from 'vue';

import { AuthenticationLogin, z } from '@vben/common-ui';
import { $t } from '@vben/locales';

import { ElMessage } from 'element-plus';

import { useAuthStore } from '#/store';

import CaptchaInput from './components/captcha-input.vue';
import { useCaptcha } from './composables/use-captcha';

defineOptions({ name: 'Login' });

const authStore = useAuthStore();
const { captchaKey, loadCaptcha } = useCaptcha();

const formSchema = computed((): VbenFormSchema[] => {
  return [
    {
      component: 'VbenInput',
      componentProps: {
        placeholder: $t('authentication.usernameTip'),
      },
      fieldName: 'username',
      label: $t('authentication.username'),
      rules: z.string().min(1, { message: $t('authentication.usernameTip') }),
    },
    {
      component: 'VbenInputPassword',
      componentProps: {
        placeholder: $t('authentication.password'),
      },
      fieldName: 'password',
      label: $t('authentication.password'),
      rules: z.string().min(1, { message: $t('authentication.passwordTip') }),
    },
    {
      component: markRaw(CaptchaInput),
      fieldName: 'code',
      label: '验证码',
      rules: z.string().min(1, { message: '请输入验证码' }),
    },
  ];
});

async function handleLogin(values: Recordable<any>) {
  try {
    await authStore.authLogin({ ...values, key: captchaKey.value });
  } catch (error) {
    // 登录走 raw 响应（需读取 x-access-token 响应头），业务失败提示在此展示
    ElMessage.error({
      message: (error as Error)?.message || '登录失败',
      duration: 5000,
    });
    // 验证码一次性使用，失败后必须刷新
    await loadCaptcha();
  }
}

onMounted(() => {
  void loadCaptcha();
});
</script>

<template>
  <AuthenticationLogin
    :form-schema="formSchema"
    :loading="authStore.loginLoading"
    :show-code-login="false"
    :show-forget-password="false"
    :show-qrcode-login="false"
    :show-register="false"
    :show-third-party-login="false"
    @submit="handleLogin"
  />
</template>
