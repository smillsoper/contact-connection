import { api } from './client'

// ── Types ─────────────────────────────────────────────────────────────────────

export interface Client {
  id: string
  tenantId: string
  name: string
  accountNumber?: string
  status: string
  campaigns: { id: string; name: string; slug: string; status: string }[]
  createdAt: string
  updatedAt: string
}

export interface Campaign {
  id: string
  tenantId: string
  clientId: string
  name: string
  slug: string
  status: string
  description?: string
  flowId?: string
  inboundFlowId?: string
  outboundFlowId?: string
  direction: string
  dialMode: string
  callerIdNumber?: string
  /** Manual outbound calling window, callee's local time ("HH:mm"); null = 8:00 AM – 9:00 PM. */
  outboundHoursStart?: string | null
  outboundHoursEnd?: string | null
  priority: number
  afterCallWorkSeconds: number
  maxQueueSize: number
  queueTimeoutSeconds: number
  serviceLevelThresholdSeconds: number
  shortAbandonThresholdSeconds: number
  queueAccelerationEnabled: boolean
  queueAccelerationIntervalSeconds: number
  queueAccelerationPriorityBoost: number
  /** How a queued call is delivered to agents — 'ring_all' | 'auto_answer_best_agent' | 'ring_top_n_by_proficiency'. */
  ringStrategy: string
  /** Only meaningful when ringStrategy === 'ring_top_n_by_proficiency'. */
  ringTopN: number
  // Call-recording policy (the ceiling the tf_record node + ESL recording controller enforce)
  /** 'disabled' | 'full' | 'conversation' | 'record_always_retain_by_disposition' */
  recordingMode: string
  /** 'one_party' | 'two_party_announce' | 'two_party_announce_optout' */
  consentModel: string
  recordingRequired: boolean
  recordStereo: boolean
  recordingBeepEnabled: boolean
  autoMaskOnHold: boolean
  recordingRetentionDays: number
  unmappedRecordingRetentionDays?: number | null
  // PCI SensitiveData (captured card/CVV/SSN) retention override in minutes — null falls back to
  // the platform default (SensitiveData:Retention:TtlMinutes on the Worker). A campaign running a
  // daily/weekly secure export needs this longer than the default safety-net window.
  sensitiveDataRetentionMinutes?: number | null
  /** 'until_script_ends' (default) | 'until_order_submitted' | 'until_exported' (S182) */
  cardDataRetention?: string
  aiSummaryEnabled?: boolean
  taxProvider?: TaxProviderKey
  taxSettings?: CampaignTaxSettings | null
  /** How this campaign answers external routers — see updateCampaignExternalRouting. */
  externalRoutingAcceptMode?: ExternalRoutingAcceptMode
  externalRoutingLimit?: number | null
  client?: { id: string; name: string }
  createdAt: string
  updatedAt: string
}

export interface AgentAssignment {
  id: string
  agentId: string
  campaignId: string
  proficiency: number
  isActive: boolean
  assignedAt: string
}

export interface CampaignDetail extends Campaign {
  phoneNumbers: { id: string; number: string; label?: string; isActive: boolean; flowId?: string; telephonyFlowId?: string }[]
  agentAssignments: AgentAssignment[]
  groupAssignments: {
    id: string
    groupId: string
    campaignId: string
    proficiency: number
    isActive: boolean
    assignedAt: string
    routingTier?: number
    exclusiveWindowSeconds?: number | null
    tierLabel?: string | null
    group?: { id: string; name: string }
  }[]
}

export interface PhoneNumber {
  id: string
  tenantId: string
  campaignId: string
  number: string
  label?: string
  isActive: boolean
  flowId?: string
  telephonyFlowId?: string
  /** Who houses the number — see NumberProvider. */
  providerId?: string | null
  /** 'hosted' (real number on our carrier) | 'routing_delivery' (pseudo-DNIS a routing platform delivers to). */
  role?: PhoneNumberRole
  /** For routing_delivery: the public client number the caller actually dials (housed at the routing platform). */
  clientNumber?: string | null
  campaign?: { id: string; name: string }
  createdAt: string
  updatedAt: string
}

export type PhoneNumberRole = 'hosted' | 'routing_delivery'
export type NumberProviderType = 'carrier' | 'routing_platform'

export interface NumberProvider {
  id: string
  name: string
  type: NumberProviderType
  sipGatewayId?: string | null
  sourceIps?: string | null
  notes?: string | null
  isActive: boolean
  hasApiKey: boolean
  apiKeyPrefix?: string | null
  apiKeyIssuedAt?: string | null
  createdAt: string
  updatedAt: string
}

