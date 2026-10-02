import { api } from './client'
import { useAuthStore } from '../stores/authStore'
import { getSubdomainFromHostname } from '../utils/subdomain'

// Commissions (S171): rules per campaign (client = default), pay periods, reports, an agent's own earnings.

export type CommissionKind = 'percent_of_order' | 'flat_per_order' | 'flat_per_product' | 'flat_per_field'

export const KIND_LABELS: Record<CommissionKind, string> = {
  percent_of_order: '% of order',
  flat_per_order: '$ per order',
  flat_per_product: '$ per unit of a product',
  flat_per_field: '$ when a custom field has a value',
}

export interface CommissionRule {
  id: string
  clientId: string | null
  campaignId: string | null
  name: string
  kind: CommissionKind
  amount: number
  productId: string | null
  productLabel: string | null
  fieldName: string | null
  fieldValue: string | null
  tierLabel: string | null
  isActive: boolean
  /** Tenant-local "yyyy-MM-ddTHH:mm"; null = open. */
  effectiveFrom: string | null
  effectiveUntil: string | null
}

export interface RuleInput {
  clientId?: string | null
  campaignId?: string | null
  name: string
  kind: CommissionKind
  amount: number
  productId?: string | null
  fieldName?: string | null
  fieldValue?: string | null
  tierLabel?: string | null
  isActive: boolean
  effectiveFrom?: string | null
  effectiveUntil?: string | null
}

/** Recalculate past calls: calls that started in [from, to), tenant-local "yyyy-MM-ddTHH:mm". */
export interface RecalcInput {
  clientId?: string | null
  campaignId?: string | null
  agentId?: string | null
  from: string
  to: string
  postTo?: 'current' | 'call_date'
  reason?: string
}

export interface RecalcPreview {
  calls: number
  changedCalls: number
  current: number
  recalculated: number
  difference: number
  agents: { agentId: string; agentName: string; current: number; recalculated: number; difference: number; changedCalls: number }[]
}

export interface RecalcBatch {
  id: string
  status: 'pending' | 'running' | 'completed' | 'failed'
  postTo: 'current' | 'call_date'
  reason: string
  requestedBy: string | null
  totalCalls: number
  processedCalls: number
  changedCalls: number
  difference: number
  error: string | null
  createdAt: string
  completedAt: string | null
  from: string
  to: string
  scope: string
}

export interface Period { start: string; end: string; label: string }

export interface CommissionSettings {
  frequency: 'weekly' | 'biweekly' | 'semimonthly' | 'monthly'
  start: string
  current: Period
  timezone: string
}

export interface AgentTotal { agentId: string; agentName: string; earned: number; reversed: number; total: number; calls: number }
export interface CommissionReport { period: Period; agents: AgentTotal[]; total: number }

export interface CommissionEntryRow {
  id: string
  agentId: string
  agentName: string
  callRecordId: string
  orderNumber: string | null
  client: string
  campaign: string
  entryType: 'earned' | 'reversal'
  ruleName: string
  description: string
  amount: number
  note: string | null
  occurredAt: string
  date: string
}

export interface MyCommissions { period: Period; today: number; total: number; entries: CommissionEntryRow[] }

export interface CallCommissions {
  orderSubmittedAt: string | null
  commissionsReversedAt: string | null
  commissionsReversedReason: string | null
  total: number
  entries: { id: string; entryType: 'earned' | 'reversal'; ruleName: string; description: string; amount: number; isReversed: boolean; note: string | null; occurredAt: string; agentName: string }[]
}

/** period = 'current' | 'previous', or a date inside the wanted period. */
export interface PeriodQuery { period?: 'current' | 'previous'; start?: string; agentId?: string }

const qs = (q: PeriodQuery) =>
  Object.entries(q).filter(([, v]) => v).map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`).join('&')

export const commissionsApi = {
  rules: (scope: { clientId?: string; campaignId?: string }) => api.get<CommissionRule[]>(`/api/v1/commission-rules?${qs(scope as PeriodQuery)}`),
  createRule: (input: RuleInput) => api.post<CommissionRule>('/api/v1/commission-rules', input),
  updateRule: (id: string, input: RuleInput) => api.put<CommissionRule>(`/api/v1/commission-rules/${id}`, input),
  deleteRule: (id: string) => api.delete<void>(`/api/v1/commission-rules/${id}`),

  previewRecalc: (input: RecalcInput) => api.post<RecalcPreview>('/api/v1/commissions/recalc/preview', input),
  startRecalc: (input: RecalcInput) => api.post<{ id: string }>('/api/v1/commissions/recalc', input),
  recalcs: () => api.get<RecalcBatch[]>('/api/v1/commissions/recalc'),

  settings: () => api.get<CommissionSettings>('/api/v1/commission-settings'),
  saveSettings: (frequency: string, start: string) => api.put<CommissionSettings>('/api/v1/commission-settings', { frequency, start }),

  report: (q: PeriodQuery) => api.get<CommissionReport>(`/api/v1/commissions/report?${qs(q)}`),
  entries: (q: PeriodQuery) => api.get<{ period: Period; entries: CommissionEntryRow[] }>(`/api/v1/commissions/entries?${qs(q)}`),
  mine: (period: 'current' | 'previous') => api.get<MyCommissions>(`/api/v1/commissions/mine?period=${period}`),

  forCall: (callId: string) => api.get<CallCommissions>(`/api/v1/call-review/calls/${callId}/commissions`),
  reverseCall: (callId: string, reason: string) => api.post<void>(`/api/v1/call-review/calls/${callId}/commissions/reverse`, { reason }),
  restoreCall: (callId: string) => api.post<void>(`/api/v1/call-review/calls/${callId}/commissions/restore`),

  /** Fetches the payroll CSV and hands it to the browser as a download. */
  async downloadCsv(q: PeriodQuery) {
    const { token, tenantSubdomain } = useAuthStore.getState()
    const subdomain = getSubdomainFromHostname() ?? tenantSubdomain
    const res = await fetch(`/api/v1/commissions/export.csv?${qs(q)}`, {
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(subdomain ? { 'X-Tenant-Subdomain': subdomain } : {}),
      },
    })
    if (!res.ok) throw new Error((await res.text()) || res.statusText)
    const name = /filename="?([^";]+)"?/.exec(res.headers.get('Content-Disposition') ?? '')?.[1] ?? 'commissions.csv'
    const url = URL.createObjectURL(await res.blob())
    const a = document.createElement('a')
    a.href = url
    a.download = name
    a.click()
    URL.revokeObjectURL(url)
  },
}

export const money = (n: number) =>
  (n < 0 ? '−$' : '$') + Math.abs(n).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
