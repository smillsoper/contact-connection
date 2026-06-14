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
  maxQueueSize: number
  queueTimeoutSeconds: number
  serviceLevelThresholdSeconds: number
  client?: { id: string; name: string }
  createdAt: string
  updatedAt: string
}

export interface PhoneNumber {
  id: string
  tenantId: string
  campaignId: string
  number: string
  label?: string
  isActive: boolean
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

export const createCampaign = (
  clientId: string,
  name: string,
  slug: string,
  description?: string,
) => api.post<Campaign>('/api/v1/campaigns', { clientId, name, slug, description })

export const activateCampaign = (id: string) =>
  api.post<Campaign>(`/api/v1/campaigns/${id}/activate`)

export const pauseCampaign = (id: string) =>
  api.post<Campaign>(`/api/v1/campaigns/${id}/pause`)

export const deactivateCampaign = (id: string) =>
  api.post<Campaign>(`/api/v1/campaigns/${id}/deactivate`)

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