export interface SaveNumberProvider {
  name: string
  type: NumberProviderType
  sourceIps?: string
  notes?: string
}

export const listNumberProviders = () => api.get<NumberProvider[]>('/api/v1/number-providers')
export const createNumberProvider = (body: SaveNumberProvider) => api.post<NumberProvider>('/api/v1/number-providers', body)
export const updateNumberProvider = (id: string, body: SaveNumberProvider) => api.put<NumberProvider>(`/api/v1/number-providers/${id}`, body)
export const activateNumberProvider = (id: string) => api.post<NumberProvider>(`/api/v1/number-providers/${id}/activate`)
export const deactivateNumberProvider = (id: string) => api.post<NumberProvider>(`/api/v1/number-providers/${id}/deactivate`)
/** Issues (or rotates) the provider's API key — the plaintext is returned only this once. */
export const issueNumberProviderApiKey = (id: string) =>
  api.post<{ apiKey: string; provider: NumberProvider }>(`/api/v1/number-providers/${id}/api-key`)
export const revokeNumberProviderApiKey = (id: string) => api.delete<NumberProvider>(`/api/v1/number-providers/${id}/api-key`)

export interface AgentGroup {
  id: string
  tenantId: string
  name: string
  slug: string
  description?: string
  isActive: boolean
  memberCount: number
  createdAt: string
  updatedAt: string
}

export interface AgentGroupDetail extends Omit<AgentGroup, 'memberCount'> {
  members: { groupId: string; agentId: string; joinedAt: string }[]
  campaignAssignments: {
    id: string
    campaignId: string
    proficiency: number
    isActive: boolean
    assignedAt: string
  }[]
}

export interface SipGateway {
  id: string
  tenantId: string
  name: string
  proxy: string
  fromDomain?: string
  username: string
  register: boolean
  transport: string
  codecPrefs?: string
  isActive: boolean
  createdAt: string
  updatedAt: string
}

// ── Clients ───────────────────────────────────────────────────────────────────

export const listClients = () =>
  api.get<Client[]>('/api/v1/clients')

export const createClient = (name: string, accountNumber?: string) =>
  api.post<Client>('/api/v1/clients', { name, accountNumber })

export const activateClient = (id: string) =>
  api.post<Client>(`/api/v1/clients/${id}/activate`)

export const deactivateClient = (id: string) =>
  api.post<Client>(`/api/v1/clients/${id}/deactivate`)

// ── Client order-number sequence ─────────────────────────────────────────────

export type OrderNumberSequence =
  | { configured: false; clientId: string }
  | {
      configured: true
      id: string
      clientId: string
      prefix: string
      suffix: string
      width: number
      nextValue: number
      nextOrderNumber: string
      createdAt: string
      updatedAt: string
    }

export const getOrderNumberSequence = (clientId: string) =>
  api.get<OrderNumberSequence>(`/api/v1/clients/${clientId}/order-number-sequence`)

export const putOrderNumberSequence = (
  clientId: string,
  body: { prefix: string; suffix: string; width: number; nextValue: number },
) => api.put<OrderNumberSequence>(`/api/v1/clients/${clientId}/order-number-sequence`, body)

export const deleteOrderNumberSequence = (clientId: string) =>
  api.delete<void>(`/api/v1/clients/${clientId}/order-number-sequence`)

// ── Campaigns ─────────────────────────────────────────────────────────────────

export const listCampaigns = (clientId?: string) =>
  api.get<Campaign[]>(`/api/v1/campaigns${clientId ? `?clientId=${clientId}` : ''}`)

export const getCampaign = (id: string) =>
  api.get<CampaignDetail>(`/api/v1/campaigns/${id}`)

export const createCampaign = (
  clientId: string,
  name: string,
  slug: string,
  description?: string,
) => api.post<Campaign>('/api/v1/campaigns', { clientId, name, slug, description })

export const updateCampaign = (id: string, data: {
  name: string
  description?: string
  direction: string
  dialMode: string
  priority: number
  afterCallWorkSeconds: number
  callerIdNumber?: string
  maxQueueSize: number
  queueTimeoutSeconds: number
  serviceLevelThresholdSeconds: number
  // Was missing here entirely — every save silently reset it server-side to the backend
  // request record's default (10), regardless of what was actually configured. Found and
  // fixed alongside the ring-strategy fields below, since this is the same payload object.
  shortAbandonThresholdSeconds: number
  queueAccelerationEnabled: boolean
  queueAccelerationIntervalSeconds: number
  queueAccelerationPriorityBoost: number
  ringStrategy: string
  ringTopN: number
}) => api.put<Campaign>(`/api/v1/campaigns/${id}`, data)

