export type TelephonyNodeType =
  | 'tf_check_block_list'
  | 'tf_check_agent_availability'
  | 'tf_reject'
  | 'tf_answer'
  | 'tf_hangup'
  | 'tf_route_to_queue'
  | 'tf_time_of_day'
  | 'tf_branch'
  | 'tf_end'
  | 'tf_set_variable'
  | 'tf_get_sip_header'
  | 'tf_set_sip_header'

export interface TelVariableAssignment {
  key: string
  value: string
}

export interface TelNodeData extends Record<string, unknown> {
  label: string
  isEntry?: boolean
  // tf_reject
  cause?: 'busy' | 'unavailable' | 'declined'
  // tf_check_block_list
  checkVariable?: string
  // tf_check_agent_availability
  campaignId?: string
  // tf_route_to_queue
  agentExtension?: string
  // tf_time_of_day
  timezone?: string
  windows?: TimeWindow[]
  // tf_branch
  condition?: string
  // tf_set_variable
  assignments?: TelVariableAssignment[]
  // tf_get_sip_header
  headerName?: string
  variableName?: string
  // tf_set_sip_header
  sipHeaderName?: string
  sipHeaderValue?: string
}

export interface TimeWindow {
  name: string
  windowType?: 'weekly' | 'holiday' | 'date'  // defaults to 'weekly'; 'date' has highest priority
  days?: number[]    // for weekly: 0=Sun … 6=Sat
  start: string      // "HH:mm"
  end: string        // "HH:mm"
  holiday?: string   // for holiday: key like 'christmas', 'thanksgiving', etc.
  date?: string      // for date: "YYYY-MM-DD" specific one-time date
}

export interface TelephonyNodeDef {
  type: TelephonyNodeType
  label: string
  cause?: string
  campaignId?: string
  agentExtension?: string
  timezone?: string
  windows?: TimeWindow[]
  condition?: string
  _pos?: { x: number; y: number }
  transitions: Record<string, string>
}

export interface TelephonyFlowDefinition {
  flow_type: 'telephony'
  name: string
  entry_node: string
  nodes: Record<string, TelephonyNodeDef>
  _waypoints?: Record<string, { x: number; y: number }[]>
}

export const TELEPHONY_NODE_META: Record<
  TelephonyNodeType,
  { label: string; color: string; description: string; handles: 'single' | 'dual' | 'none' | 'multi' }
> = {
  tf_check_block_list: {
    label: 'Block List',
    color: '#dc2626',
    description: 'Check if the caller is on the block list',
    handles: 'dual',
  },
  tf_check_agent_availability: {
    label: 'Agent Availability',
    color: '#0369a1',
    description: 'Check if agents are available for this campaign',
    handles: 'dual',
  },
  tf_reject: {
    label: 'Reject',
    color: '#7f1d1d',
    description: 'Reject the call with a SIP cause code',
    handles: 'none',
  },
  tf_answer: {
    label: 'Answer',
    color: '#15803d',
    description: 'Answer the inbound call',
    handles: 'single',
  },
  tf_hangup: {
    label: 'Hang Up',
    color: '#991b1b',
    description: 'Hang up the call (post-answer)',
    handles: 'none',
  },
  tf_route_to_queue: {
    label: 'Route to Queue',
    color: '#1d4ed8',
    description: 'Push the call to the agent queue',
    handles: 'single',
  },
  tf_time_of_day: {
    label: 'Time of Day',
    color: '#92400e',
    description: 'Branch based on day/time schedule',
    handles: 'multi',
  },
  tf_branch: {
    label: 'Branch',
    color: '#b45309',
    description: 'Conditional split on a variable',
    handles: 'dual',
  },
  tf_end: {
    label: 'End',
    color: '#374151',
    description: 'End the telephony flow',
    handles: 'none',
  },
  tf_set_variable: {
    label: 'Set Variable',
    color: '#7c3aed',
    description: 'Assign one or more named variables for use by later nodes',
    handles: 'single',
  },
  tf_get_sip_header: {
    label: 'Get SIP Header',
    color: '#0f766e',
    description: 'Extract a SIP header value from the inbound INVITE into a variable',
    handles: 'single',
  },
  tf_set_sip_header: {
    label: 'Set SIP Header',
    color: '#0e7490',
    description: 'Inject a SIP header into subsequent outgoing SIP messages for this channel',
    handles: 'single',
  },
}

export function defaultTelNodeData(type: TelephonyNodeType): TelNodeData {
  switch (type) {
    case 'tf_check_block_list':
      return { label: 'Check Block List' }
    case 'tf_check_agent_availability':
      return { label: 'Check Agent Availability', campaignId: '' }
    case 'tf_reject':
      return { label: 'Reject Call', cause: 'busy' }
    case 'tf_answer':
      return { label: 'Answer Call' }
    case 'tf_hangup':
      return { label: 'Hang Up' }
    case 'tf_route_to_queue':
      return { label: 'Route to Queue', agentExtension: '' }
    case 'tf_time_of_day':
      return {
        label: 'Time of Day',
        timezone: 'America/Chicago',
        windows: [
          { name: 'business_hours', windowType: 'weekly', days: [1, 2, 3, 4, 5], start: '08:00', end: '17:00' },
          { name: 'after_hours', windowType: 'weekly', days: [1, 2, 3, 4, 5], start: '17:00', end: '08:00' },
          { name: 'weekend', windowType: 'weekly', days: [0, 6], start: '00:00', end: '24:00' },
        ],
      }
    case 'tf_branch':
      return { label: 'Branch', condition: '' }
    case 'tf_end':
      return { label: 'End' }
    case 'tf_set_variable':
      return { label: 'Set Variable', assignments: [{ key: '', value: '' }] }
    case 'tf_get_sip_header':
      return { label: 'Get SIP Header', headerName: 'X-Original-ANI', variableName: 'custom_ani' }
    case 'tf_set_sip_header':
      return { label: 'Set SIP Header', sipHeaderName: '', sipHeaderValue: '' }
  }
}
