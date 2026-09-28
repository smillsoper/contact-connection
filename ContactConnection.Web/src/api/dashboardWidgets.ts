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

export const dashboardWidgetsApi = {
  agentStateCounter: (params: WidgetFilterConfig) =>
    api.get<AgentStateCounterData>(`/api/v1/dashboard-widgets/agent-state-counter${buildQuery(params)}`),

  agentList: (params: WidgetFilterConfig) =>
    api.get<AgentListRow[]>(`/api/v1/dashboard-widgets/agent-list${buildQuery(params)}`),

  callStateByCampaign: (params: WidgetFilterConfig) =>
    api.get<CampaignStateCountRow[]>(`/api/v1/dashboard-widgets/call-state-by-campaign${buildQuery(params)}`),

  pendingQueueCallbacks: (params: WidgetFilterConfig) =>
    api.get<PendingQueueCallbackRow[]>(`/api/v1/dashboard-widgets/pending-queue-callbacks${buildQuery(params)}`),

  queuedCalls: (params: WidgetFilterConfig) =>
    api.get<QueuedCallRow[]>(`/api/v1/dashboard-widgets/queued-calls${buildQuery(params)}`),

  serviceLevelThreshold: (params: WidgetFilterConfig) =>
    api.get<ServiceLevelThresholdData>(`/api/v1/dashboard-widgets/service-level-threshold${buildQuery(params)}`),
}
