import { usePortalAuthStore } from '../stores/portalAuthStore'

// Tenant invoicing in the Portal (S179, Sprint 1 item 2).

export type InvoiceStatus = 'draft' | 'issued' | 'paid' | 'void'
export type InvoiceKind = 'invoice' | 'credit_note'
export type InvoiceLineKind =
  | 'usage_local' | 'usage_tollfree' | 'usage_outbound' | 'minimum' | 'setup_fee' | 'adjustment' | 'credit'

export interface InvoiceSummary {
  id: string
  tenantId: string
  tenantName: string | null
  kind: InvoiceKind
  number: string | null
  creditsInvoiceId: string | null
  periodStart: string | null
  periodEnd: string | null
  status: InvoiceStatus
  total: number
  issuedAt: string | null
  dueOn: string | null
  paidAt: string | null
  createdAt: string
}

export interface InvoiceLine {
  id: string
  kind: InvoiceLineKind
  description: string
  quantity: number
  unitPrice: number
  amount: number
  reason: string | null
  createdBy: string | null
  createdAt: string
  metered: boolean
}

export interface InvoiceDetail extends Omit<InvoiceSummary, 'tenantName'> {
  notes: string | null
  billToName: string | null
  billToEmail: string | null
  paymentReference: string | null
  voidedAt: string | null
  voidReason: string | null
  paymentState: 'processing' | 'failed' | 'disputed' | null
  paymentError: string | null
  paymentAttempts: number
  creditDisposition: 'refund_stripe' | 'refund_manual' | 'carry_forward' | null
  createdBy: string | null
  updatedAt: string
  lines: InvoiceLine[]
  /** Only on GET: the credit notes against this invoice, issued credits, and what's owed after them. */
  creditNotes?: { id: string; number: string | null; status: InvoiceStatus; total: number; issuedAt: string | null }[]
  credited?: number
  net?: number
  /** A credit note's original invoice number. */
  creditsNumber?: string | null
  originalStatus?: InvoiceStatus | null
  originalPaidThroughStripe?: boolean
  /** A carried-forward credit's balance left. */
  creditRemaining?: number
}

interface InvoiceView {
  invoice: InvoiceDetail
  creditNotes: NonNullable<InvoiceDetail['creditNotes']>
  credited: number
  net: number
  creditsNumber: string | null
  originalStatus: InvoiceStatus | null
  originalPaidThroughStripe: boolean
  creditRemaining: number
}

async function call<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = usePortalAuthStore.getState().token
  const res = await fetch(path, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
  })
  const text = await res.text()
  if (!res.ok) {
    let message = text || res.statusText
    try { message = (JSON.parse(text) as { error?: string }).error ?? message } catch { /* plain text */ }
    throw new Error(message)
  }
  if (!text) return undefined as T
  return (res.headers.get('content-type')?.includes('json') ? JSON.parse(text) : text) as T
}

const post = <T>(path: string, body?: unknown) => call<T>(path, { method: 'POST', body: JSON.stringify(body ?? {}) })

export const invoicesApi = {
  list: (tenantId?: string) => call<InvoiceSummary[]>(`/api/v1/portal/invoices${tenantId ? `?tenantId=${tenantId}` : ''}`),
  get: (id: string) => call<InvoiceView>(`/api/v1/portal/invoices/${id}`).then((v) => ({
    ...v.invoice, creditNotes: v.creditNotes, credited: v.credited, net: v.net, creditsNumber: v.creditsNumber,
    originalStatus: v.originalStatus, originalPaidThroughStripe: v.originalPaidThroughStripe, creditRemaining: v.creditRemaining,
  }) as InvoiceDetail),
  document: (id: string) => call<string>(`/api/v1/portal/invoices/${id}/document`),
  createMonthly: (tenantId: string, month: string) => post<InvoiceDetail>(`/api/v1/portal/tenants/${tenantId}/invoices`, { month }),
  createStandalone: (tenantId: string) => post<InvoiceDetail>(`/api/v1/portal/tenants/${tenantId}/invoices`, { month: null }),
  refreshUsage: (id: string) => post<InvoiceDetail>(`/api/v1/portal/invoices/${id}/refresh-usage`),
  addLine: (id: string, line: { kind: InvoiceLineKind; description: string; quantity: number; unitPrice: number; reason?: string }) =>
    post<InvoiceDetail>(`/api/v1/portal/invoices/${id}/lines`, line),
  removeLine: (id: string, lineId: string) => call<InvoiceDetail>(`/api/v1/portal/invoices/${id}/lines/${lineId}`, { method: 'DELETE' }),
  setNotes: (id: string, notes: string) => call<InvoiceDetail>(`/api/v1/portal/invoices/${id}/notes`, { method: 'PUT', body: JSON.stringify({ notes }) }),
  issue: (id: string) => post<{ invoice: InvoiceDetail; emailedTo: string | null; emailError: string | null }>(`/api/v1/portal/invoices/${id}/issue`),
  markPaid: (id: string, paidAt: string | null, reference: string) =>
    post<InvoiceDetail>(`/api/v1/portal/invoices/${id}/mark-paid`, { paidAt, reference }),
  void: (id: string, reason: string) => post<InvoiceDetail>(`/api/v1/portal/invoices/${id}/void`, { reason }),
  creditNote: (id: string, amount: number, description: string, reason: string) =>
    post<InvoiceDetail>(`/api/v1/portal/invoices/${id}/credit-note`, { amount, description, reason }),
  deleteDraft: (id: string) => call<void>(`/api/v1/portal/invoices/${id}`, { method: 'DELETE' }),
  settleCredit: (id: string, disposition: 'refund_stripe' | 'refund_manual' | 'carry_forward', reference?: string) =>
    post<InvoiceDetail>(`/api/v1/portal/invoices/${id}/settle-credit`, { disposition, reference }),
}

export const money = (n: number) => n.toLocaleString('en-US', { style: 'currency', currency: 'USD' })

export function invoiceLabel(i: { kind: InvoiceKind; number: string | null }) {
  return i.number ?? (i.kind === 'credit_note' ? 'Credit note (draft)' : 'Draft')
}

export function periodLabel(i: { periodStart: string | null }) {
  if (!i.periodStart) return 'One-off'
  const [y, m] = i.periodStart.split('-').map(Number)
  return new Date(y, m - 1, 1).toLocaleDateString('en-US', { month: 'long', year: 'numeric' })
}

export const STATUS_STYLE: Record<InvoiceStatus, string> = {
  draft: 'bg-amber-950/50 text-amber-300 border-amber-800',
  issued: 'bg-sky-950/50 text-sky-300 border-sky-800',
  paid: 'bg-emerald-950/50 text-emerald-300 border-emerald-800',
  void: 'bg-gray-800 text-gray-400 border-gray-700',
}
