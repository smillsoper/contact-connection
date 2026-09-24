import { api } from './client'

export interface ProductSearchResult {
  id: string
  sku: string
  description: string
  inventory: {
    inventoryStatus: 'Available' | 'CanBackorder' | 'NoBackorder' | 'Discontinued' | 'OutOfStock'
    qtyAvailable: number
    decrementOnOrder: boolean
    backorderMessage: string | null
    discontinuedMessage: string | null
  }
}

export const productsApi = {
  // Free-text search over description/SKU, optionally scoped to a category/attribute set.
  search: (query: string, page = 1, pageSize = 20) =>
    api.get<ProductSearchResult[]>(
      `/api/v1/products?query=${encodeURIComponent(query)}&page=${page}&pageSize=${pageSize}`,
    ),
}
