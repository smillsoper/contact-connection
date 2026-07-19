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
}

function buildQuery(params: WidgetFilterConfig): string {
  const parts: string[] = []
  if (params.campaignId) parts.push(`campaignId=${params.campaignId}`)
  if (params.clientId) parts.push(`clientId=${params.clientId}`)
  if (params.groupId) parts.push(`groupId=${params.groupId}`)
  if (params.loggedInOnly) parts.push('loggedInOnly=true')
  return parts.length ? `?${parts.join('&')}` : ''
}

export const dashboardWidgetsApi = {
  agentStateCounter: (params: WidgetFilterConfig) =>
    api.get<AgentStateCounterData>(`/api/v1/dashboard-widgets/agent-state-counter${buildQuery(params)}`),

  agentList: (params: WidgetFilterConfig) =>
    api.get<AgentListRow[]>(`/api/v1/dashboard-widgets/agent-list${buildQuery(params)}`),
}
