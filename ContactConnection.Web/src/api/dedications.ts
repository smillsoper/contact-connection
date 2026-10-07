import { api } from './client'

/** Agent dedications (S183): a supervisor dedicates an agent to campaigns — while active, the agent takes calls ONLY from
 * those campaigns (even ones they aren't assigned to); their other assignments resume when it ends. */

export type DedicationMode = 'duration' | 'until' | 'schedule'

/** Days: 0 = Sunday … 6 = Saturday. Times "HH:mm" in the tenant's time zone; an end before the start runs past midnight. */
export interface DedicationWindow { days: number[]; start: string; end: string }

export interface Dedication {
  id: string
  agentId: string
  mode: DedicationMode
  campaigns: { id: string; name: string }[]
  startsAt: string
  endsAt: string | null
  windows: DedicationWindow[]
  note: string | null
  createdByName: string
  createdAt: string
  activeNow: boolean
  nextChangeAt: string | null
  summary: string
}

export interface CreateDedication {
  agentId: string
  campaignIds: string[]
  mode: DedicationMode
  minutes?: number
  endsAt?: string
  windows?: DedicationWindow[]
  /** Schedule: the last day it runs (yyyy-MM-dd, the whole day in the tenant's time zone). */
  lastDay?: string
  note?: string
}

export interface MyDedication { active: boolean; campaigns: string[]; summary: string; nextChangeAt: string | null }
export interface MyQueueRow { callRecordId: string; campaign: string; queuedSince: string | null; isCallback: boolean }

export const dedicationsApi = {
  list: (agentId?: string) => api.get<Dedication[]>(`/api/v1/agent-dedications${agentId ? `?agentId=${agentId}` : ''}`),
  create: (req: CreateDedication) => api.post<Dedication>('/api/v1/agent-dedications', req),
  end: (id: string) => api.post<void>(`/api/v1/agent-dedications/${id}/end`),
  mine: () => api.get<MyDedication>('/api/v1/agent-dedications/mine'),
  myQueue: () => api.get<MyQueueRow[]>('/api/v1/agent/my-queue'),
}
