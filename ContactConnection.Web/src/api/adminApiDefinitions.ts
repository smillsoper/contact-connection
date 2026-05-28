import { api } from './client'

export interface ApiDefinitionRecord {
  id: string
  apiType: string
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
  apiType: string
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
