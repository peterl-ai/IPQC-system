import { computed, ref } from 'vue'

export interface CurrentUser {
  id: string
  username: string
  displayName: string
  role: 'Admin' | 'PQE' | 'IPQA'
  isActive: boolean
  mustChangePassword: boolean
  permissions: string[]
}
interface AuthResponse { accessToken: string; accessExpiresAtUtc: string; user: CurrentUser }

const user = ref<CurrentUser | null>(null)
const accessToken = ref<string | null>(null)
const initialized = ref(false)
let restorePromise: Promise<void> | null = null
let refreshPromise: Promise<boolean> | null = null

async function me(token: string): Promise<CurrentUser> {
  const response = await fetch('/api/me', { headers: { Authorization: `Bearer ${token}` } })
  if (!response.ok) throw new Error('Session unavailable')
  return response.json() as Promise<CurrentUser>
}

async function refresh(): Promise<boolean> {
  if (refreshPromise) return refreshPromise
  refreshPromise = (async () => {
    try {
      const response = await fetch('/api/auth/refresh', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: '{}', credentials: 'same-origin',
      })
      if (!response.ok) throw new Error('Refresh failed')
      const data = await response.json() as AuthResponse
      accessToken.value = data.accessToken
      user.value = await me(data.accessToken)
      return true
    } catch {
      accessToken.value = null
      user.value = null
      return false
    } finally { initialized.value = true; refreshPromise = null }
  })()
  return refreshPromise
}

export const authSession = {
  user,
  accessToken,
  initialized,
  authenticated: computed(() => user.value !== null),
  async restore() {
    if (initialized.value) return
    if (!restorePromise) restorePromise = refresh().then(() => undefined).finally(() => { restorePromise = null })
    await restorePromise
  },
  refresh,
  async login(username: string, password: string) {
    const response = await fetch('/api/auth/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password, clientType: 'Web' }), credentials: 'same-origin',
    })
    if (!response.ok) throw new Error('Invalid username or password.')
    const data = await response.json() as AuthResponse
    accessToken.value = data.accessToken
    user.value = await me(data.accessToken)
    initialized.value = true
  },
  async changePassword(currentPassword: string, newPassword: string) {
    const response = await fetch('/api/auth/change-password', {
      method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${accessToken.value}` },
      body: JSON.stringify({ currentPassword, newPassword }), credentials: 'same-origin',
    })
    if (!response.ok) throw new Error('Password change failed.')
    accessToken.value = null
    user.value = null
  },
  async logout() {
    try {
      const send = () => fetch('/api/auth/logout', {
        method: 'POST', headers: { Authorization: `Bearer ${accessToken.value}`, 'Content-Type': 'application/json' },
        body: '{}', credentials: 'same-origin',
      })
      if (!accessToken.value && !await refresh()) return
      const response = await send()
      if (response.status === 401 && await refresh()) await send()
    } finally { accessToken.value = null; user.value = null; initialized.value = true }
  },
  clear() { accessToken.value = null; user.value = null; initialized.value = true },
}
