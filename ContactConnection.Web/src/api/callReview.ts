import { api } from './client'
import type { CartDocument } from './cart'

// Call Records admin (S165) — find a past call, review it, correct its data and re-run its flow's
// API calls (e.g. resubmit an order the Order API rejected). See CallReviewEndpoints.cs.

export interface CallSummary {
  id: string
  createdAt: string
  callStartAt: string | null
  callEndAt: string | null
  handleTimeSeconds: number | null
  campaignId: string
  campaignName: string | null
  agentId: string | null
  agentName: string | null
  source: string
  overallStatus: string
  callerId: string | null
  billingPhone: string | null
  shippingPhone: string | null
  customerName: string | null
  orderNumber: string | null
  cartTotal: number | null
  hasFailedApiCall: boolean
}

export interface CallSearchPage {
  total: number
  page: number
  pageSize: number
  items: CallSummary[]
}

export interface CallSearchFilters {
  from?: string
  to?: string
  campaignId?: string
  phone?: string
  orderNumber?: string
  name?: string
  failedOnly?: boolean
  page?: number
  pageSize?: number
}

/** Same camelCase shape as the backend AddressData (call_records.addresses). */
export interface CallAddress {
  prefix?: string | null
  street?: string | null
  unitPrefix?: string | null
  unit?: string | null
  company?: string | null
  firstName?: string | null
  middleInitial?: string | null
  lastName?: string | null
  city?: string | null
  state?: string | null
  zip?: string | null
  zip4?: string | null
  country?: string | null
  isPOBox?: boolean
  isCanada?: boolean
  isForeign?: boolean
  isMilitary?: boolean
  isOutlyingUS?: boolean
  isAKHI?: boolean
  latitude?: number | null
  longitude?: number | null
  isVerified?: boolean
  verificationSource?: string | null
}

export interface ApiCallNodeSummary {
  nodeId: string
  /** 'api_call' | 'authorize_payment' */
  nodeType: string
  /** API call marked as the order submission — wipes the card on success. */
  releasesCardData: boolean
  label: string
  outputVariable: string | null
  oncePerCall: boolean
  runCount: number
  lastRunAt: string | null
  success: string | null
  statusCode: string | null
  error: string | null
  response: string | null
}

export interface CallSessionView {
  id: string
  flowId: string
  flowName: string | null
  flowVersion: number
  status: string
  startedAt: string
  completedAt: string | null
  isLive: boolean
  commitLabel: string | null
  flowVars: Record<string, string>
  inputs: Record<string, string>
  apiCalls: ApiCallNodeSummary[]
}

export interface CallPayment {
  id: string
  gateway: string
  transactionType: string
  amount: number
  status: string
  gatewayTransactionId: string | null
  authCode: string | null
  responseReasonText: string | null
  cardLast4: string | null
  cardType: string | null
  createdAt: string
  voidedAt: string | null
}

export interface CallAuditEntry {
  id: string
  action: string
  summary: string
  detail: unknown
  actorName: string
  createdAt: string
}

