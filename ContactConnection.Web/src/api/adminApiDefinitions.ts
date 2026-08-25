import { api } from './client'
import type { EntityVersionSummary } from './versioning'

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
  /** Outbound requests/minute allowed against this definition, or null for unlimited. See
   * API_HARDENING_CHECKLIST.md Tier 2 — shared across every tenant using this definition when
   * it's a Portal (platform-default) definition, so this is what protects a shared quota. */
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

export function listAdminApiDefinitions(): Promise<ApiDefinitionRecord[]> {
  return api.get<ApiDefinitionRecord[]>('/api/v1/admin/api-definitions')
}

export function getAdminApiDefinition(id: string): Promise<ApiDefinitionRecord> {
  return api.get<ApiDefinitionRecord>(`/api/v1/admin/api-definitions/${id}`)
}

export function createAdminApiDefinition(data: CreateApiDefinitionData): Promise<ApiDefinitionRecord> {
  return api.post<ApiDefinitionRecord>('/api/v1/admin/api-definitions', {
    ...data,
    authConfig: data.authConfig ?? JSON.stringify({ type: 'none' }),
  })
}

export function updateAdminApiDefinition(id: string, data: UpdateApiDefinitionData): Promise<ApiDefinitionRecord> {
  return api.put<ApiDefinitionRecord>(`/api/v1/admin/api-definitions/${id}`, data)
}

export function activateAdminApiDefinition(id: string): Promise<ApiDefinitionRecord> {
  return api.post<ApiDefinitionRecord>(`/api/v1/admin/api-definitions/${id}/activate`, {})
}

export function deactivateAdminApiDefinition(id: string): Promise<ApiDefinitionRecord> {
  return api.post<ApiDefinitionRecord>(`/api/v1/admin/api-definitions/${id}/deactivate`, {})
}

export function deleteAdminApiDefinition(id: string): Promise<void> {
  return api.delete<void>(`/api/v1/admin/api-definitions/${id}`)
}

export function listAdminApiDefinitionVersions(id: string): Promise<EntityVersionSummary[]> {
  return api.get<EntityVersionSummary[]>(`/api/v1/admin/api-definitions/${id}/versions`)
}

export function revertAdminApiDefinition(id: string, versionNumber: number): Promise<ApiDefinitionRecord> {
  return api.post<ApiDefinitionRecord>(`/api/v1/admin/api-definitions/${id}/versions/${versionNumber}/revert`, {})
}

// ─── Admin API Endpoints ─────────────────────────────────────────────────────

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

export function listAdminApiEndpoints(definitionId: string): Promise<ApiEndpointRecord[]> {
  return api.get<ApiEndpointRecord[]>(`/api/v1/admin/api-definitions/${definitionId}/endpoints`)
}

export function createAdminApiEndpoint(definitionId: string, data: CreateApiEndpointData): Promise<ApiEndpointRecord> {
  return api.post<ApiEndpointRecord>(`/api/v1/admin/api-definitions/${definitionId}/endpoints`, data)
}

export function updateAdminApiEndpoint(definitionId: string, endpointId: string, data: UpdateApiEndpointData): Promise<ApiEndpointRecord> {
  return api.put<ApiEndpointRecord>(`/api/v1/admin/api-definitions/${definitionId}/endpoints/${endpointId}`, data)
}

export function setPreferredAdminApiEndpoint(definitionId: string, endpointId: string): Promise<ApiEndpointRecord> {
  return api.post<ApiEndpointRecord>(`/api/v1/admin/api-definitions/${definitionId}/endpoints/${endpointId}/set-preferred`, {})
}

export function deleteAdminApiEndpoint(definitionId: string, endpointId: string): Promise<void> {
  return api.delete<void>(`/api/v1/admin/api-definitions/${definitionId}/endpoints/${endpointId}`)
}

export function listAdminApiEndpointVersions(definitionId: string, endpointId: string): Promise<EntityVersionSummary[]> {
  return api.get<EntityVersionSummary[]>(`/api/v1/admin/api-definitions/${definitionId}/endpoints/${endpointId}/versions`)
}

export function revertAdminApiEndpoint(definitionId: string, endpointId: string, versionNumber: number): Promise<ApiEndpointRecord> {
  return api.post<ApiEndpointRecord>(`/api/v1/admin/api-definitions/${definitionId}/endpoints/${endpointId}/versions/${versionNumber}/revert`, {})
}

// ─── Inbound webhooks — see API_HARDENING_CHECKLIST.md Tier 2 ──────────────────

export interface WebhookConfig {
  id: string
  tenantApiEndpointId: string
  path: string
  tenantSubdomain: string
  signatureHeaderName: string
  signatureAlgorithm: string
  includeTimestamp: boolean
  timestampToleranceSeconds: number
  isActive: boolean
  createdAt: string
  updatedAt: string | null
  /** Only populated by enable/regenerate-secret responses — shown to the admin once and never
   * re-fetchable afterward. */
  secret: string | null
}

export interface UpdateWebhookConfigData {
  signatureHeaderName?: string
  signatureAlgorithm?: string
  includeTimestamp?: boolean
  timestampToleranceSeconds?: number
  isActive?: boolean
}

