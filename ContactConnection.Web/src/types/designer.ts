export type ContactConnectionNodeType =
  | 'script'
  | 'input'
  | 'email'
  | 'phone'
  | 'address'
  | 'section'
  | 'execute_flow'
  | 'transition_to_flow'
  | 'scheduled_callback'
  | 'branch'
  | 'set_variable'
  | 'trigger_telephony_event'
  | 'api_call'
  | 'set_custom_field'
  | 'get_custom_field'
  | 'store_value'
  | 'get_value'
  | 'add_to_cart'
  | 'remove_cart_item'
  | 'reset_cart'
  | 'end'

export interface NodeData extends Record<string, unknown> {
  label: string
  isEntry?: boolean
  // script
  content?: string
  waitForTelephonyEventName?: string
  waitForTelephonyEventTimeoutSeconds?: number
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
  useValidation?: boolean
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
  // scheduled_callback — book a customer callback for a future time
  callbackNumber?: string          // template, e.g. {{flow.customer_phone.value}}
  scheduledDateValue?: string      // template — the date
  scheduledTimeValue?: string      // template — the time; blank => 09:00
  targetCampaignId?: string        // queue campaign for the answered leg (blank = current)
  allowedDays?: string             // CSV of 0-6 (0=Sun) the callback may land on
  allowedStartTime?: string        // "HH:mm" earliest local time
  allowedEndTime?: string          // "HH:mm" latest local time
  windowMinutes?: number
  maxAttempts?: number
  callerIdOverride?: string         // blank = DNIS the caller dialed. Literal or {{variable}}
  // branch
  condition?: string
  // set_variable
  assignments?: { variable: string; value: string }[]
  // trigger_telephony_event
  eventName?: string
  // api_call
  apiEndpointId?: string
  apiDefinitionScope?: 'tenant' | 'portal'
  apiDefinitionName?: string
  apiEndpointName?: string
  timeoutSeconds?: number
  // set_custom_field / get_custom_field — write/read an existing Custom Field Definition's value
  // for the current call record. definitionFieldName/definitionDisplayLabel/definitionDataTypeName
  // are denormalized display fields (same pattern as apiDefinitionName/apiEndpointName) so the
  // canvas node and properties panel don't need a fresh lookup just to show the picked field.
  definitionId?: string
  definitionFieldName?: string
  definitionDisplayLabel?: string
  definitionDataTypeName?: string
  value?: string
  // store_value / get_value — generic tenant/client/campaign key-value store, free-form key
  // (variables allowed), deliberately separate from Custom Fields (no pre-defined schema).
  scope?: 'tenant' | 'client' | 'campaign'
  keyName?: string
  retention?: 'forever' | '1_hour' | '24_hours' | '1_week' | '1_month'
  // add_to_cart — offerId/replacesOfferIds are what the backend reads; the *Name/*Names fields are
  // denormalized display-only, same convention as apiDefinitionName/definitionDisplayLabel above.
  offerId?: string
  offerDisplayName?: string
  quantity?: number
  mode?: 'add' | 'replace'
  replacesOfferIds?: string[]
  replacesOfferNames?: string[]
  // remove_cart_item — same "raw ids for backend, parallel *Names for display" convention
  removeOfferIds?: string[]
  removeOfferNames?: string[]
  // end
  status?: string
}

export interface FlowOption { value: string; label: string }

