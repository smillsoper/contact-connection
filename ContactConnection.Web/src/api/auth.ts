import { api } from './client'

interface LoginRequest {
  email: string
  password: string
  tenantSubdomain: string
}

export type LoginResponse =
  | {
      mfaPending: false
      token: string
      agentId: string
      email: string
      firstName: string
      lastName: string
      role: string
      tenantSubdomain: string
      sipExtension: string | null
      sipPassword: string | null   // plaintext, returned once — store in memory only
      permissions: string[]
      landingPage: string
    }
  | {
      mfaPending: true
      preAuthToken: string
      agentId: string
      email: string
      tenantSubdomain: string
      mfaSetupRequired: boolean
    }

export interface FullAuthResponse {
  mfaPending: false
  token: string
  agentId: string
  email: string
  firstName: string
  lastName: string
  role: string
  tenantSubdomain: string
  sipExtension: string | null
  sipPassword: string | null
  permissions: string[]
  landingPage: string
}

export interface MfaSetupData {
  secret: string
  otpAuthUri: string
}

export async function login(req: LoginRequest): Promise<LoginResponse> {
  const res = await fetch('/api/v1/auth/login', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Tenant-Subdomain': req.tenantSubdomain,
    },
    body: JSON.stringify({ email: req.email, password: req.password }),
  })

  if (!res.ok) throw new Error('Invalid credentials')
  return res.json() as Promise<LoginResponse>
}

export async function mfaSetup(preAuthToken: string, subdomain: string): Promise<MfaSetupData> {
  const res = await fetch('/api/v1/auth/mfa/setup', {
    headers: {
      Authorization: `Bearer ${preAuthToken}`,
      'X-Tenant-Subdomain': subdomain,
    },
  })
  if (!res.ok) throw new Error('Failed to load MFA setup')
  return res.json() as Promise<MfaSetupData>
}

export async function mfaSetupConfirm(
  preAuthToken: string,
  subdomain: string,
  code: string
): Promise<FullAuthResponse> {
  const res = await fetch('/api/v1/auth/mfa/setup/confirm', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${preAuthToken}`,
      'X-Tenant-Subdomain': subdomain,
    },
    body: JSON.stringify({ code }),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new Error((body as { error?: string }).error ?? 'Invalid code')
  }
  return res.json() as Promise<FullAuthResponse>
}

export async function mfaVerify(
  preAuthToken: string,
  subdomain: string,
  code: string
): Promise<FullAuthResponse> {
  const res = await fetch('/api/v1/auth/mfa/verify', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${preAuthToken}`,
      'X-Tenant-Subdomain': subdomain,
    },
    body: JSON.stringify({ code }),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new Error((body as { error?: string }).error ?? 'Invalid code')
  }
  return res.json() as Promise<FullAuthResponse>
}

/** Refreshes the login token. `withSip` also issues a new softphone password — only the agent
 *  portal's page load needs that; any other refresh must leave the registered softphone alone. */
async function refresh(withSip = false): Promise<FullAuthResponse> {
  return api.post<FullAuthResponse>(`/api/v1/auth/refresh${withSip ? '?sip=true' : ''}`)
}

// One SIP-issuing refresh per page load: React dev mode runs mount effects twice, and two
// overlapping refreshes each rotate the SIP password — the softphone could keep the older one.
let sipRefreshInFlight: Promise<FullAuthResponse> | null = null
function refreshWithSipOnce(): Promise<FullAuthResponse> {
  sipRefreshInFlight ??= refresh(true)
  return sipRefreshInFlight
}

export const authApi = { login, mfaSetup, mfaSetupConfirm, mfaVerify, refresh, refreshWithSipOnce }