export interface CallDetail {
  id: string
  createdAt: string
  updatedAt: string
  callStartAt: string | null
  callEndAt: string | null
  handleTimeSeconds: number | null
  source: string
  recordType: string
  overallStatus: string
  clientId: string
  clientName: string | null
  campaignId: string
  campaignName: string | null
  agentId: string | null
  agentName: string | null
  callerId: string | null
  dnis: string | null
  orderNumber: string | null
  recordingUrl: string | null
  contact: {
    firstName: string | null
    lastName: string | null
    email: string | null
    phone: string | null
    billingPhone: string | null
    shippingPhone: string | null
  }
  addresses: { billing: CallAddress | null; shipping: CallAddress | null } | null
  cart: CartDocument | null
  authorizedAmount: number | null
  cardData: {
    onFile: boolean
    storedAt: string | null
    wipedAt: string | null
    wipeReason: string | null
    retention: string
    /** When the retention sweep would wipe it (null when nothing is on file). */
    expiresAt: string | null
  }
  payments: CallPayment[]
  customFields: CallCustomField[]
  commitmentEvents: { eventName?: string; timestamp?: string; [k: string]: unknown }[]
  dispositions: { interactionNumber: number; type: string; disposition: string | null; status: string; startedAt: string; completedAt: string | null }[]
  sessions: CallSessionView[]
  audit: CallAuditEntry[]
  /** A caller still connected (live telephony channel); null when not. */
  liveCall: { connected: boolean; withAgent: boolean; callerNumber: string } | null
  finalized: { at: string; byName: string | null; reason: string | null } | null
  /** Something is still open (a script, a connected caller, or no end time) — Finalize is offered. */
  canFinalize: boolean
  /** The call's agent's supervisor lock. */
  agentLock: { statusLocked: boolean; signInLocked: boolean; lockedBy: string | null; reason: string | null } | null
}

export interface FinalizeCallRequest {
  reason: string
  agentLock: 'none' | 'status' | 'sign_in'
  lockReason: string | null
  confirmLiveCall: boolean
}

export interface FinalizeCallResult {
  hungUp: boolean
  hangupError: string | null
  scriptsClosed: number
  lockedAgents: string[]
}

export interface CallCustomField {
  definitionId: string
  fieldName: string
  displayLabel: string
  dataTypeName: string
  isRequired: boolean
  isActive: boolean
  scope: 'campaign' | 'client' | 'tenant'
  /** False for a value stored under a field that no longer applies to the call — read-only. */
  inScope: boolean
  /** Stored value as text (booleans "true"/"false", dates ISO); null when never set. */
  value: string | null
  storedAt: string | null
}

export interface ApiCallRerunResult {
  success: boolean
  transition: string
  statusCode: string | null
  error: string | null
  response: string | null
  replayed: boolean
  nodeType: string
}

export interface UpdateContactRequest {
  firstName: string | null
  lastName: string | null
  email: string | null
  billingPhone: string | null
  shippingPhone: string | null
}

const base = '/api/v1/call-review/calls'

export const callReviewApi = {
  search: (f: CallSearchFilters) => {
    const q = new URLSearchParams()
    for (const [k, v] of Object.entries(f)) {
      if (v === undefined || v === null || v === '' || v === false) continue
      q.set(k, String(v))
    }
    return api.get<CallSearchPage>(`${base}?${q.toString()}`)
  },

  get: (id: string) => api.get<CallDetail>(`${base}/${id}`),

  updateContact: (id: string, req: UpdateContactRequest) => api.put<void>(`${base}/${id}/contact`, req),

  updateAddress: (id: string, role: 'billing' | 'shipping', address: CallAddress) =>
    api.put<{ cart: CartDocument | null }>(`${base}/${id}/addresses/${role}`, address),

  updateCartQuantity: (id: string, itemIndex: number, quantity: number) =>
    api.patch<CartDocument>(`${base}/${id}/cart/items/${itemIndex}`, { quantity }),

  removeCartItem: (id: string, itemIndex: number) =>
    api.delete<CartDocument>(`${base}/${id}/cart/items/${itemIndex}`),

  updateCustomField: (id: string, definitionId: string, value: string | null) =>
    api.put<void>(`${base}/${id}/custom-fields/${definitionId}`, { value }),

  updateVariables: (id: string, sessionId: string, changes: Record<string, string | null>) =>
    api.put<void>(`${base}/${id}/sessions/${sessionId}/variables`, { changes }),

  finalize: (id: string, req: FinalizeCallRequest) => api.post<FinalizeCallResult>(`${base}/${id}/finalize`, req),

  rerunApiCall: (id: string, sessionId: string, nodeId: string) =>
    api.post<ApiCallRerunResult>(`${base}/${id}/sessions/${sessionId}/api-calls/${encodeURIComponent(nodeId)}/rerun`),
}
