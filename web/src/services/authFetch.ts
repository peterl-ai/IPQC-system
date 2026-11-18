import { authSession, type CurrentUser } from './authSession'

interface RequestAuthority {
  authorize: (user: CurrentUser | null) => boolean
  refreshOnUnauthorized: boolean
}

export async function authFetch(path: string, init: RequestInit = {}, authority?: RequestAuthority): Promise<Response> {
  await authSession.restore()
  const send = (token: string | null) => {
    if (authority && (!token || authSession.signingOut.value || !authority.authorize(authSession.user.value))) {
      return Promise.resolve(new Response(null, { status: token ? 403 : 401 }))
    }
    const headers = new Headers(init.headers)
    if (token) headers.set('Authorization', `Bearer ${token}`)
    return fetch(path.startsWith('/api') ? path : `/api${path}`, {
      ...init, headers, credentials: 'same-origin',
    })
  }
  let response = await send(authSession.accessToken.value)
  if (authority) {
    if (response.status === 401 && authority.refreshOnUnauthorized && !authSession.signingOut.value &&
      authority.authorize(authSession.user.value) && await authSession.refresh()) {
      response = await send(authSession.accessToken.value)
    }
    return response
  }
  if (response.status === 401 && await authSession.refresh())
    response = await send(authSession.accessToken.value)
  if (response.status === 401) authSession.clear()
  return response
}