export interface WebhookEventRecord {
  id: string
  receivedAt: string
  signatureValid: boolean
  processingStatus: 'received' | 'processed' | 'duplicate' | 'rejected' | 'failed'
  processingError: string | null
  outcomeKey: string | null
  processedAt: string | null
}

const webhookBase = (definitionId: string, endpointId: string) =>
  `/api/v1/admin/api-definitions/${definitionId}/endpoints/${endpointId}/webhook`

export function getAdminWebhook(definitionId: string, endpointId: string): Promise<WebhookConfig> {
  return api.get<WebhookConfig>(webhookBase(definitionId, endpointId))
}

export function enableAdminWebhook(definitionId: string, endpointId: string): Promise<WebhookConfig> {
  return api.post<WebhookConfig>(webhookBase(definitionId, endpointId), {})
}

export function updateAdminWebhook(definitionId: string, endpointId: string, data: UpdateWebhookConfigData): Promise<WebhookConfig> {
  return api.patch<WebhookConfig>(webhookBase(definitionId, endpointId), data)
}

export function regenerateAdminWebhookSecret(definitionId: string, endpointId: string): Promise<WebhookConfig> {
  return api.post<WebhookConfig>(`${webhookBase(definitionId, endpointId)}/regenerate-secret`, {})
}

export function regenerateAdminWebhookToken(definitionId: string, endpointId: string): Promise<WebhookConfig> {
  return api.post<WebhookConfig>(`${webhookBase(definitionId, endpointId)}/regenerate-token`, {})
}

export function disableAdminWebhook(definitionId: string, endpointId: string): Promise<void> {
  return api.delete<void>(webhookBase(definitionId, endpointId))
}

export function listAdminWebhookEvents(definitionId: string, endpointId: string, take = 50): Promise<WebhookEventRecord[]> {
  return api.get<WebhookEventRecord[]>(`${webhookBase(definitionId, endpointId)}/events?take=${take}`)
}

// ─── Admin Webhooks dashboard (tenant-wide list, across every Definition/Endpoint) ────────────

export interface AdminWebhookSummary {
  webhookEndpointId: string
  definitionId: string
  definitionName: string
  endpointId: string
  endpointName: string
  endpointPath: string
  url: string
  isActive: boolean
  createdAt: string
  updatedAt: string | null
  lastEventAt: string | null
  lastEventStatus: WebhookEventRecord['processingStatus'] | null
}

export function listAdminWebhooks(): Promise<AdminWebhookSummary[]> {
  return api.get<AdminWebhookSummary[]>('/api/v1/admin/webhooks')
}

// ─── Tenant API Preferences ──────────────────────────────────────────────────

export type ApiPreferenceSource = 'portal' | 'tenant'

export interface TenantApiPreferenceRecord {
  id: string
  apiSubType: string
  source: ApiPreferenceSource
  endpointId: string
  settingsJson: string | null
  createdAt: string
  updatedAt: string | null
}

export interface AvailableEndpointsResult {
  subType: string
  tenantPreference: { source: ApiPreferenceSource; endpointId: string; settingsJson: string | null } | null
  portalEndpoints: AvailableEndpointItem[]
  tenantEndpoints: AvailableEndpointItem[]
}

export interface AvailableEndpointItem {
  source: ApiPreferenceSource
  definitionId: string
  definitionName: string | null
  definitionProvider: string | null
  id: string
  apiSubType: string
  name: string
  path: string
  isPreferred: boolean
  isActive: boolean
  isTenantSelected: boolean
}

export function listAdminApiPreferences(): Promise<TenantApiPreferenceRecord[]> {
  return api.get<TenantApiPreferenceRecord[]>('/api/v1/admin/api-preferences')
}

export function setAdminApiPreference(
  subType: string,
  source: ApiPreferenceSource,
  endpointId: string,
  settingsJson?: string,
): Promise<TenantApiPreferenceRecord> {
  return api.put<TenantApiPreferenceRecord>(`/api/v1/admin/api-preferences/${subType}`, { source, endpointId, settingsJson })
}

export function deleteAdminApiPreference(subType: string): Promise<void> {
  return api.delete<void>(`/api/v1/admin/api-preferences/${subType}`)
}

export function getAvailableEndpoints(subType: string): Promise<AvailableEndpointsResult> {
  return api.get<AvailableEndpointsResult>(`/api/v1/admin/available-endpoints/${subType}`)
}

export interface TtsProviderInfo {
  key: string
  requiredCredentialFields: string[]
}

export function listAdminTtsProviders(): Promise<TtsProviderInfo[]> {
  return api.get<TtsProviderInfo[]>('/api/v1/admin/tts-providers')
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

export function testAdminEndpoint(definitionId: string, payload: EndpointTestPayload): Promise<EndpointTestResult> {
  return api.post<EndpointTestResult>(`/api/v1/admin/api-definitions/${definitionId}/endpoints/test`, payload)
}

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

export function testAdminAuth(authConfig: string): Promise<AuthTestResult> {
  return api.post<AuthTestResult>('/api/v1/admin/api-definitions/test-auth', { authConfig })
}
