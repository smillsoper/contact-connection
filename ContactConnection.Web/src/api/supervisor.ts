import { api } from './client'

// Supervisor tools on an agent's live call (S167) — see SupervisorEndpoints.cs.
// Monitor/Coach need supervisor.monitor; Barge/Take Over need supervisor.override.
export type MonitorMode = 'listen' | 'coach' | 'barge'

export interface MonitorState {
  supervisorId: string
  agentId: string
  agentName: string
  callRecordId: string
  legUuid: string
  mode: MonitorMode
  startedAt: string
}

export const MONITOR_MODE_LABEL: Record<MonitorMode, string> = {
  listen: 'Monitoring',
  coach: 'Coaching',
  barge: 'Barged in',
}

export const supervisorApi = {
  me: () => api.get<{ agentId: string; registered: boolean }>('/api/v1/supervisor/me'),
  current: () => api.get<MonitorState | undefined>('/api/v1/supervisor/monitor'),
  start: (agentId: string, mode: MonitorMode) => api.post<MonitorState>('/api/v1/supervisor/monitor', { agentId, mode }),
  setMode: (mode: MonitorMode) => api.put<MonitorState>('/api/v1/supervisor/monitor', { mode }),
  stop: () => api.delete<void>('/api/v1/supervisor/monitor'),
  callAgent: (agentId: string) => api.post<{ legUuid: string; agentName: string }>('/api/v1/supervisor/call-agent', { agentId }),
  takeOver: (agentId: string) =>
    api.post<{ callRecordId: string; phone: boolean; scriptsMoved: number }>('/api/v1/supervisor/take-over', { agentId }),
}
