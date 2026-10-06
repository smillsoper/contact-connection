import type { CallDetailData } from '../components/dashboard/CallDetailModal'
import { api } from './client'
import type { WidgetFilterConfig } from '../types/dashboard'

export interface AgentStateCounterData {
  total: number
  by_state: Record<string, number>
}

export interface AgentListRow {
  agent_id: string
  name: string
  state_code: string
  state_label: string
  since: string | null
  /** SIP softphone registered with FreeSWITCH — distinct from state_code (agent status). */
  registered: boolean
  registered_since: string | null
  /** CRM scripts the agent has open right now (on a phone call or not), one per call record. */
  live_calls: { call_record_id: string; flow_name: string | null; started_at: string }[]
  /** Bridged to a live caller right now — Monitor / Coach / Barge need one. */
  on_live_call?: boolean
  /** Supervisor lock (Call Records "Finalize"). */
  status_locked?: boolean
  sign_in_locked?: boolean
  lock_reason?: string | null
  locked_by?: string | null
}

export interface CampaignStateCountRow {
  campaign_id: string
  campaign_name: string
  pre_queue: number
  in_queue: number
  with_agent: number
  post_agent: number
}

export interface PendingQueueCallbackRow {
  call_record_id: string
  campaign_id: string
  caller_number: string
  callback_number: string | null
  queued_since: string | null
  attempts: number
  max_attempts: number
  reserved_agent_id: string | null
  retry_after: string | null
}

export interface QueuedCallRow {
  call_record_id: string
  campaign_id: string
  campaign_name: string
  caller_number: string
  queued_since: string | null
  is_queue_callback: boolean
  /** Caller is in an IVR menu / voicemail sub-dialog — not deliverable this moment. */
  in_menu: boolean
  /** "Only offer to this group" (e.g. Elite) — the group's name. */
  pinned_group: string | null
  pinned_group_id: string | null
  /** Nobody who could take this call is logged in — the manager-on-duty alert. */
  none_logged_in: boolean
  /** Routing tier the call is offered to right now; null = nobody available. */
  offer_tier: number | null
  offer_labels: string | null
  /** An exclusive window is holding the call for this tier. */
  held_for_tier: number | null
  offered_agents: number
}

export interface ServiceLevelThresholdData {
  met: number
  missed: number
  /** null when met + missed === 0 (no answered calls in the window yet). */
  percent_in_sl: number | null
}

function buildQuery(params: WidgetFilterConfig): string {
  const parts: string[] = []
  if (params.campaignId) parts.push(`campaignId=${params.campaignId}`)
  if (params.clientId) parts.push(`clientId=${params.clientId}`)
  if (params.groupId) parts.push(`groupId=${params.groupId}`)
  if (params.loggedInOnly) parts.push('loggedInOnly=true')
  if (params.timeWindow) {
    parts.push(`timeWindowMode=${params.timeWindow.mode}`)
    if (params.timeWindow.value != null) parts.push(`timeWindowValue=${params.timeWindow.value}`)
  }
  return parts.length ? `?${parts.join('&')}` : ''
}

// ── Records widget (S181) ──────────────────────────────────────────────────

export interface RecordColumn { key: string; label: string; group: string }
export interface RecordsPage {
  total: number; page: number; pageSize: number; truncated: boolean
  columns: RecordColumn[]
  rows: { id: string; values: Record<string, string | null> }[]
}
/** The records widget's detail view (S181): the call's full read-only details, and whether this viewer may play it. */
export interface RecordDetailResponse { detail: CallDetailData; canPlayRecording: boolean }

/** Paging, search, sort and per-column filters for one request. */
export interface RecordsParams { page: number; pageSize: number; search?: string; sort?: string; desc?: boolean; filters?: Record<string, string> }

export function recordsQuery(p: RecordsParams): string {
  const q = new URLSearchParams({ page: String(p.page), pageSize: String(p.pageSize) })
  if (p.search) q.set('search', p.search)
  if (p.sort) { q.set('sort', p.sort); q.set('desc', String(p.desc ?? true)) }
  for (const [k, v] of Object.entries(p.filters ?? {})) if (v.trim()) q.set(`f.${k}`, v)
  return q.toString()
}

