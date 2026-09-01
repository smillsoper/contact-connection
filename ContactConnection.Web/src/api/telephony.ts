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
  campaign?: { id: string; name: string }
  createdAt: string
  updatedAt: string
}

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
}) => api.put<Campaign>(`/api/v1/campaigns/${id}/recording`, data)

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

export const createPhoneNumber = (campaignId: string, number: string, label?: string) =>
  api.post<PhoneNumber>('/api/v1/phone-numbers', { campaignId, number, label })

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
