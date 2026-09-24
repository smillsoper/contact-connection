import { api } from './client'

export interface OfferSummary {
  id: string
  productId: string
  name: string
  clientId: string | null
  campaignId: string | null
  fullPrice: number
  shipping: number
  taxExempt: boolean
  shippingExempt: boolean
  isActive: boolean
  mixMatchCode: string | null
}

export interface CreateOfferRequest {
  productId: string
  name: string
  fullPrice: number
  shipping?: number
  taxExempt?: boolean
  shippingExempt?: boolean
  clientId?: string | null
  campaignId?: string | null
}

export interface UpdateOfferRequest {
  name: string
  fullPrice: number
  shipping: number
  taxExempt: boolean
  shippingExempt: boolean
  clientId: string | null
  campaignId: string | null
}

export const offersApi = {
  listByProduct: (productId: string) =>
    api.get<OfferSummary[]>(`/api/v1/offers/product/${productId}`),

  create: (req: CreateOfferRequest) => api.post<OfferSummary>('/api/v1/offers', req),

  update: (id: string, req: UpdateOfferRequest) => api.put<OfferSummary>(`/api/v1/offers/${id}`, req),

  activate: (id: string) => api.post<OfferSummary>(`/api/v1/offers/${id}/activate`),

  deactivate: (id: string) => api.post<OfferSummary>(`/api/v1/offers/${id}/deactivate`),
}