export const updateCampaignRecording = (id: string, data: {
  recordingMode: string
  consentModel: string
  recordingRequired: boolean
  recordStereo: boolean
  recordingBeepEnabled: boolean
  autoMaskOnHold: boolean
  recordingRetentionDays: number
  /** Retain-by-disposition (S181): days to keep calls with a missing / unmapped disposition; null = the normal retention. */
  unmappedRecordingRetentionDays?: number | null
}) => api.put<Campaign>(`/api/v1/campaigns/${id}/recording`, data)

// ── Campaign sales tax ───────────────────────────────────────────────────────

/** '' = flat rate (the default), 'avalara' = Avalara AvaTax. */
export type TaxProviderKey = '' | 'avalara'

export interface TaxShipFrom {
  street?: string
  unit?: string
  city?: string
  state?: string
  zip?: string
  country?: string
}

export interface StateTaxRate {
  /** Two-letter state code. */
  state: string
  /** Fraction: 0.029 = 2.9%. */
  rate: number
  /** Also tax shipping at this rate (prorated to the taxable share of the cart). */
  taxShipping?: boolean
  /** Fixed per-order state fee (e.g. Colorado Retail Delivery Fee) — reported separately from tax. */
  fee?: StateFee | null
}

export interface StateFee {
  description: string
  amount: number
  /** Only charge when the taxable subtotal is at least this (e.g. Minnesota's $100 threshold). */
  minTaxableSubtotal?: number
  /** Identifier for order APIs; defaults to "{STATE}_FEE". */
  code?: string
}

/** A state fee Avalara calculates from a dedicated line (e.g. CO → OF400000). */
export interface AvalaraFeeLine {
  state: string
  taxCode: string
  description: string
  code?: string
}

/** Flat rate uses `rates` (states not listed aren't taxed); Avalara uses the rest.
 *  Credentials are never part of this. */
export interface CampaignTaxSettings {
  rates?: StateTaxRate[]
  companyCode?: string
  productTaxCode?: string
  shippingTaxCode?: string
  customerCode?: string
  shipFrom?: TaxShipFrom
  feeLines?: AvalaraFeeLine[]
}

export const updateCampaignTax = (id: string, taxProvider: TaxProviderKey, taxSettings: CampaignTaxSettings | null) =>
  api.put<Campaign>(`/api/v1/campaigns/${id}/tax`, { taxProvider, taxSettings })

export const updateCampaignAiSettings = (id: string, aiSummaryEnabled: boolean) =>
  api.put<Campaign>(`/api/v1/campaigns/${id}/ai-settings`, { aiSummaryEnabled })

export const updateCampaignSensitiveDataRetention = (id: string, sensitiveDataRetentionMinutes: number | null, cardDataRetention?: string) =>
  api.put<Campaign>(`/api/v1/campaigns/${id}/sensitive-data-retention`, { sensitiveDataRetentionMinutes, cardDataRetention })

export const setCampaignFlow = (id: string, flowId: string) =>
  api.put<Campaign>(`/api/v1/campaigns/${id}/flow`, { flowId })

export const removeCampaignFlow = (id: string) =>
  api.delete<Campaign>(`/api/v1/campaigns/${id}/flow`)

export const setCampaignInboundFlow = (id: string, flowId: string) =>
  api.put<Campaign>(`/api/v1/campaigns/${id}/inbound-flow`, { flowId })

export const removeCampaignInboundFlow = (id: string) =>
  api.delete<Campaign>(`/api/v1/campaigns/${id}/inbound-flow`)

export const setCampaignOutboundFlow = (id: string, flowId: string) =>
  api.put<Campaign>(`/api/v1/campaigns/${id}/outbound-flow`, { flowId })

export const removeCampaignOutboundFlow = (id: string) =>
  api.delete<Campaign>(`/api/v1/campaigns/${id}/outbound-flow`)

export const setPhoneNumberFlow = (id: string, flowId: string) =>
  api.put<PhoneNumber>(`/api/v1/phone-numbers/${id}/flow`, { flowId })

export const removePhoneNumberFlow = (id: string) =>
  api.delete<PhoneNumber>(`/api/v1/phone-numbers/${id}/flow`)

