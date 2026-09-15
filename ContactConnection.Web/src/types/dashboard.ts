export type DashboardWidgetType =
  'agent_state_counter' | 'agent_list' | 'call_state_by_campaign' | 'callbacks' | 'service_level_threshold'

export interface TimeWindowConfig {
  mode: 'today' | 'hours' | 'minutes'
  /** Ignored for mode 'today'. Required for 'hours'/'minutes'. */
  value?: number
}

export interface WidgetFilterConfig {
  campaignId?: string
  clientId?: string
  groupId?: string
  loggedInOnly?: boolean
  timeWindow?: TimeWindowConfig
}

export interface DashboardWidgetInstance {
  id: string
  widgetType: DashboardWidgetType
  /** User-editable display title — overrides WIDGET_META's static label when set. Especially
   * useful once a widget's filter narrows it to one client/campaign, so the tile itself says
   * what it's actually showing instead of the generic widget-type name. */
  title?: string
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
  callbacks: {
    type: 'callbacks',
    label: 'Callbacks',
    defaultSize: { w: 5, h: 8 },
    minSize: { w: 4, h: 5 },
  },
  service_level_threshold: {
    type: 'service_level_threshold',
    label: 'Service Level',
    defaultSize: { w: 4, h: 8 },
    minSize: { w: 3, h: 6 },
  },
}

export const WIDGET_TYPES: DashboardWidgetType[] =
  ['agent_state_counter', 'agent_list', 'call_state_by_campaign', 'callbacks', 'service_level_threshold']

// Which filter fields each widget's config modal should show — agent-scoped widgets support
// Client/Campaign/Agent Group + Logged-in-only; call-scoped widgets only support Client/Campaign
// (agent group and logged-in-only don't apply to calls). timeWindow only applies to widgets whose
// stat is a lookback aggregate rather than a live snapshot.
export interface WidgetFilterFields {
  client: boolean
  campaign: boolean
  group: boolean
  loggedInOnly: boolean
  timeWindow: boolean
}

export const WIDGET_FILTER_FIELDS: Record<DashboardWidgetType, WidgetFilterFields> = {
  agent_state_counter: { client: true, campaign: true, group: true, loggedInOnly: true, timeWindow: false },
  agent_list: { client: true, campaign: true, group: true, loggedInOnly: true, timeWindow: false },
  call_state_by_campaign: { client: true, campaign: true, group: false, loggedInOnly: false, timeWindow: false },
  callbacks: { client: true, campaign: true, group: false, loggedInOnly: false, timeWindow: false },
  service_level_threshold: { client: true, campaign: true, group: false, loggedInOnly: false, timeWindow: true },
}

export function newWidgetId(): string {
  return `w_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`
}
