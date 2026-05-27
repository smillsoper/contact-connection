import { api } from './client'

export interface AgentRecord {
  id: string
  firstName: string
  lastName: string
  email: string
  role: string
  isActive: boolean
  createdAt: string
  lastLoginAt: string | null
}

export function listAdminAgents(): Promise<AgentRecord[]> {
  return api.get<AgentRecord[]>('/api/v1/admin/agents')
}

export function resetAgentPassword(id: string, newPassword: string): Promise<AgentRecord> {
  return api.post<AgentRecord>(`/api/v1/admin/agents/${id}/reset-password`, { newPassword })
}

export function updateAgent(id: string, data: { role?: string; isActive?: boolean }): Promise<AgentRecord> {
  return api.patch<AgentRecord>(`/api/v1/admin/agents/${id}`, data)
}

export function inviteAdmin(email: string): Promise<{ message: string }> {
  return api.post<{ message: string }>('/api/v1/admin/agents/invite', { email })
}