export const setPhoneNumberTelephonyFlow = (id: string, flowId: string) =>
  api.put<PhoneNumber>(`/api/v1/phone-numbers/${id}/telephony-flow`, { flowId })

export const removePhoneNumberTelephonyFlow = (id: string) =>
  api.delete<PhoneNumber>(`/api/v1/phone-numbers/${id}/telephony-flow`)

export const activateCampaign = (id: string) =>
  api.post<Campaign>(`/api/v1/campaigns/${id}/activate`)

export const pauseCampaign = (id: string) =>
  api.post<Campaign>(`/api/v1/campaigns/${id}/pause`)

export const deactivateCampaign = (id: string) =>
  api.post<Campaign>(`/api/v1/campaigns/${id}/deactivate`)

export const assignCampaignAgent = (campaignId: string, agentId: string, proficiency: number) =>
  api.post<AgentAssignment>(`/api/v1/campaigns/${campaignId}/agents`, { agentId, proficiency })

export const bulkAssignCampaignAgents = (campaignId: string, agents: { agentId: string; proficiency: number }[]) =>
  api.post<{ added: number; updated: number }>(`/api/v1/campaigns/${campaignId}/agents/bulk`, { agents })

export const updateCampaignAgentProficiency = (campaignId: string, agentId: string, proficiency: number) =>
  api.put<AgentAssignment>(`/api/v1/campaigns/${campaignId}/agents/${agentId}`, { proficiency })

export const removeCampaignAgent = (campaignId: string, agentId: string) =>
  api.delete<void>(`/api/v1/campaigns/${campaignId}/agents/${agentId}`)

// ── Phone Numbers ─────────────────────────────────────────────────────────────

export const listPhoneNumbers = (campaignId: string) =>
  api.get<PhoneNumber[]>(`/api/v1/phone-numbers?campaignId=${campaignId}`)

export const createPhoneNumber = (
  campaignId: string, number: string, label?: string,
  provider?: { providerId?: string | null; role?: PhoneNumberRole; clientNumber?: string | null },
) => api.post<PhoneNumber>('/api/v1/phone-numbers', { campaignId, number, label, ...provider })

export const updatePhoneNumberProvider = (
  id: string, providerId: string | null, role: PhoneNumberRole, clientNumber: string | null,
) => api.patch<PhoneNumber>(`/api/v1/phone-numbers/${id}`, providerId
  ? { providerId, role, clientNumber }
  : { clearProvider: true, role: 'hosted', clientNumber: null })

export const activatePhoneNumber = (id: string) =>
  api.post<PhoneNumber>(`/api/v1/phone-numbers/${id}/activate`)

export const deactivatePhoneNumber = (id: string) =>
  api.post<PhoneNumber>(`/api/v1/phone-numbers/${id}/deactivate`)

// ── Agent Groups ──────────────────────────────────────────────────────────────

export const listAgentGroups = () =>
  api.get<AgentGroup[]>('/api/v1/agent-groups')

export const createAgentGroup = (name: string, slug: string, description?: string) =>
  api.post<AgentGroup>('/api/v1/agent-groups', { name, slug, description })

export const getAgentGroup = (id: string) =>
  api.get<AgentGroupDetail>(`/api/v1/agent-groups/${id}`)

export const addGroupMember = (groupId: string, agentId: string) =>
  api.post<{ groupId: string; agentId: string; joinedAt: string }>(
    `/api/v1/agent-groups/${groupId}/members`,
    { agentId },
  )

export const removeGroupMember = (groupId: string, agentId: string) =>
  api.delete<void>(`/api/v1/agent-groups/${groupId}/members/${agentId}`)

// ── Parallel queuing — group routing tiers (docs/design/parallel-queuing.md) ────

/** A group's routing settings on one campaign. tier 0 = regular pool; higher tiers are offered a
 *  waiting call first. exclusiveWindowSeconds holds new calls for this tier (only tier > 0). */
export interface GroupRouting {
  routingTier: number
  exclusiveWindowSeconds: number | null
  tierLabel: string | null
}

export interface GroupMemberCampaigns {
  campaigns: ({ campaignId: string; campaignName: string; proficiency: number } & GroupRouting)[]
  members: { agentId: string; allowedCampaignIds: string[] }[]
}

export const getGroupMemberCampaigns = (groupId: string) =>
  api.get<GroupMemberCampaigns>(`/api/v1/agent-groups/${groupId}/member-campaigns`)

