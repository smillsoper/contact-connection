import { api } from './client'

export interface AgentRecord {
  id: string
  firstName: string
  lastName: string
  email: string
  role: string
  roleId: string | null
  roleName: string | null
  isActive: boolean
  createdAt: string
  lastLoginAt: string | null
  timezone: string | null
  sipExtension: string | null
}

export function listAdminAgents(): Promise<AgentRecord[]> {
  return api.get<AgentRecord[]>('/api/v1/admin/agents')
}

export function resetAgentPassword(id: string, newPassword: string): Promise<AgentRecord> {
  return api.post<AgentRecord>(`/api/v1/admin/agents/${id}/reset-password`, { newPassword })
}

export function updateAgent(id: string, data: { role?: string; roleId?: string | null; isActive?: boolean; timezone?: string | null }): Promise<AgentRecord> {
  return api.patch<AgentRecord>(`/api/v1/admin/agents/${id}`, data)
}

export interface InviteResult {
  sent: number
  failed: string[]
  message: string
}

export function inviteUsers(emails: string, roleId: string): Promise<InviteResult> {
  return api.post<InviteResult>('/api/v1/admin/agents/invite', { emails, roleId })
}
