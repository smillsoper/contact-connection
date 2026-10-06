import { api } from './client'

// The tenant's Billing page (S179, Sprint 1 item 4) — paying ContactConnection's invoices through Stripe.

export interface BillingConfig { configured: boolean; publishableKey: string | null }
export interface PaymentMethodView { configured: boolean; type: 'card' | 'us_bank_account' | null; label: string | null; autopay: boolean }
export interface PaymentResult { state: 'paid' | 'processing' | 'failed'; message: string | null }

export interface TenantInvoice {
  id: string
  kind: 'invoice' | 'credit_note'
  number: string | null
  periodStart: string | null
  periodEnd: string | null
  status: 'issued' | 'paid' | 'void'
  total: number
  issuedAt: string | null
  dueOn: string | null
  paidAt: string | null
  paymentState: 'processing' | 'failed' | 'disputed' | null
  paymentError: string | null
  creditsInvoiceId: string | null
  creditDisposition: 'refund_stripe' | 'refund_manual' | 'carry_forward' | null
  credited: number
  owed: number
}

export const billingApi = {
  config: () => api.get<BillingConfig>('/api/v1/billing/config'),
  paymentMethod: () => api.get<PaymentMethodView>('/api/v1/billing/payment-method'),
  setupIntent: () => api.post<{ clientSecret: string }>('/api/v1/billing/payment-method/setup-intent', {}),
  savePaymentMethod: (setupIntentId: string) => api.post<PaymentMethodView>('/api/v1/billing/payment-method', { setupIntentId }),
  setAutopay: (enabled: boolean) => api.put<PaymentMethodView>('/api/v1/billing/autopay', { enabled }),
  invoices: () => api.get<TenantInvoice[]>('/api/v1/billing/invoices'),
  document: (id: string) => api.getText(`/api/v1/billing/invoices/${id}/document`),
  pay: (id: string) => api.post<PaymentResult>(`/api/v1/billing/invoices/${id}/pay`, {}),
}