export interface ContactConnectionNodeDef {
  type: ContactConnectionNodeType
  label: string
  content?: string
  waitForTelephonyEventName?: string
  waitForTelephonyEventTimeoutSeconds?: number
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
  useValidation?: boolean
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
  eventName?: string
  apiEndpointId?: string
  apiDefinitionScope?: 'tenant' | 'portal'
  apiDefinitionName?: string
  apiEndpointName?: string
  timeoutSeconds?: number
  definitionId?: string
  definitionFieldName?: string
  definitionDisplayLabel?: string
  definitionDataTypeName?: string
  value?: string
  scope?: 'tenant' | 'client' | 'campaign'
  keyName?: string
  retention?: 'forever' | '1_hour' | '24_hours' | '1_week' | '1_month'
  offerId?: string
  offerDisplayName?: string
  quantity?: number
  mode?: 'add' | 'replace'
  replacesOfferIds?: string[]
  replacesOfferNames?: string[]
  removeOfferIds?: string[]
  removeOfferNames?: string[]
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
  { label: string; color: string; description: string; handles: 'single' | 'none' | 'custom' }
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
  scheduled_callback: {
    label: 'Scheduled Callback',
    color: '#0891b2',
    description: 'Book a customer callback for a future date/time',
    handles: 'single',
  },
  branch: {
    label: 'Branch',
    color: '#f59e0b',
    description: 'Conditional split on a variable',
    handles: 'single',
  },
  set_variable: {
    label: 'Set Variable',
    color: '#8b5cf6',
    description: 'Assign a value to a flow variable',
    handles: 'single',
  },
  trigger_telephony_event: {
    label: 'Trigger Telephony Event',
    color: '#be123c',
    description: 'Fire a custom event on the bridged call (e.g. start a card capture)',
    handles: 'single',
  },
  api_call: {
    label: 'API Call',
    color: '#6366f1',
    description: 'Call a saved General API Definition',
    handles: 'single',
  },
  set_custom_field: {
    label: 'Set Call Record Value',
    color: '#65a30d',
    description: 'Save a value into a defined custom field for this call',
    handles: 'single',
  },
  get_custom_field: {
    label: 'Get Call Record Value',
    color: '#4d7c0f',
    description: 'Read a defined custom field’s value into a flow variable',
    handles: 'single',
  },
  store_value: {
    label: 'Store Value',
    color: '#0e7490',
    description: 'Save a free-form value scoped to the tenant, client, or campaign',
    handles: 'single',
  },
  get_value: {
    label: 'Get Value',
    color: '#155e75',
    description: 'Read a stored value back into a flow variable',
    handles: 'single',
  },
  add_to_cart: {
    label: 'Add to Cart',
    color: '#059669',
    description: 'Add (or replace) a specific offer on the current call’s cart',
    handles: 'single',
  },
  remove_cart_item: {
    label: 'Remove Cart Item',
    color: '#b45309',
    description: 'Remove one or more specific offers from the current call’s cart',
    handles: 'single',
  },
  reset_cart: {
    label: 'Reset Cart',
    color: '#dc2626',
    description: 'Clear every item from the current call’s cart',
    handles: 'single',
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
      return { label: 'New Script', content: '', waitForTelephonyEventName: '', waitForTelephonyEventTimeoutSeconds: 60 }
    case 'input':
      return { label: 'New Input', scriptLabel: '', scriptContent: '', fieldType: 'text', required: false, options: '', outputVariable: '', minChars: undefined, maxChars: undefined, inputMask: '', customMask: '' }
    case 'email':
      return { label: 'Email', scriptLabel: '', scriptContent: '', outputVariable: '', required: false, checkARecord: false, checkMX: true, checkDisposable: true }
    case 'phone':
      return { label: 'Phone Number', scriptLabel: '', scriptContent: '', outputVariable: '', required: false, allowInternational: false, dncCheck: false }
    case 'address':
      return { label: 'Address', scriptLabel: '', scriptContent: '', outputVariable: '', useValidation: true, allowInternational: false, showMiddleInitial: false, showCompany: false, requiredFields: ['firstName', 'lastName', 'address1', 'zip', 'city', 'state'], fieldScripts: {} }
    case 'section':
      return { label: 'New Section', name: '', outputVariable: '', allowJumpFromAnywhere: false, clearPreviousValues: false }
    case 'execute_flow':
      return { label: 'Execute Flow', targetFlowId: '', targetFlowName: '' }
    case 'transition_to_flow':
      return { label: 'Transition to Flow', targetFlowId: '', targetFlowName: '' }
    case 'scheduled_callback':
      return {
        label: 'Scheduled Callback',
        callbackNumber: '', scheduledDateValue: '', scheduledTimeValue: '',
        targetFlowId: '', targetFlowName: '', targetCampaignId: '',
        allowedDays: '', allowedStartTime: '', allowedEndTime: '',
        windowMinutes: 120, maxAttempts: 3, callerIdOverride: '', outputVariable: 'scheduled_callback',
      }
    case 'branch':
      return { label: 'New Branch', condition: '' }
    case 'set_variable':
      return { label: 'Set Variable', assignments: [{ variable: '', value: '' }] }
    case 'trigger_telephony_event':
      return { label: 'Trigger Telephony Event', eventName: '' }
    case 'api_call':
      return { label: 'New API Call', apiEndpointId: '', apiDefinitionScope: 'tenant', apiDefinitionName: '', apiEndpointName: '', outputVariable: '', timeoutSeconds: 30 }
    case 'set_custom_field':
      return { label: 'Set Call Record Value', definitionId: '', definitionFieldName: '', definitionDisplayLabel: '', definitionDataTypeName: '', value: '' }
    case 'get_custom_field':
      return { label: 'Get Call Record Value', definitionId: '', definitionFieldName: '', definitionDisplayLabel: '', definitionDataTypeName: '', outputVariable: '' }
    case 'store_value':
      return { label: 'Store Value', scope: 'campaign', keyName: '', value: '', retention: 'forever' }
    case 'get_value':
      return { label: 'Get Value', scope: 'campaign', keyName: '', outputVariable: '' }
    case 'add_to_cart':
      return { label: 'Add to Cart', offerId: '', offerDisplayName: '', quantity: 1, mode: 'add', replacesOfferIds: [], replacesOfferNames: [] }
    case 'remove_cart_item':
      return { label: 'Remove Cart Item', removeOfferIds: [], removeOfferNames: [] }
    case 'reset_cart':
      return { label: 'Reset Cart' }
    case 'end':
      return { label: 'End', status: 'complete' }
  }
}
