import { useClientAuthStore, type ClientProfile } from '../stores/clientAuthStore'
import { getSubdomainFromHostname } from '../utils/subdomain'

/** Client-portal API (S181). Its own fetch: the client token, never the agent one. A 401 ends the session. */
export function clientSubdomain(): string | null {
  return getSubdomainFromHostname() ?? useClientAuthStore.getState().subdomain
}

/** The account was deactivated / deleted, or the session ran out — back to sign-in. */
function endSession() {
  useClientAuthStore.getState().clear()
  window.location.assign('/client/login?ended=1')
}

async function clientFetch<T>(path: string, init: RequestInit = {}, token?: string | null): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json', ...(init.headers as Record<string, string>) }
  const bearer = token === undefined ? useClientAuthStore.getState().token : token
  if (bearer) headers['Authorization'] = `Bearer ${bearer}`
  const sub = clientSubdomain()
  if (sub) headers['X-Tenant-Subdomain'] = sub

  let res: Response
  try {
    res = await fetch(`/api/v1/client-portal${path}`, { ...init, headers })
  } catch (err) {
    // A request with a body that the API refuses before reading it (e.g. a deactivated account) can arrive as a network
    // error rather than a 401 — check the session with a body-less request before calling it a network problem.
    if (token === undefined && useClientAuthStore.getState().token && init.body) {
      const probe = await fetch('/api/v1/client-portal/me', { headers: { ...headers, 'Content-Type': 'application/json' } }).catch(() => null)
      if (probe?.status === 401) endSession()
    }
    throw err instanceof Error && err.message === 'Failed to fetch' ? new Error("Can't reach the server — check your connection.") : err
  }
  if (!res.ok) {
    if (res.status === 401 && token === undefined && useClientAuthStore.getState().token) endSession()
    const body = await res.text()
    let message = res.status === 429 ? 'Too many attempts — wait a minute and try again.' : res.status === 401 ? 'Incorrect email or password.' : `Error ${res.status}`
    try { const p = JSON.parse(body); message = p.error ?? p.detail ?? p.title ?? message } catch { /* keep */ }
    throw new Error(message)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

export interface ClientAuthResponse {
  mfaPending: boolean
  mfaSetupRequired: boolean | null
  preAuthToken: string | null
  token: string | null
  user: ClientProfile | null
}

export interface ClientInviteInfo {
  email: string; firstName: string; lastName: string; hasPassword: boolean
  tenantName: string; tenantLogoUrl: string | null; minPasswordLength: number; mfaRequirement: string
}

export interface ClientDashboardRef { id: string; name: string }

const post = <T>(path: string, body: unknown, token?: string | null) =>
  clientFetch<T>(path, { method: 'POST', body: JSON.stringify(body) }, token)

export const clientPortalApi = {
  invite: (token: string) => clientFetch<ClientInviteInfo>(`/auth/invite/${encodeURIComponent(token)}`, {}, null),
  acceptInvite: (token: string, body: { firstName?: string; lastName?: string; password: string }) =>
    post<ClientAuthResponse>(`/auth/invite/${encodeURIComponent(token)}/accept`, body, null),
  login: (email: string, password: string) => post<ClientAuthResponse>('/auth/login', { email, password }, null),
  mfaSetup: (preAuth: string) => clientFetch<{ secret: string; otpAuthUri: string }>('/auth/mfa/setup', {}, preAuth),
  mfaSetupConfirm: (preAuth: string, code: string) => post<ClientAuthResponse>('/auth/mfa/setup/confirm', { code }, preAuth),
  mfaVerify: (preAuth: string, code: string) => post<ClientAuthResponse>('/auth/mfa/verify', { code }, preAuth),
  me: () => clientFetch<{ profile: ClientProfile; dashboards: ClientDashboardRef[] }>('/me'),
  setPreferences: (timeZone: string | null, defaultDashboardId: string | null) =>
    clientFetch<ClientProfile>('/me/preferences', { method: 'PUT', body: JSON.stringify({ timeZone, defaultDashboardId }) }),
  mfaStart: () => clientFetch<{ secret: string; otpAuthUri: string }>('/me/mfa/setup', { method: 'POST' }),
  mfaEnable: (code: string) => post<ClientProfile>('/me/mfa/enable', { code }),
  mfaDisable: (code: string) => post<ClientProfile>('/me/mfa/disable', { code }),
  dashboard: (id: string) => clientFetch<{ id: string; name: string; layout: string }>(`/dashboards/${id}`),
  widgetData: <T>(dashboardId: string, widgetId: string) => clientFetch<T>(`/dashboards/${dashboardId}/widgets/${encodeURIComponent(widgetId)}/data`),
}
