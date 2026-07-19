export type DashboardWidgetType = 'agent_state_counter' | 'agent_list'

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
}

export const WIDGET_TYPES: DashboardWidgetType[] = ['agent_state_counter', 'agent_list']

export function newWidgetId(): string {
  return `w_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`
}
