import { usePortalAuthStore } from '../stores/portalAuthStore'
import type { EntityVersionSummary } from './versioning'
import type { CredentialAuditEntrySummary } from './credentialAudit'

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
  roleName: string | null
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

// ─── Portal API Definitions ─────────────────────────────────────────────────

export interface ApiDefinitionRecord {
  id: string
  apiCategory: string
  provider: string | null
  name: string
  description: string | null
  httpMethod: string
  baseUrl: string
  timeoutSeconds: number
  headers: string
  queryParams: string
  requestBodyTemplate: string | null
  responseMapping: string
  authConfig: string
  isActive: boolean
  /** Outbound requests/minute allowed against this definition, or null for unlimited. Every
   * tenant using this Portal definition (a platform-default credential) shares this same budget —
   * see API_HARDENING_CHECKLIST.md Tier 2. */
  rateLimitPerMinute: number | null
  createdAt: string
  updatedAt: string | null
}

export interface CreateApiDefinitionData {
  apiCategory: string
  name: string
  httpMethod: string
  baseUrl: string
  description?: string
  provider?: string
  timeoutSeconds?: number
  authConfig?: string
  rateLimitPerMinute?: number
}

export interface UpdateApiDefinitionData {
  name: string
  httpMethod: string
  baseUrl: string
  apiCategory?: string
  description?: string
  provider?: string
  timeoutSeconds?: number
  headers?: string
  queryParams?: string
  requestBodyTemplate?: string
  responseMapping?: string
  authConfig?: string
  /** Omit to leave unchanged, 0 to clear back to unlimited, or a positive number to set a new
   * limit — same convention the backend uses. */
  rateLimitPerMinute?: number
}

export async function listPortalApiDefinitions(): Promise<ApiDefinitionRecord[]> {
  return portalFetch<ApiDefinitionRecord[]>('/api/v1/portal/api-definitions')
}

export async function getPortalApiDefinition(id: string): Promise<ApiDefinitionRecord> {
  return portalFetch<ApiDefinitionRecord>(`/api/v1/portal/api-definitions/${id}`)
}

interface TtsProviderInfo {
  key: string
  requiredCredentialFields: string[]
}

/** Live-registered ITtsStreamProvider keys (e.g. "azure", "elevenlabs") — the valid Provider
 *  values for a definition backing a TtsStreaming endpoint. See TtsProviderValidation. The
 *  backend returns full {key, requiredCredentialFields} objects (see TtsProvidersEndpoints and
 *  the Admin-side listAdminTtsProviders) — this caller only needs the keys, so it unwraps here
 *  rather than leaking the object shape into DetailApi.listTtsProviders (typed string[]). */
export async function getPortalTtsProviders(): Promise<string[]> {
  const providers = await portalFetch<TtsProviderInfo[]>('/api/v1/portal/tts-providers')
  return providers.map((p) => p.key)
}

export async function createPortalApiDefinition(data: CreateApiDefinitionData): Promise<ApiDefinitionRecord> {
  return portalFetch<ApiDefinitionRecord>('/api/v1/portal/api-definitions', {
    method: 'POST',
    body: JSON.stringify({ ...data, authConfig: data.authConfig ?? JSON.stringify({ type: 'none' }) }),
  })
}

export async function updatePortalApiDefinition(id: string, data: UpdateApiDefinitionData): Promise<ApiDefinitionRecord> {
  return portalFetch<ApiDefinitionRecord>(`/api/v1/portal/api-definitions/${id}`, {
    method: 'PUT',
    body: JSON.stringify(data),
  })
}

export async function activatePortalApiDefinition(id: string): Promise<ApiDefinitionRecord> {
  return portalFetch<ApiDefinitionRecord>(`/api/v1/portal/api-definitions/${id}/activate`, { method: 'POST' })
}

export async function deactivatePortalApiDefinition(id: string): Promise<ApiDefinitionRecord> {
  return portalFetch<ApiDefinitionRecord>(`/api/v1/portal/api-definitions/${id}/deactivate`, { method: 'POST' })
}

export async function deletePortalApiDefinition(id: string): Promise<void> {
  return portalFetch<void>(`/api/v1/portal/api-definitions/${id}`, { method: 'DELETE' })
}

export async function listPortalApiDefinitionVersions(id: string): Promise<EntityVersionSummary[]> {
  return portalFetch<EntityVersionSummary[]>(`/api/v1/portal/api-definitions/${id}/versions`)
}

export async function revertPortalApiDefinition(id: string, versionNumber: number): Promise<ApiDefinitionRecord> {
  return portalFetch<ApiDefinitionRecord>(`/api/v1/portal/api-definitions/${id}/versions/${versionNumber}/revert`, { method: 'POST' })
}

// ─── Portal Credentials ──────────────────────────────────────────────────────

export interface CredentialSummary {
  keyName: string
  updatedOn: string | null
  expiresOn: string | null
}

export async function listPortalCredentials(): Promise<CredentialSummary[]> {
  return portalFetch<CredentialSummary[]>('/api/v1/portal/credentials')
}

export async function setPortalCredential(keyName: string, value: string, expiresOn?: string | null): Promise<void> {
  return portalFetch<void>(`/api/v1/portal/credentials/${keyName}`, {
    method: 'PUT',
    body: JSON.stringify({ value, expiresOn: expiresOn || null }),
  })
}

export async function deletePortalCredential(keyName: string): Promise<void> {
  return portalFetch<void>(`/api/v1/portal/credentials/${keyName}`, { method: 'DELETE' })
}

