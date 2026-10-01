import { api } from './client'

export interface OfferFlag { name: string; value: string }
export interface AutoShipInterval { intervalDays: number; autoShipId?: string | null }
export interface OfferUpsell {
  isUpsell: boolean
  upsellQty: number
  upsellQtyOfEntry: number
  upsellCommission: number
  upsellClientAmount: number
}

export interface OfferSummary {
  id: string
  productId: string
  name: string
  /** SKU override (S169) — null = the product's SKU. `effectiveSku` is what the cart and order use. */
  sku?: string | null
  effectiveSku?: string | null
  clientId: string | null
  /** Empty = all of the client's campaigns. */
  campaignIds: string[]
  fullPrice: number
  shipping: number
  taxExempt: boolean
  shippingExempt: boolean
  /** Overrides the product's tax code; null = product's code / campaign default. */
  taxCode?: string | null
  isActive: boolean
  mixMatchCode: string | null
  upsell?: OfferUpsell
  autoShip?: { autoShip: boolean; autoShipOptional: boolean; autoShipIntervals: AutoShipInterval[] }
  flags?: OfferFlag[]
}

export interface CreateOfferRequest {
  productId: string
  name: string
  fullPrice: number
  shipping?: number
  taxExempt?: boolean
  shippingExempt?: boolean
  clientId?: string | null
  campaignIds?: string[]
  taxCode?: string | null
  sku?: string | null
  isUpsell?: boolean
  upsellQty?: number
  upsellQtyOfEntry?: number
  upsellCommission?: number
  upsellClientAmount?: number
  autoShip?: boolean
  autoShipOptional?: boolean
  autoShipIntervals?: AutoShipInterval[]
  flags?: OfferFlag[]
}

export interface UpdateOfferRequest {
  name: string
  fullPrice: number
  shipping: number
  taxExempt: boolean
  shippingExempt: boolean
  clientId: string | null
  campaignIds: string[]
  taxCode?: string | null
  /** Optional on update — omitted = unchanged. '' clears the SKU override. */
  sku?: string | null
  autoShip?: boolean
  autoShipOptional?: boolean
  autoShipIntervals?: AutoShipInterval[]
  upsell?: OfferUpsell
  flags?: OfferFlag[]
}

export const offersApi = {
  listByProduct: (productId: string) =>
    api.get<OfferSummary[]>(`/api/v1/offers/product/${productId}`),

  /** A product's offers that fit a call — tenant-wide plus those scoped to the call's client/campaign. */
  listForCall: (callRecordId: string, productId: string) =>
    api.get<OfferSummary[]>(`/api/v1/call-records/${callRecordId}/offers?productId=${productId}`),

  create: (req: CreateOfferRequest) => api.post<OfferSummary>('/api/v1/offers', req),

  update: (id: string, req: UpdateOfferRequest) => api.put<OfferSummary>(`/api/v1/offers/${id}`, req),

  activate: (id: string) => api.post<OfferSummary>(`/api/v1/offers/${id}/activate`),

  deactivate: (id: string) => api.post<OfferSummary>(`/api/v1/offers/${id}/deactivate`),
}