export const dashboardWidgetsApi = {
  records: (config: WidgetFilterConfig, p: RecordsParams) => {
    const base = buildQuery(config)
    const cols = (config.columns ?? []).join(',')
    return api.get<RecordsPage>(`/api/v1/dashboard-widgets/records${base || '?'}${base ? '&' : ''}${recordsQuery(p)}${cols ? `&columns=${encodeURIComponent(cols)}` : ''}`)
  },
  recordDetail: (config: WidgetFilterConfig, id: string) => {
    const q = new URLSearchParams()
    if (config.campaignId) q.set('campaignId', config.campaignId)
    else if (config.clientId) q.set('clientId', config.clientId)
    if (config.allowRecordings === false) q.set('allowRecordings', 'false')
    return api.get<RecordDetailResponse>(`/api/v1/dashboard-widgets/records/${id}?${q}`)
  },
  recordColumns: () => api.get<RecordColumn[]>('/api/v1/dashboard-widgets/records/columns'),

  agentStateCounter: (params: WidgetFilterConfig) =>
    api.get<AgentStateCounterData>(`/api/v1/dashboard-widgets/agent-state-counter${buildQuery(params)}`),

  agentList: (params: WidgetFilterConfig) =>
    api.get<AgentListRow[]>(`/api/v1/dashboard-widgets/agent-list${buildQuery(params)}`),

  callStateByCampaign: (params: WidgetFilterConfig) =>
    api.get<CampaignStateCountRow[]>(`/api/v1/dashboard-widgets/call-state-by-campaign${buildQuery(params)}`),

  pendingQueueCallbacks: (params: WidgetFilterConfig) =>
    api.get<PendingQueueCallbackRow[]>(`/api/v1/dashboard-widgets/pending-queue-callbacks${buildQuery(params)}`),

  activeCalls: (params: WidgetFilterConfig) =>
    api.get<ActiveCallRow[]>(`/api/v1/dashboard-widgets/active-calls${buildQuery(params)}`),

  queuedCalls: (params: WidgetFilterConfig) =>
    api.get<QueuedCallRow[]>(`/api/v1/dashboard-widgets/queued-calls${buildQuery(params)}`),

  serviceLevelThreshold: (params: WidgetFilterConfig) =>
    api.get<ServiceLevelThresholdData>(`/api/v1/dashboard-widgets/service-level-threshold${buildQuery(params)}`),

  kpi: (params: WidgetFilterConfig) => {
    const q = buildQuery(params)
    const extra = `groupBy=${encodeURIComponent(params.groupBy ?? 'none')}${params.groupBy2 ? `&groupBy2=${encodeURIComponent(params.groupBy2)}` : ''}`
    return api.get<KpiResult>(`/api/v1/dashboard-widgets/kpi${q ? `${q}&${extra}` : `?${extra}`}`)
  },
}

/** KPI widget (S181) — see KpiCalculator.cs for every formula. */
export interface KpiRevenue {
  total: number; net: number; perCall: number | null; perOpportunity: number | null; averageOrder: number | null
  perAgentHour: number | null; perTalkHour: number | null
}
export interface KpiMetrics {
  interactions: number; opportunities: number; orders: number; netOrders: number; declines: number
  rawCloseRate: number | null; grossCloseRate: number | null; netCloseRate: number | null
  gross: KpiRevenue; exclTax: KpiRevenue; merch: KpiRevenue
  upsellTakeRate: number | null; unitsPerOrder: number | null
  callsOffered: number; callsHandled: number; callsAbandoned: number; abandonRate: number | null; serviceLevel: number | null
  avgTalkSeconds: number | null; avgAcwSeconds: number | null; ahtSeconds: number | null
  loggedInHours: number; talkHours: number
  saleWithoutOrder: number; unmapped: number
  custom: {
    id: string; name: string; numerator: number; denominator: number; percent: number | null
    kind: 'ratio' | 'formula'; format: KpiFormatName; value: number | null
  }[]
}

export type KpiFormatName = 'number' | 'currency' | 'percent' | 'duration' | 'integer'
export interface KpiVariable { name: string; label: string; group: string }
export interface KpiResult {
  total: KpiMetrics
  rows: { key: string; label: string; label2: string | null; subtotal: boolean; metrics: KpiMetrics }[]
  since: string; until: string
}

export interface CustomKpi {
  id: string; name: string; description: string | null; numeratorCategoryIds: string[]; denominatorCategoryIds: string[]
  displayOrder: number; isActive: boolean
  /** S181: ratio (categories) or formula (NCalc over the KPI variables). */
  kind: 'ratio' | 'formula'; formula: string | null; format: KpiFormatName
}
type CustomKpiInput = {
  name: string; description: string | null; numeratorCategoryIds: string[]; denominatorCategoryIds: string[]
  kind?: 'ratio' | 'formula'; formula?: string | null; format?: KpiFormatName; isActive?: boolean
}
export const customKpisApi = {
  list: () => api.get<CustomKpi[]>('/api/v1/custom-kpis'),
  create: (k: CustomKpiInput) => api.post<CustomKpi>('/api/v1/custom-kpis', k),
  update: (id: string, k: CustomKpiInput) => api.put<CustomKpi>(`/api/v1/custom-kpis/${id}`, k),
  remove: (id: string) => api.delete<void>(`/api/v1/custom-kpis/${id}`),
  variables: () => api.get<KpiVariable[]>('/api/v1/custom-kpis/variables'),
  validate: (formula: string) => api.post<{ error: string | null }>('/api/v1/custom-kpis/validate', { formula }),
}

/** Active Calls widget (S180): one call an agent is on right now. */
export interface ActiveCallRow {
  callRecordId: string
  direction: 'inbound' | 'callback' | 'outbound'
  customerNumber: string | null
  ourNumber: string | null
  campaignId: string | null
  campaignName: string | null
  agentId: string
  agentName: string | null
  connectedAt: string | null
  tierLabel: string | null
  onHold: boolean
  secureCapture: boolean
  recording: boolean
  /** Monitor / Coach / Barge / Take over can reach it (inbound + callbacks; not manual outbound yet). */
  supervisable: boolean
  scriptName: string | null
  /** The script section the agent is in right now (null when the script has none). */
  sectionName: string | null
}
