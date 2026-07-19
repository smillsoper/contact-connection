export type DashboardWidgetType = 'agent_state_counter' | 'agent_list' | 'call_state_by_campaign'

export interface WidgetFilterConfig {
  campaignId?: string
  clientId?: string
  groupId?: string
  loggedInOnly?: boolean
}

export interface DashboardWidgetInstance {
  id: string
  widgetType: DashboardWidgetType
  x: number
  y: number
  w: number
  h: number
  config: WidgetFilterConfig
}

export interface WidgetMeta {
  type: DashboardWidgetType
  label: string
  defaultSize: { w: number; h: number }
  minSize: { w: number; h: number }
}

export const WIDGET_META: Record<DashboardWidgetType, WidgetMeta> = {
  agent_state_counter: {
    type: 'agent_state_counter',
    label: 'Agent State Counter',
    defaultSize: { w: 4, h: 8 },
    minSize: { w: 3, h: 6 },
  },
  agent_list: {
    type: 'agent_list',
    label: 'Agent List',
    defaultSize: { w: 4, h: 8 },
    minSize: { w: 3, h: 5 },
  },
  call_state_by_campaign: {
    type: 'call_state_by_campaign',
    label: 'Call State by Campaign',
    defaultSize: { w: 8, h: 8 },
    minSize: { w: 5, h: 5 },
  },
}

export const WIDGET_TYPES: DashboardWidgetType[] = ['agent_state_counter', 'agent_list', 'call_state_by_campaign']

// Which filter fields each widget's config modal should show — agent-scoped widgets support
// Client/Campaign/Agent Group + Logged-in-only; call-scoped widgets only support Client/Campaign
// (agent group and logged-in-only don't apply to calls).
export interface WidgetFilterFields {
  client: boolean
  campaign: boolean
  group: boolean
  loggedInOnly: boolean
}

export const WIDGET_FILTER_FIELDS: Record<DashboardWidgetType, WidgetFilterFields> = {
  agent_state_counter: { client: true, campaign: true, group: true, loggedInOnly: true },
  agent_list: { client: true, campaign: true, group: true, loggedInOnly: true },
  call_state_by_campaign: { client: true, campaign: true, group: false, loggedInOnly: false },
}

export function newWidgetId(): string {
  return `w_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`
}
