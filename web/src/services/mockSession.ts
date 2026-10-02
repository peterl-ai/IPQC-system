import { computed, ref } from 'vue'
import type { Role } from '@/types/auth'

const ROLE_KEY = 'jax-ipqc-mock-role'
const validRoles: Role[] = ['admin', 'pqe', 'ipqa']

function initialRole(): Role {
  const saved = localStorage.getItem(ROLE_KEY) as Role | null
  return saved && validRoles.includes(saved) ? saved : 'admin'
}

const role = ref<Role>(initialRole())

export const mockSession = {
  role,
  userName: computed(() => ({ admin: 'Alex Morgan', pqe: 'Priya Shah', ipqa: 'Jordan Lee' })[role.value]),
  setRole(nextRole: Role) {
    role.value = nextRole
    localStorage.setItem(ROLE_KEY, nextRole)
  },
}