export const setGroupMemberCampaigns = (groupId: string, agentId: string, allowedCampaignIds: string[]) =>
  api.put<{ agentId: string; allowedCampaignIds: string[] }>(
    `/api/v1/agent-groups/${groupId}/members/${agentId}/campaigns`, { allowedCampaignIds })

export const assignGroupToCampaign = (campaignId: string, groupId: string, proficiency: number, routing: GroupRouting) =>
  api.post<unknown>(`/api/v1/campaigns/${campaignId}/groups`, { groupId, proficiency, ...routing })

export const setGroupCampaignRouting = (campaignId: string, groupId: string, routing: GroupRouting) =>
  api.put<unknown>(`/api/v1/campaigns/${campaignId}/groups/${groupId}/routing`, routing)

export const setGroupCampaignProficiency = (campaignId: string, groupId: string, proficiency: number) =>
  api.put<unknown>(`/api/v1/campaigns/${campaignId}/groups/${groupId}`, { proficiency })

export const removeGroupFromCampaign = (campaignId: string, groupId: string) =>
  api.delete<void>(`/api/v1/campaigns/${campaignId}/groups/${groupId}`)

// ── External routing (routers like RingSquared asking "will you take a call?") ────

export type ExternalRoutingAcceptMode = 'queue_count' | 'queue_wait' | 'agent_available'

export const updateCampaignExternalRouting = (id: string, acceptMode: ExternalRoutingAcceptMode, limit: number | null) =>
  api.put<Campaign>(`/api/v1/campaigns/${id}/external-routing`, { acceptMode, limit })

// ── SIP Gateways ──────────────────────────────────────────────────────────────

export const listSipGateways = (tenantId: string) =>
  api.get<SipGateway[]>(`/api/v1/sip-gateways?tenantId=${tenantId}`)

export const createSipGateway = (body: {
  tenantId: string
  name: string
  proxy: string
  username: string
  password: string
  register: boolean
  transport?: string
  fromDomain?: string
}) => api.post<SipGateway>('/api/v1/sip-gateways', body)

export const activateSipGateway = (id: string) =>
  api.post<SipGateway>(`/api/v1/sip-gateways/${id}/activate`)

export const deactivateSipGateway = (id: string) =>
  api.post<SipGateway>(`/api/v1/sip-gateways/${id}/deactivate`)

// ── Campaign External Numbers ─────────────────────────────────────────────────

export interface CampaignExternalNumber {
  id: string
  campaignId: string
  label: string
  number: string
  displayOrder: number
  isActive: boolean
  flowId?: string
  telephonyFlowId?: string
  createdAt: string
}

export interface ClientTransferNumber {
  id: string
  campaignId: string
  campaignName?: string
  campaignFlowId?: string
  label: string
  number: string
}

export const listExternalNumbers = (campaignId: string) =>
  api.get<CampaignExternalNumber[]>(`/api/v1/campaigns/${campaignId}/external-numbers`)

export const addExternalNumber = (campaignId: string, label: string, number: string) =>
  api.post<CampaignExternalNumber>(`/api/v1/campaigns/${campaignId}/external-numbers`, { label, number })

export const removeExternalNumber = (campaignId: string, numberId: string) =>
  api.delete<void>(`/api/v1/campaigns/${campaignId}/external-numbers/${numberId}`)

export const setExternalNumberFlow = (campaignId: string, numberId: string, flowId: string) =>
  api.put<CampaignExternalNumber>(`/api/v1/campaigns/${campaignId}/external-numbers/${numberId}/flow`, { flowId })

export const removeExternalNumberFlow = (campaignId: string, numberId: string) =>
  api.delete<CampaignExternalNumber>(`/api/v1/campaigns/${campaignId}/external-numbers/${numberId}/flow`)

export const setExternalNumberTelephonyFlow = (campaignId: string, numberId: string, flowId: string) =>
  api.put<CampaignExternalNumber>(`/api/v1/campaigns/${campaignId}/external-numbers/${numberId}/telephony-flow`, { flowId })

export const removeExternalNumberTelephonyFlow = (campaignId: string, numberId: string) =>
  api.delete<CampaignExternalNumber>(`/api/v1/campaigns/${campaignId}/external-numbers/${numberId}/telephony-flow`)

export const getClientTransferNumbers = (campaignId: string) =>
  api.get<ClientTransferNumber[]>(`/api/v1/campaigns/${campaignId}/client-transfer-numbers`)
