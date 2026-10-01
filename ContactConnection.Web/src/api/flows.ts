import { api } from './client'
import type { FlowNodeState, StartSessionRequest, AdvanceSessionRequest } from '../types/flow'
import type { ContactConnectionFlowDefinition } from '../types/designer'
import type { EntityVersionSummary } from './versioning'

export interface FlowSummary {
  id: string
  name: string
  flow_type: string
  flow_direction?: string
  flow_sub_type?: string
  /** Home client/campaign (optional; both null = shared) — see FlowScopeSelect. */
  client_id?: string | null
  campaign_id?: string | null
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

// One callable endpoint under a "general"-category API Definition (the definition is the
// connection — base URL/auth; the endpoint is the operation, e.g. "Get Stats").
export interface GeneralApiSummary {
  id: string
  name: string
  definitionId: string
  definitionName: string
  provider: string | null
  scope: 'tenant' | 'portal'
}

// A Custom Field Definition, for the set_custom_field / get_custom_field (and telephony
// tf_set_custom_field / tf_get_custom_field) node dropdowns. Scope is implicit: both null =
// tenant-wide, clientId only = client-wide, both set = campaign-specific.
export interface CustomFieldDefinitionSummary {
  id: string
  clientId: string | null
  campaignId: string | null
  fieldName: string
  displayLabel: string
  dataTypeName: string
  isRequired: boolean
  displayOrder: number
  isActive: boolean
}

/** A past session to render an API Call node's request against (S169). */
export interface PreviewSession {
  sessionId: string
  callRecordId: string
  startedAt: string
  status: string
  callerId: string | null
  callerName: string
  /** True when this flow has no sessions yet and these come from other flows. */
  otherFlow: boolean
}

/** An API request as it would be sent — credentials never included. */
export interface ApiRequestPreview {
  endpointName: string | null
  method: string | null
  url: string | null
  headers: Record<string, string>
  body: string | null
  bodyTemplateType: string | null
  authType: string | null
  error: string | null
}

const NO_FLOW = '00000000-0000-0000-0000-000000000000'

export const flowsApi = {
  /** Recent sessions to preview an API Call node against (this flow's, else any flow's). */
  previewSessions: (flowId: string | null) =>
    api.get<PreviewSession[]>(`/api/v1/flows/${flowId ?? NO_FLOW}/preview-sessions`),

  /** Renders the node's request against a session's data. Sends nothing. */
  previewApiCall: (flowId: string | null, sessionId: string, nodeId: string, node: Record<string, unknown>) =>
    api.post<ApiRequestPreview>(`/api/v1/flows/${flowId ?? NO_FLOW}/preview-api-call`, { sessionId, nodeId, node }),

  // Agent panel — published flows only
  list: () => api.get<FlowSummary[]>('/api/v1/flows'),

  setScope: (id: string, clientId: string | null, campaignId: string | null) =>
    api.put<{ id: string; client_id: string | null; campaign_id: string | null }>(
      `/api/v1/flows/${id}/scope`, { clientId, campaignId }),

  // Flows management page — all flows including drafts
  listAll: () => api.get<FlowSummary[]>('/api/v1/flows/all'),

  // Filtered by flow type (crm or telephony)
  listAllByType: (type: string) => api.get<FlowSummary[]>(`/api/v1/flows/all?type=${type}`),

  // "general"-category API Definitions available to this tenant's flow designers (tenant's own
  // + platform-provided), for the api_call / tf_general_api_call node dropdowns
  listGeneralApis: () => api.get<GeneralApiSummary[]>('/api/v1/flows/general-apis'),

  // Custom Field Definitions for the Set/Get Call Record Value node dropdowns — reuses the
  // existing admin CRUD endpoint directly (no query params = every definition for the tenant,
  // active and inactive; callers filter to isActive client-side).
  listCustomFieldDefinitions: () => api.get<CustomFieldDefinitionSummary[]>('/api/v1/custom-field-definitions'),

  startSession: (req: StartSessionRequest) =>
    api.post<FlowNodeState>('/api/v1/flow-sessions', req),

  getSession: (sessionId: string) =>
    api.get<FlowNodeState>(`/api/v1/flow-sessions/${sessionId}`),

  /** The signed-in agent's still-open scripts, each on its current step — reopened on portal load (S171). */
  mySessions: () => api.get<FlowNodeState[]>('/api/v1/flow-sessions/mine'),

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
  create: (
    name: string,
    flowType: string,
    definition: ContactConnectionFlowDefinition,
    flowDirection?: string,
    flowSubType?: string,
  ) =>
    api.post<FlowDetail>('/api/v1/flows', {
      name,
      flowType,
      definition: JSON.stringify(definition),
      flowDirection: flowDirection ?? null,
      flowSubType:   flowSubType   ?? null,
    }),

  getDetail: (id: string) => api.get<FlowDetail>(`/api/v1/flows/${id}`),

  updateDefinition: (
    id: string,
    name: string,
    definition: ContactConnectionFlowDefinition,
    flowDirection?: string,
    flowSubType?: string,
  ) =>
    api.put<FlowDetail>(`/api/v1/flows/${id}`, {
      name,
      definition: JSON.stringify(definition),
      flowDirection: flowDirection ?? null,
      flowSubType:   flowSubType   ?? null,
    }),

  publish: (id: string) => api.post<FlowDetail>(`/api/v1/flows/${id}/publish`),

  delete: (id: string) => api.delete<void>(`/api/v1/flows/${id}`),

  // Version history — newest first; revert applies the snapshot and records it as a new version
  listVersions: (id: string) => api.get<EntityVersionSummary[]>(`/api/v1/flows/${id}/versions`),

  revert: (id: string, versionNumber: number) =>
    api.post<FlowDetail>(`/api/v1/flows/${id}/versions/${versionNumber}/revert`, {}),
}
