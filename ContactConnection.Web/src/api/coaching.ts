import { api } from './client'

/** In-call coaching notes (S183): supervisor → agent, pinned in the agent's portal until they press "Got it". */
export interface CoachingNote {
  id: string
  agentId: string
  fromId: string
  fromName: string
  text: string
  callRecordId: string | null
  createdAt: string
  seenAt: string | null
  acknowledgedAt: string | null
  retractedAt: string | null
  status: 'sent' | 'seen' | 'acknowledged' | 'retracted'
}

export const COACHING_MAX = 500

export const coachingApi = {
  send: (agentId: string, text: string) => api.post<CoachingNote>('/api/v1/coaching-notes', { agentId, text }),
  forAgent: (agentId: string) => api.get<CoachingNote[]>(`/api/v1/coaching-notes?agentId=${agentId}`),
  forCall: (callRecordId: string) => api.get<CoachingNote[]>(`/api/v1/coaching-notes/call/${callRecordId}`),
  mine: () => api.get<CoachingNote[]>('/api/v1/coaching-notes/mine'),
  seen: (id: string) => api.post<CoachingNote>(`/api/v1/coaching-notes/${id}/seen`),
  acknowledge: (id: string) => api.post<CoachingNote>(`/api/v1/coaching-notes/${id}/acknowledge`),
  retract: (id: string) => api.post<CoachingNote>(`/api/v1/coaching-notes/${id}/retract`),
}

export const COACHING_STATUS_LABEL: Record<CoachingNote['status'], string> = {
  sent: 'Sent', seen: 'Seen', acknowledged: 'Got it', retracted: 'Taken back',
}
