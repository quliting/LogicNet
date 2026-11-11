<script lang="ts" setup>
import { onMounted } from 'vue';

import { ElInput } from 'element-plus';

import { useCaptcha } from '../composables/use-captcha';

const code = defineModel<string>({ default: '' });

const { captchaLoading, captchaUrl, loadCaptcha } = useCaptcha();

onMounted(() => {
  if (!captchaUrl.value) void loadCaptcha();
});
</script>

<template>
  <div class="flex w-full items-center gap-2">
    <ElInput
      v-model="code"
      maxlength="6"
      placeholder="请输入验证码"
      class="flex-1"
    />
    <img
      :src="captchaUrl"
      class="h-10 w-28 cursor-pointer rounded border object-cover"
      :class="{ 'opacity-60': captchaLoading }"
      alt="验证码"
      title="点击刷新验证码"
      @click="loadCaptcha"
    />
  </div>
</template>
