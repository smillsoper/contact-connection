export interface FlowOption {
  value: string
  label: string
}

export interface FlowNodeState {
  sessionId: string
  nodeId: string
  nodeType: 'script' | 'input' | 'email' | 'phone' | 'address' | 'branch' | 'set_variable' | 'api_call' | 'end'
  label: string
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
  allowInternational?: boolean
  showMiddleInitial?: boolean
  showCompany?: boolean
  requiredFields?: string[]
  fieldScripts?: Record<string, string>
  prefilledAddress?: Record<string, string>
}

export interface StartSessionRequest {
  flowId: string
  callRecordId?: string
}

export interface AdvanceSessionRequest {
  inputValue?: string
  transition?: string
}
