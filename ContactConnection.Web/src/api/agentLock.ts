import { api } from './client'

// Supervisor lock on an agent (S166) — set from Call Records "Finalize", lifted from Users or the
// dashboard's Agent List. See AgentLockEndpoints.cs.
export interface AgentLockState {
  id: string
  statusLocked: boolean
  signInLocked: boolean
  statusLockedAt: string | null
  statusLockedByName: string | null
  statusLockReason: string | null
}

export const agentLockApi = {
  lock: (agentId: string, signIn: boolean, reason: string | null) =>
    api.post<AgentLockState>(`/api/v1/agents/${agentId}/lock`, { signIn, reason }),
  unlock: (agentId: string) => api.post<AgentLockState>(`/api/v1/agents/${agentId}/unlock`),
}

/** Key the login page reads once to explain a forced sign-out. */
export const LOGIN_NOTICE_KEY = 'cc_login_notice'
