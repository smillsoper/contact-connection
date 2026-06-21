import { api } from './client'
import type { FlowNodeState, StartSessionRequest, AdvanceSessionRequest } from '../types/flow'
import type { ContactConnectionFlowDefinition } from '../types/designer'

export interface FlowSummary {
  id: string
  name: string
  flow_type: string
  is_active: boolean
  version: number
  created_at: string
  updated_at: string
}

export interface FlowDetail extends FlowSummary {
  definition: string
}

export interface AddressValidationResult {
  outcomeKey: string
  outcomeLabel: string
  message: string | null
  correctedFields: Record<string, string> | null
  matches: Record<string, string>[] | null
}

export interface AutocompleteSuggestion {
  placeId: string
  displayText: string
}

export interface AutocompleteAddressResult {
  suggestions: AutocompleteSuggestion[]
}

export interface AutocompleteSelectionResult {
  fields: Record<string, string> | null
  error: string | null
}

export interface ZipLookupResult {
  city: string | null
  cities: string[]
  state: string | null
  zip4: string | null
  latitude: number | null
  longitude: number | null
  outcomeKey: string | null
  message: string | null
}

export const flowsApi = {
  // Agent panel — published flows only
  list: () => api.get<FlowSummary[]>('/api/v1/flows'),

  // Flows management page — all flows including drafts
  listAll: () => api.get<FlowSummary[]>('/api/v1/flows/all'),

  // Filtered by flow type (crm or telephony)
  listAllByType: (type: string) => api.get<FlowSummary[]>(`/api/v1/flows/all?type=${type}`),

  startSession: (req: StartSessionRequest) =>
    api.post<FlowNodeState>('/api/v1/flow-sessions', req),

  getSession: (sessionId: string) =>
    api.get<FlowNodeState>(`/api/v1/flow-sessions/${sessionId}`),

  advance: (sessionId: string, req: AdvanceSessionRequest) =>
    api.post<FlowNodeState>(`/api/v1/flow-sessions/${sessionId}/advance`, req),

  validateAddress: (sessionId: string, address: Record<string, string>) =>
    api.post<AddressValidationResult>(`/api/v1/flow-sessions/${sessionId}/validate-address`, { address }),

  lookupZip: (sessionId: string, zip: string) =>
    api.post<ZipLookupResult>(`/api/v1/flow-sessions/${sessionId}/lookup-zip`, { zip }),

  autocompleteAddress: (sessionId: string, text: string, sessionToken: string) =>
    api.post<AutocompleteAddressResult>(`/api/v1/flow-sessions/${sessionId}/autocomplete-address`, { text, sessionToken }),

  selectAutocompleteAddress: (sessionId: string, placeId: string, sessionToken: string) =>
    api.post<AutocompleteSelectionResult>(`/api/v1/flow-sessions/${sessionId}/select-autocomplete-address`, { placeId, sessionToken }),

  // Flow designer
  create: (name: string, flowType: string, definition: ContactConnectionFlowDefinition) =>
    api.post<FlowDetail>('/api/v1/flows', {
      name,
      flowType,
      definition: JSON.stringify(definition),
    }),

  getDetail: (id: string) => api.get<FlowDetail>(`/api/v1/flows/${id}`),

  updateDefinition: (id: string, name: string, definition: ContactConnectionFlowDefinition) =>
    api.put<FlowDetail>(`/api/v1/flows/${id}`, { name, definition: JSON.stringify(definition) }),

  publish: (id: string) => api.post<FlowDetail>(`/api/v1/flows/${id}/publish`),

  delete: (id: string) => api.delete<void>(`/api/v1/flows/${id}`),
}
