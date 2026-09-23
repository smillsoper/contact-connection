export interface FlowOption {
  value: string
  label: string
}

export interface JumpTarget {
  sectionNodeId: string
  name: string
  isCurrentSection: boolean
  clearPreviousValues: boolean
  isLocked: boolean
}

export interface FlowNodeState {
  sessionId: string
  nodeId: string
  nodeType: 'script' | 'input' | 'email' | 'phone' | 'address' | 'branch' | 'set_variable' | 'api_call' | 'end' | 'section' | 'execute_flow' | 'transition_to_flow' | 'set_custom_field' | 'get_custom_field' | 'store_value' | 'get_value'
  label: string
  flowName?: string
  content?: string
  inputType?: 'text' | 'select' | 'checkbox'
  required?: boolean
  validationError?: string
  nodeScriptLabel?: string
  nodeScriptContent?: string
  minChars?: number
  maxChars?: number
  inputMask?: string
  options?: FlowOption[]
  condition?: string
  isTerminal: boolean
  lockedFields: string[]
  scriptContext?: string
  // address node
  useValidation?: boolean
  allowInternational?: boolean
  showMiddleInitial?: boolean
  showCompany?: boolean
  requiredFields?: string[]
  fieldScripts?: Record<string, string>
  defaultValue?: string
  prefilledAddress?: Record<string, string>
  // section state
  currentSectionName?: string
  sectionLocked?: boolean
  jumpTargets?: JumpTarget[]
  // auto-advance when the named trigger_telephony_event branch reaches its own tf_end (closes
  // the trigger_telephony_event fire-and-continue race — see project_shared_call_variables)
  waitForTelephonyEventName?: string
  // fallback re-enable for Continue if the matching event-ended push never arrives; null/unset = 60s
  waitForTelephonyEventTimeoutSeconds?: number
}

export interface StartSessionRequest {
  flowId: string
  callRecordId?: string
}

export interface AdvanceSessionRequest {
  inputValue?: string
  transition?: string
  jumpToSectionNodeId?: string
}
