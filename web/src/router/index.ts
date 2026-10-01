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
import { mockSession } from '@/services/mockSession'
import type { Permission } from '@/types/auth'

export function routeIsAuthorized(route: Pick<RouteLocationNormalized, 'meta'>, role = mockSession.role.value): boolean {
  return hasPermission(role, route.meta.permission as Permission | undefined)
}

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      component: AppShell,
      redirect: '/home',
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

router.beforeEach((to) => {
  if (to.name !== 'forbidden' && !routeIsAuthorized(to)) {
    return { name: 'forbidden', query: { from: to.fullPath } }
  }
  if (to.path === '/home' && mockSession.role.value === 'ipqa') return defaultRouteForRole('ipqa')
  return true
})

declare module 'vue-router' {
  interface RouteMeta {
    permission?: Permission
    status?: 'permission' | 'notFound'
  }
}
