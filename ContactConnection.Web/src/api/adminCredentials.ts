import { api } from './client'
import type { CredentialAuditEntrySummary } from './credentialAudit'

export interface CredentialSummary {
  keyName: string
  updatedOn: string | null
  expiresOn: string | null
}

export function listAdminCredentials(): Promise<CredentialSummary[]> {
  return api.get<CredentialSummary[]>('/api/v1/admin/credentials')
}

export function setAdminCredential(keyName: string, value: string, expiresOn?: string | null): Promise<void> {
  return api.put<void>(`/api/v1/admin/credentials/${keyName}`, { value, expiresOn: expiresOn || null })
}

export function deleteAdminCredential(keyName: string): Promise<void> {
  return api.delete<void>(`/api/v1/admin/credentials/${keyName}`)
}

export function listAdminCredentialAudit(keyName: string): Promise<CredentialAuditEntrySummary[]> {
  return api.get<CredentialAuditEntrySummary[]>(`/api/v1/admin/credentials/${keyName}/audit`)
}
