import { api } from './client'

export interface ScheduledCallback {
  id: string
  callRecordId: string
  campaignId: string
  callbackNumber: string
  dnis: string | null
  callerIdOverride: string | null
  targetFlowId: string | null
  targetCampaignId: string | null
  status: 'scheduled' | 'attempted' | 'completed' | 'abandoned' | 'expired' | 'cancelled'
  requestedAt: string
  scheduledFor: string
  expiresAt: string
  attemptCount: number
  maxAttempts: number
  lastAttemptAt: string | null
  outboundCallRecordId: string | null
  completedAt: string | null
  abandonedAt: string | null
  expiredAt: string | null
  cancelledAt: string | null
  detail: string | null
  createdAt: string
  updatedAt: string
}

export interface ScheduledCallbackListParams {
  status?: string
  campaignId?: string
  clientId?: string
  limit?: number
}

function query(p: ScheduledCallbackListParams): string {
  const parts: string[] = []
  if (p.status) parts.push(`status=${encodeURIComponent(p.status)}`)
  if (p.campaignId) parts.push(`campaignId=${p.campaignId}`)
  if (p.clientId) parts.push(`clientId=${p.clientId}`)
  if (p.limit) parts.push(`limit=${p.limit}`)
  return parts.length ? `?${parts.join('&')}` : ''
}

export const scheduledCallbacksApi = {
  list: (params: ScheduledCallbackListParams = {}) =>
    api.get<ScheduledCallback[]>(`/api/v1/scheduled-callbacks${query(params)}`),

  cancel: (id: string, reason?: string) =>
    // Always send an object body — the endpoint's minimal-API body binding rejects a
    // completely empty request body even though the DTO is nullable.
    api.post<ScheduledCallback>(`/api/v1/scheduled-callbacks/${id}/cancel`, { reason: reason ?? null }),
}
