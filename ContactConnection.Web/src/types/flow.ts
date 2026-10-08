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
  /** production / training / sandbox (S179 launch modes) — anything but production shows a banner. */
  runMode?: 'production' | 'training' | 'sandbox'
  callRecordId: string
  nodeId: string
  nodeType: 'script' | 'input' | 'email' | 'phone' | 'address' | 'branch' | 'set_variable' | 'api_call' | 'end' | 'section' | 'execute_flow' | 'transition_to_flow' | 'set_custom_field' | 'set_disposition' | 'get_custom_field' | 'store_value' | 'get_value' | 'add_to_cart' | 'remove_cart_item' | 'reset_cart' | 'authorize_payment' | 'void_payment' | 'send_email' | 'commit'
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
  /** Set once the flow passed a commit point — shown as a banner; earlier sections can't be revisited. */
  commitLabel?: string | null
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
  /** Designer sandbox only (S183): run the saved draft instead of the published script. */
  useDraft?: boolean
}

export interface AdvanceSessionRequest {
  inputValue?: string
  transition?: string
  jumpToSectionNodeId?: string
}
