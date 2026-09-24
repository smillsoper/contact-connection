import { api } from './client'

export interface OfferSummary {
  id: string
  productId: string
  name: string
  fullPrice: number
  shipping: number
  isActive: boolean
  mixMatchCode: string | null
}

export const offersApi = {
  listByProduct: (productId: string) =>
    api.get<OfferSummary[]>(`/api/v1/offers/product/${productId}`),
}
