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

async function refresh(): Promise<FullAuthResponse> {
  return api.post<FullAuthResponse>('/api/v1/auth/refresh')
}

export const authApi = { login, mfaSetup, mfaSetupConfirm, mfaVerify, refresh }
