import { ref } from 'vue';

import { getCaptchaApi } from '#/api';

const captchaKey = ref('');
const captchaUrl = ref('');
const captchaLoading = ref(false);

/** 重新获取图形验证码（登录失败后也应刷新，后端验证码校验一次即销毁） */
async function loadCaptcha() {
  if (captchaLoading.value) return;
  try {
    captchaLoading.value = true;
    const data = await getCaptchaApi();
    captchaKey.value = data.Key;
    captchaUrl.value = data.Url;
  } finally {
    captchaLoading.value = false;
  }
}

export function useCaptcha() {
  return {
    captchaKey,
    captchaLoading,
    captchaUrl,
    loadCaptcha,
  };
}
