import { api } from './client'

// Minimal subsets of the backend's CartItem/CartDocument — only the fields the cart UI renders.
// The real response carries more (payment installments, surcharges, personalization, etc.)
// that TypeScript's structural typing happily ignores here.

export interface CartItem {
  offerId: string
  productId: string
  sku: string
  description: string
  quantity: number
  fullPrice: number
  extendedPrice: number
}

export interface CartDocument {
  items: CartItem[]
  cartSubtotal: number
  shipping: number
  salesTax: number
  cartTotal: number
}

export interface CartConflictError {
  error: string
  unavailableSkus: string[]
}

// Thrown by the client wrapper as a plain Error with a flattened message on any non-2xx response
// (see api/client.ts) — a 409 inventory conflict has no structured field to recover
// `unavailableSkus` from once caught here, so its message is already composed server-side to be
// directly renderable (see CallRecordsEndpoints.CartResult).

export const cartApi = {
  get: (callRecordId: string) => api.get<CartDocument | undefined>(`/api/v1/call-records/${callRecordId}/cart`),

  addItem: (callRecordId: string, offerId: string, quantity: number) =>
    api.post<CartDocument>(`/api/v1/call-records/${callRecordId}/cart/items`, { offerId, quantity }),

  updateQuantity: (callRecordId: string, itemIndex: number, quantity: number) =>
    api.patch<CartDocument>(`/api/v1/call-records/${callRecordId}/cart/items/${itemIndex}`, { quantity }),

  removeItem: (callRecordId: string, itemIndex: number) =>
    api.delete<CartDocument>(`/api/v1/call-records/${callRecordId}/cart/items/${itemIndex}`),
}
