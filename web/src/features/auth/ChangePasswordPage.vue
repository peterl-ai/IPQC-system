<template>
  <main class="auth-page">
    <a-card class="auth-card" :title="t('auth.changePassword')">
      <a-alert v-if="error" type="error" show-icon :message="error" style="margin-bottom: 16px" />
      <p>{{ t('auth.passwordPolicy') }}</p>
      <form class="auth-form" @submit.prevent="submit">
        <a-form-item :label="t('auth.currentPassword')" required><a-input-password v-model:value="current" autocomplete="current-password" /></a-form-item>
        <a-form-item :label="t('auth.newPassword')" required><a-input-password v-model:value="next" autocomplete="new-password" /></a-form-item>
        <a-form-item :label="t('auth.confirmPassword')" required><a-input-password v-model:value="confirm" autocomplete="new-password" /></a-form-item>
        <a-button type="primary" html-type="submit" :loading="busy" block>{{ t('auth.changePassword') }}</a-button>
      </form>
    </a-card>
  </main>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { authSession } from '@/services/authSession'

const { t } = useI18n()
const router = useRouter()
const current = ref('')
const next = ref('')
const confirm = ref('')
const busy = ref(false)
const error = ref('')
async function submit() {
  if (next.value !== confirm.value) { error.value = t('auth.passwordMismatch'); return }
  busy.value = true; error.value = ''
  try { await authSession.changePassword(current.value, next.value); await router.replace('/login') }
  catch { error.value = t('auth.passwordChangeFailed') }
  finally { busy.value = false; current.value = ''; next.value = ''; confirm.value = '' }
}
</script>

<style scoped>
.auth-page { min-height: 100vh; display: grid; place-items: center; background: #eef2f6; padding: 24px; }
.auth-card { width: min(420px, 100%); }
.auth-form { display: grid; }
</style>
