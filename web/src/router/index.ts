import { createRouter, createWebHistory, type RouteLocationNormalized } from 'vue-router'
import AppShell from '@/app/AppShell.vue'
import HomePage from '@/features/home/HomePage.vue'
import PatrolStandardsPage from '@/features/patrol-standards/PatrolStandardsPage.vue'
import PatrolPlansPage from '@/features/patrol-plans/PatrolPlansPage.vue'
import PatrolTasksPage from '@/features/patrol-tasks/PatrolTasksPage.vue'
import CompletedRecordsPage from '@/features/completed-records/CompletedRecordsPage.vue'
import UserManagementPage from '@/features/user-management/UserManagementPage.vue'
import StatusPage from '@/features/system/StatusPage.vue'
import { defaultRouteForRole, hasPermission } from '@/services/permissions'
import { authSession } from '@/services/authSession'
import LoginPage from '@/features/auth/LoginPage.vue'
import ChangePasswordPage from '@/features/auth/ChangePasswordPage.vue'
import type { Permission } from '@/types/auth'

export function routeIsAuthorized(route: Pick<RouteLocationNormalized, 'meta'>): boolean {
  return hasPermission(authSession.user.value?.permissions ?? [], route.meta.permission as Permission | undefined)
}

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: LoginPage },
    { path: '/change-password', name: 'change-password', component: ChangePasswordPage },
    {
      path: '/',
      component: AppShell,
      redirect: () => defaultRouteForRole(authSession.user.value?.role.toLowerCase() as import('@/types/auth').Role ?? 'admin'),
      children: [
        { path: 'home', name: 'home', component: HomePage, meta: { permission: 'home:view' } },
        { path: 'standards', name: 'standards', component: PatrolStandardsPage, meta: { permission: 'standards:view' } },
        { path: 'plans', name: 'plans', component: PatrolPlansPage, meta: { permission: 'plans:view' } },
        { path: 'tasks', name: 'tasks', component: PatrolTasksPage, meta: { permission: 'tasks:view' } },
        { path: 'records', name: 'records', component: CompletedRecordsPage, meta: { permission: 'records:view' } },
        { path: 'users', name: 'users', component: UserManagementPage, meta: { permission: 'users:view' } },
        { path: 'forbidden', name: 'forbidden', component: StatusPage, meta: { status: 'permission' } },
        { path: ':pathMatch(.*)*', name: 'not-found', component: StatusPage, meta: { status: 'notFound' } },
      ],
    },
  ],
})

router.beforeEach(async (to) => {
  await authSession.restore()
  if (!authSession.authenticated.value && to.name !== 'login')
    return { name: 'login', query: { from: to.fullPath } }
  if (authSession.authenticated.value && authSession.user.value?.mustChangePassword &&
      to.name !== 'change-password') return { name: 'change-password' }
  if (authSession.authenticated.value && to.name === 'login')
    return defaultRouteForRole(authSession.user.value!.role.toLowerCase() as import('@/types/auth').Role)
  if (to.name === 'login' || to.name === 'change-password') return true
  if (to.name !== 'forbidden' && !routeIsAuthorized(to)) {
    return { name: 'forbidden', query: { from: to.fullPath } }
  }
  return true
})

declare module 'vue-router' {
  interface RouteMeta {
    permission?: Permission
    status?: 'permission' | 'notFound'
  }
}
