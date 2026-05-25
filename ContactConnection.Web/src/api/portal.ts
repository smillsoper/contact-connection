import { usePortalAuthStore } from '../stores/portalAuthStore'

// Raw fetch with portal auth token — no tenant header needed
async function portalFetch<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const token = usePortalAuthStore.getState().token
  const res = await fetch(path, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(options.headers as Record<string, string> | undefined),
    },
  })
  if (!res.ok) {
    const body = await res.text()
    throw new Error(body || res.statusText)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

// ─── Auth ───────────────────────────────────────────────────────────────────

export interface PortalLoginResponse {
  token: string
  adminId: string
  email: string
  firstName: string
  lastName: string
}

export async function portalLogin(email: string, password: string): Promise<PortalLoginResponse> {
  return portalFetch<PortalLoginResponse>('/api/v1/portal/auth/login', {
    method: 'POST',
    body: JSON.stringify({ email, password }),
  })
}

export async function portalBootstrap(
  firstName: string,
  lastName: string,
  email: string,
  password: string,
): Promise<PortalLoginResponse> {
  return portalFetch<PortalLoginResponse>('/api/v1/portal/auth/bootstrap', {
    method: 'POST',
    body: JSON.stringify({ firstName, lastName, email, password }),
  })
}

// ─── Tenants ────────────────────────────────────────────────────────────────

export interface TenantFeatureFlags {
  telephony: boolean
  omsBuiltIn: boolean
  shopifyAdapter: boolean
  tenantChat: boolean
}

export interface TenantSettings {
  dateFormat: string
  timeFormat: string
  supportEmail: string | null
  billingEmail: string | null
  sessionTimeoutMinutes: number
  mfaRequirement: string
}

export interface TenantRecord {
  id: string
  name: string
  displayName: string | null
  logoUrl: string | null
  subdomain: string
  customDomain: string | null
  schemaName: string
  timezone: string
  isActive: boolean
  onboardingComplete: boolean
  trialExpiresAt: string | null
  billingContact: string | null
  inviteEmail: string | null
  featureFlags: TenantFeatureFlags
  settings: TenantSettings
  createdAt: string
}

export async function listTenants(): Promise<TenantRecord[]> {
  return portalFetch<TenantRecord[]>('/api/v1/portal/tenants')
}

export async function getTenant(id: string): Promise<TenantRecord> {
  return portalFetch<TenantRecord>(`/api/v1/portal/tenants/${id}`)
}

export async function provisionTenant(data: {
  name: string
  subdomain: string
  timezone: string
  featureFlags: TenantFeatureFlags
  inviteEmail?: string
}): Promise<TenantRecord> {
  return portalFetch<TenantRecord>('/api/v1/portal/tenants', {
    method: 'POST',
    body: JSON.stringify(data),
  })
}

export async function updateTenant(
  id: string,
  data: { billingContact?: string; customDomain?: string; inviteEmail?: string; trialExpiresAt?: string | null },
): Promise<TenantRecord> {
  return portalFetch<TenantRecord>(`/api/v1/portal/tenants/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(data),
  })
}

export async function updateFeatureFlags(id: string, flags: TenantFeatureFlags): Promise<TenantRecord> {
  return portalFetch<TenantRecord>(`/api/v1/portal/tenants/${id}/feature-flags`, {
    method: 'PATCH',
    body: JSON.stringify(flags),
  })
}

export async function activateTenant(id: string): Promise<TenantRecord> {
  return portalFetch<TenantRecord>(`/api/v1/portal/tenants/${id}/activate`, { method: 'POST' })
}

export async function deactivateTenant(id: string): Promise<TenantRecord> {
  return portalFetch<TenantRecord>(`/api/v1/portal/tenants/${id}/deactivate`, { method: 'POST' })
}

export async function resendTenantInvite(id: string): Promise<void> {
  return portalFetch<void>(`/api/v1/portal/tenants/${id}/resend-invite`, { method: 'POST' })
}

export async function resetTenantOnboarding(id: string): Promise<TenantRecord> {
  return portalFetch<TenantRecord>(`/api/v1/portal/tenants/${id}/reset-onboarding`, { method: 'POST' })
}

export async function inviteTenantAdmin(id: string, email: string): Promise<{ message: string }> {
  return portalFetch<{ message: string }>(`/api/v1/portal/tenants/${id}/invite-admin`, {
    method: 'POST',
    body: JSON.stringify({ email }),
  })
}

export interface TenantAgentRecord {
  id: string
  firstName: string
  lastName: string
  email: string
  role: string
  isActive: boolean
  lastLoginAt: string | null
}

export async function listTenantAgents(tenantId: string): Promise<TenantAgentRecord[]> {
  return portalFetch<TenantAgentRecord[]>(`/api/v1/portal/tenants/${tenantId}/agents`)
}

export async function resetTenantAgentPassword(tenantId: string, agentId: string, newPassword: string): Promise<void> {
  return portalFetch<void>(`/api/v1/portal/tenants/${tenantId}/agents/${agentId}/reset-password`, {
    method: 'POST',
    body: JSON.stringify({ newPassword }),
  })
}
