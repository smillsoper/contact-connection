export type ContactConnectionNodeType =
  | 'script'
  | 'input'
  | 'email'
  | 'phone'
  | 'address'
  | 'section'
  | 'execute_flow'
  | 'transition_to_flow'
  | 'branch'
  | 'set_variable'
  | 'api_call'
  | 'end'

export interface NodeData extends Record<string, unknown> {
  label: string
  isEntry?: boolean
  // script
  content?: string
  // input / email shared script
  scriptLabel?: string
  scriptContent?: string
  // input
  fieldType?: string
  required?: boolean
  options?: string
  outputVariable?: string
  minChars?: number
  maxChars?: number
  inputMask?: string
  customMask?: string
  // email
  checkARecord?: boolean
  checkMX?: boolean
  checkDisposable?: boolean
  // phone
  allowInternational?: boolean
  dncCheck?: boolean
  // address
  showMiddleInitial?: boolean
  showCompany?: boolean
  requiredFields?: string[]
  fieldScripts?: Record<string, string>
  // section
  name?: string
  allowJumpFromAnywhere?: boolean
  clearPreviousValues?: boolean
  // execute_flow / transition_to_flow
  targetFlowId?: string
  targetFlowName?: string
  // branch
  condition?: string
  // set_variable
  assignments?: { variable: string; value: string }[]
  // api_call
  method?: string
  url?: string
  headers?: string
  body?: string
  responseMap?: { source: string; target: string }[]
  // end
  status?: string
}

export interface FlowOption { value: string; label: string }

export interface ContactConnectionNodeDef {
  type: ContactConnectionNodeType
  label: string
  content?: string
  scriptLabel?: string
  scriptContent?: string
  fieldType?: string
  required?: boolean
  options?: FlowOption[]
  outputVariable?: string
  minChars?: number
  maxChars?: number
  inputMask?: string
  customMask?: string
  checkARecord?: boolean
  checkMX?: boolean
  checkDisposable?: boolean
  allowInternational?: boolean
  dncCheck?: boolean
  showMiddleInitial?: boolean
  showCompany?: boolean
  requiredFields?: string[]
  fieldScripts?: Record<string, string>
  name?: string
  allowJumpFromAnywhere?: boolean
  clearPreviousValues?: boolean
  targetFlowId?: string
  targetFlowName?: string
  condition?: string
  assignments?: { variable: string; value: string }[]
  method?: string
  url?: string
  headers?: string
  body?: string
  responseMap?: { source: string; target: string }[]
  status?: string
  _pos?: { x: number; y: number }
  transitions: Record<string, string>
}

export interface ContactConnectionFlowDefinition {
  flow_type: 'crm' | 'telephony'
  name: string
  entry_node: string
  nodes: Record<string, ContactConnectionNodeDef>
  _waypoints?: Record<string, { x: number; y: number }[]>
}

export const NODE_META: Record<
  ContactConnectionNodeType,
  { label: string; color: string; description: string; handles: 'single' | 'dual' | 'none' | 'custom' }
> = {
  script: {
    label: 'Script',
    color: '#3b82f6',
    description: 'Display text to the agent',
    handles: 'single',
  },
  input: {
    label: 'Input',
    color: '#10b981',
    description: 'Capture data from the agent',
    handles: 'custom',
  },
  email: {
    label: 'Email',
    color: '#0891b2',
    description: 'Capture and validate an email address',
    handles: 'single',
  },
  phone: {
    label: 'Phone',
    color: '#0d9488',
    description: 'Capture and validate a phone number',
    handles: 'single',
  },
  address: {
    label: 'Address',
    color: '#f97316',
    description: 'Capture and validate a mailing address',
    handles: 'single',
  },
  section: {
    label: 'Section',
    color: '#ffffff',
    description: 'Mark a named section of the flow',
    handles: 'single',
  },
  execute_flow: {
    label: 'Execute Flow',
    color: '#0369a1',
    description: 'Run a sub-flow and return',
    handles: 'single',
  },
  transition_to_flow: {
    label: 'Transition to Flow',
    color: '#7e22ce',
    description: 'Hand off to another flow (no return)',
    handles: 'none',
  },
  branch: {
    label: 'Branch',
    color: '#f59e0b',
    description: 'Conditional split on a variable',
    handles: 'dual',
  },
  set_variable: {
    label: 'Set Variable',
    color: '#8b5cf6',
    description: 'Assign a value to a flow variable',
    handles: 'single',
  },
  api_call: {
    label: 'API Call',
    color: '#6366f1',
    description: 'Call an external API endpoint',
    handles: 'dual',
  },
  end: {
    label: 'End',
    color: '#ef4444',
    description: 'Terminate the flow',
    handles: 'none',
  },
}

export function defaultNodeData(type: ContactConnectionNodeType): NodeData {
  switch (type) {
    case 'script':
      return { label: 'New Script', content: '' }
    case 'input':
      return { label: 'New Input', scriptLabel: '', scriptContent: '', fieldType: 'text', required: false, options: '', outputVariable: '', minChars: undefined, maxChars: undefined, inputMask: '', customMask: '' }
    case 'email':
      return { label: 'Email', scriptLabel: '', scriptContent: '', outputVariable: '', required: false, checkARecord: false, checkMX: true, checkDisposable: true }
    case 'phone':
      return { label: 'Phone Number', scriptLabel: '', scriptContent: '', outputVariable: '', required: false, allowInternational: false, dncCheck: false }
    case 'address':
      return { label: 'Address', scriptLabel: '', scriptContent: '', outputVariable: '', allowInternational: false, showMiddleInitial: false, showCompany: false, requiredFields: ['firstName', 'lastName', 'address1', 'zip', 'city', 'state'], fieldScripts: {} }
    case 'section':
      return { label: 'New Section', name: '', outputVariable: '', allowJumpFromAnywhere: false, clearPreviousValues: false }
    case 'execute_flow':
      return { label: 'Execute Flow', targetFlowId: '', targetFlowName: '' }
    case 'transition_to_flow':
      return { label: 'Transition to Flow', targetFlowId: '', targetFlowName: '' }
    case 'branch':
      return { label: 'New Branch', condition: '' }
    case 'set_variable':
      return { label: 'Set Variable', assignments: [{ variable: '', value: '' }] }
    case 'api_call':
      return { label: 'New API Call', method: 'GET', url: '', headers: '', body: '' }
    case 'end':
      return { label: 'End', status: 'complete' }
  }
}
