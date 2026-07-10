import { api } from './client'

export type CaptureMode = 'count' | 'duration'

export interface StartTraceFilters {
  campaignId?: string
  flowId?: string
  dnis?: string
  ani?: string
  captureMode: CaptureMode
  captureValue: number
}

export interface StartTraceResult {
  subscriptionId: string
  effectiveCaptureMode: CaptureMode
  effectiveCaptureValue: number
}

export interface CallTraceStep {
  callRecordId: string
  sequence: number
  engine: 'telephony' | 'crm'
  nodeId: string
  nodeType: string
  label: string | null
  enteredAt: string
  detail: string | null
  transitionTaken: string | null
  exitReason: string | null
  nextNodeId: string | null
}

export interface CallTraceCallSummary {
  callRecordId: string
  campaignId: string | null
  flowId: string | null
  dnis: string | null
  ani: string | null
  startedAt: string
}

export const callTracesApi = {
  start: (filters: StartTraceFilters) =>
    api.post<StartTraceResult>('/api/v1/call-traces', {
      campaignId: filters.campaignId || null,
      flowId: filters.flowId || null,
      dnis: filters.dnis?.trim() || null,
      ani: filters.ani?.trim() || null,
      captureMode: filters.captureMode,
      captureValue: filters.captureValue,
    }),

  stop: (subscriptionId: string) =>
    api.post<void>(`/api/v1/call-traces/${subscriptionId}/stop`),

  getTimeline: (callRecordId: string) =>
    api.get<CallTraceStep[]>(`/api/v1/call-traces/${callRecordId}`),

  search: (filters: { campaignId?: string; flowId?: string; dnis?: string; ani?: string; limit?: number }) => {
    const params = new URLSearchParams()
    if (filters.campaignId) params.set('campaignId', filters.campaignId)
    if (filters.flowId) params.set('flowId', filters.flowId)
    if (filters.dnis) params.set('dnis', filters.dnis)
    if (filters.ani) params.set('ani', filters.ani)
    if (filters.limit) params.set('limit', String(filters.limit))
    return api.get<CallTraceCallSummary[]>(`/api/v1/call-traces/search?${params.toString()}`)
  },
}