export async function listPortalCredentialAudit(keyName: string): Promise<CredentialAuditEntrySummary[]> {
  return portalFetch<CredentialAuditEntrySummary[]>(`/api/v1/portal/credentials/${keyName}/audit`)
}

// ─── Portal API Endpoints ────────────────────────────────────────────────────

export interface ApiEndpointRecord {
  id: string
  definitionId: string
  apiSubType: string
  name: string
  description: string | null
  path: string
  httpMethod: string | null
  requestBodyTemplate: string | null
  queryParams: string
  headers: string
  responseMapping: string
  sortOrder: number
  isPreferred: boolean
  isActive: boolean
  isRetrySafe: boolean
  sensitiveResponseFields: string
  createdAt: string
  updatedAt: string | null
}

export interface CreateApiEndpointData {
  apiSubType: string
  name: string
  path: string
  httpMethod?: string
  description?: string
  sortOrder?: number
  requestBodyTemplate?: string
  queryParams?: string
  headers?: string
  responseMapping?: string
  isRetrySafe?: boolean
  sensitiveResponseFields?: string
}

export interface UpdateApiEndpointData {
  apiSubType?: string
  name: string
  path: string
  httpMethod?: string
  description?: string
  sortOrder?: number
  requestBodyTemplate?: string
  queryParams?: string
  headers?: string
  responseMapping?: string
  isRetrySafe?: boolean
  sensitiveResponseFields?: string
}

export async function listPortalApiEndpoints(definitionId: string): Promise<ApiEndpointRecord[]> {
  return portalFetch<ApiEndpointRecord[]>(`/api/v1/portal/api-definitions/${definitionId}/endpoints`)
}

export async function createPortalApiEndpoint(definitionId: string, data: CreateApiEndpointData): Promise<ApiEndpointRecord> {
  return portalFetch<ApiEndpointRecord>(`/api/v1/portal/api-definitions/${definitionId}/endpoints`, {
    method: 'POST',
    body: JSON.stringify(data),
  })
}

export async function updatePortalApiEndpoint(definitionId: string, endpointId: string, data: UpdateApiEndpointData): Promise<ApiEndpointRecord> {
  return portalFetch<ApiEndpointRecord>(`/api/v1/portal/api-definitions/${definitionId}/endpoints/${endpointId}`, {
    method: 'PUT',
    body: JSON.stringify(data),
  })
}

export async function setPreferredPortalApiEndpoint(definitionId: string, endpointId: string): Promise<ApiEndpointRecord> {
  return portalFetch<ApiEndpointRecord>(`/api/v1/portal/api-definitions/${definitionId}/endpoints/${endpointId}/set-preferred`, { method: 'POST' })
}

export async function listPortalApiEndpointVersions(definitionId: string, endpointId: string): Promise<EntityVersionSummary[]> {
  return portalFetch<EntityVersionSummary[]>(`/api/v1/portal/api-definitions/${definitionId}/endpoints/${endpointId}/versions`)
}

export async function revertPortalApiEndpoint(definitionId: string, endpointId: string, versionNumber: number): Promise<ApiEndpointRecord> {
  return portalFetch<ApiEndpointRecord>(`/api/v1/portal/api-definitions/${definitionId}/endpoints/${endpointId}/versions/${versionNumber}/revert`, { method: 'POST' })
}

export async function deletePortalApiEndpoint(definitionId: string, endpointId: string): Promise<void> {
  return portalFetch<void>(`/api/v1/portal/api-definitions/${definitionId}/endpoints/${endpointId}`, { method: 'DELETE' })
}

export interface EndpointTestPayload {
  path: string
  httpMethod?: string
  queryParams?: string
  headers?: string
  requestBodyTemplate?: string
  namespace: string
  testData: Record<string, string>
  sensitiveResponseFields?: string[]
}

export interface EndpointTestResult {
  success: boolean
  statusCode: number | null
  body: string | null
  responseHeaders: Record<string, string> | null
  resolvedUrl: string | null
  error: string | null
}

export async function testPortalEndpoint(definitionId: string, payload: EndpointTestPayload): Promise<EndpointTestResult> {
  return portalFetch<EndpointTestResult>(`/api/v1/portal/api-definitions/${definitionId}/endpoints/test`, {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

// ─── Auth Testing ────────────────────────────────────────────────────────────

export interface AuthTestResult {
  type: string
  success: boolean
  message?: string
  error?: string
  statusCode?: number
  rawResponse?: string
  credentials?: Array<{ key: string; found: boolean }>
  fieldMapping?: {
    token: { name: string; found: boolean; preview: string | null }
    tokenType: { name: string; found: boolean; value: string | null }
    expiresIn: { name: string; found: boolean; value: string | null }
  }
}

export async function testPortalAuth(authConfig: string): Promise<AuthTestResult> {
  return portalFetch<AuthTestResult>('/api/v1/portal/api-definitions/test-auth', {
    method: 'POST',
    body: JSON.stringify({ authConfig }),
  })
}

// ─── Maintenance ─────────────────────────────────────────────────────────────

export interface MigrateTenantsResult {
  migrated: number
  errors: string[]
}

// Applies any pending EF migrations to every tenant schema (idempotent — safe to run
// repeatedly). Fixes schema drift like a tenant missing migrations that were applied
// everywhere else. Backend returns 207 when some tenants error, which fetch treats as
// ok (in the 200-299 range), so both outcomes come back through the normal response path.
export async function migrateTenants(): Promise<MigrateTenantsResult> {
  return portalFetch<MigrateTenantsResult>('/api/v1/portal/maintenance/migrate-tenants', {
    method: 'POST',
  })
}
