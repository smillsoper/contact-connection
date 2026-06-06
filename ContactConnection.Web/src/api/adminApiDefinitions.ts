import { api } from './client'

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

// ─── Tenant API Preferences ──────────────────────────────────────────────────

export interface TenantApiPreferenceRecord {
  id: string
  apiSubType: string
  portalApiEndpointId: string
  createdAt: string
  updatedAt: string | null
}

export interface AvailableEndpointsResult {
  subType: string
  tenantPreference: { portalApiEndpointId: string } | null
  portalEndpoints: AvailableEndpointItem[]
  tenantEndpoints: AvailableEndpointItem[]
}

export interface AvailableEndpointItem {
  source: 'portal' | 'tenant'
  definitionId: string
  definitionName: string | null
  definitionProvider: string | null
  id: string
  apiSubType: string
  name: string
  path: string
  isPreferred: boolean
  isActive: boolean
  isTenantSelected?: boolean
}

export function listAdminApiPreferences(): Promise<TenantApiPreferenceRecord[]> {
  return api.get<TenantApiPreferenceRecord[]>('/api/v1/admin/api-preferences')
}

export function setAdminApiPreference(subType: string, portalApiEndpointId: string): Promise<TenantApiPreferenceRecord> {
  return api.put<TenantApiPreferenceRecord>(`/api/v1/admin/api-preferences/${subType}`, { portalApiEndpointId })
}

export function deleteAdminApiPreference(subType: string): Promise<void> {
  return api.delete<void>(`/api/v1/admin/api-preferences/${subType}`)
}

export function getAvailableEndpoints(subType: string): Promise<AvailableEndpointsResult> {
  return api.get<AvailableEndpointsResult>(`/api/v1/admin/available-endpoints/${subType}`)
}

export interface EndpointTestPayload {
  path: string
  httpMethod?: string
  queryParams?: string
  headers?: string
  requestBodyTemplate?: string
  namespace: string
  testData: Record<string, string>
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
