import { api } from './client'

export interface CredentialSummary {
  keyName: string
  updatedOn: string | null
}

export function listAdminCredentials(): Promise<CredentialSummary[]> {
  return api.get<CredentialSummary[]>('/api/v1/admin/credentials')
}

export function setAdminCredential(keyName: string, value: string): Promise<void> {
  return api.put<void>(`/api/v1/admin/credentials/${keyName}`, { value })
}

export function deleteAdminCredential(keyName: string): Promise<void> {
  return api.delete<void>(`/api/v1/admin/credentials/${keyName}`)
}
