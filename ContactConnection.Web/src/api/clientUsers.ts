import { api } from './client'

/** Tenant admin side of client users (S181) — people outside the tenant who view client dashboards. */
export interface ClientUser {
  id: string
  email: string
  firstName: string
  lastName: string
  isActive: boolean
  canPlayRecordings: boolean
  mfaEnabled: boolean
  hasPassword: boolean
  linkPending: boolean
  linkExpiresAt: string | null
  lastLoginAt: string | null
  createdAt: string
  dashboardIds: string[]
}

export interface ClientUserInput {
  email?: string
  firstName: string
  lastName: string
  isActive?: boolean
  canPlayRecordings: boolean
  dashboardIds: string[]
}

export interface ClientUserAudit {
  id: string
  action: string
  detail: string | null
  ipAddress: string | null
  at: string
  clientUserId: string | null
  clientUser: string | null
  byAgent: string | null
}

const base = '/api/v1/admin/client-users'
export const clientUsersApi = {
  list: () => api.get<ClientUser[]>(base),
  invite: (v: ClientUserInput) => api.post<{ user: ClientUser; emailSent: boolean }>(base, v),
  update: (id: string, v: ClientUserInput) => api.patch<ClientUser>(`${base}/${id}`, v),
  sendLink: (id: string) => api.post<{ user: ClientUser; emailSent: boolean }>(`${base}/${id}/send-link`),
  resetMfa: (id: string) => api.post<ClientUser>(`${base}/${id}/reset-mfa`),
  remove: (id: string) => api.delete<void>(`${base}/${id}`),
  audit: (clientUserId?: string) => api.get<ClientUserAudit[]>(`${base}/audit${clientUserId ? `?clientUserId=${clientUserId}` : ''}`),
}
