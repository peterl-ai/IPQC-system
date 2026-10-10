import { authSession } from './authSession'

export async function authFetch(path: string, init: RequestInit = {}): Promise<Response> {
  await authSession.restore()
  const send = (token: string | null) => {
    const headers = new Headers(init.headers)
    if (token) headers.set('Authorization', `Bearer ${token}`)
    return fetch(path.startsWith('/api') ? path : `/api${path}`, {
      ...init, headers, credentials: 'same-origin',
    })
  }
  let response = await send(authSession.accessToken.value)
  if (response.status === 401 && await authSession.refresh())
    response = await send(authSession.accessToken.value)
  if (response.status === 401) authSession.clear()
  return response
}
