<template>
  <main class="auth-page">
    <a-card class="auth-card" :title="t('auth.signIn')">
      <a-alert v-if="error" type="error" show-icon :message="error" style="margin-bottom: 16px" />
      <form class="auth-form" @submit.prevent="submit">
        <a-form-item :label="t('field.username')" required><a-input v-model:value="username" autocomplete="username" /></a-form-item>
        <a-form-item :label="t('auth.password')" required><a-input-password v-model:value="password" autocomplete="current-password" /></a-form-item>
        <a-button type="primary" html-type="submit" :loading="busy" block>{{ t('auth.signIn') }}</a-button>
      </form>
      <a-select :value="locale" style="margin-top: 20px; width: 100%" @change="changeLanguage">
        <a-select-option value="en">English</a-select-option>
        <a-select-option value="zh-CN">简体中文</a-select-option>
      </a-select>
    </a-card>
  </main>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { authSession } from '@/services/authSession'
import { defaultRouteForRole } from '@/services/permissions'
import { saveLocale, type SupportedLocale } from '@/i18n'
import type { Role } from '@/types/auth'

const { t, locale } = useI18n()
const router = useRouter()
const route = useRoute()
const username = ref('')
const password = ref('')
const busy = ref(false)
const error = ref('')
async function submit() {
  busy.value = true; error.value = ''
  try {
    await authSession.login(username.value, password.value)
    const user = authSession.user.value!
    if (user.mustChangePassword) await router.replace('/change-password')
    else await router.replace(typeof route.query.from === 'string' && route.query.from.startsWith('/')
      ? route.query.from : defaultRouteForRole(user.role.toLowerCase() as Role))
  } catch { error.value = t('auth.invalidCredentials') }
  finally { busy.value = false; password.value = '' }
}
function changeLanguage(next: SupportedLocale) { locale.value = next; saveLocale(next) }
</script>

<style scoped>
.auth-page { min-height: 100vh; display: grid; place-items: center; background: #eef2f6; padding: 24px; }
.auth-card { width: min(420px, 100%); }
.auth-form { display: grid; }
</style>
