import { api } from './client'

export type ProductInventoryStatus = 'Available' | 'CanBackorder' | 'NoBackorder' | 'Discontinued' | 'OutOfStock'

export interface ProductSearchResult {
  id: string
  sku: string
  description: string
  weight: number
  searchable: boolean
  inventory: {
    inventoryStatus: ProductInventoryStatus
    qtyAvailable: number
    decrementOnOrder: boolean
    minimumQty: number
    backorderMessage: string | null
    discontinuedMessage: string | null
  }
}

export interface CreateProductRequest {
  sku: string
  description: string
  weight?: number
  inventoryStatus?: ProductInventoryStatus
  qtyAvailable?: number
  decrementOnOrder?: boolean
}

export interface UpdateProductRequest {
  weight: number
  inventoryStatus: ProductInventoryStatus
  qtyAvailable: number
  decrementOnOrder: boolean
  minimumQty: number
  searchable: boolean
}

export const productsApi = {
  // Free-text search over description/SKU, optionally scoped to a category/attribute set.
  // Also used as the admin "list all products" call (empty query, includeAll=true so a product
  // toggled non-searchable doesn't disappear from the admin's own list).
  search: (query = '', page = 1, pageSize = 50, includeAll = false) =>
    api.get<ProductSearchResult[]>(
      `/api/v1/products?query=${encodeURIComponent(query)}&page=${page}&pageSize=${pageSize}&includeAll=${includeAll}`,
    ),

  create: (req: CreateProductRequest) => api.post<ProductSearchResult>('/api/v1/products', req),

  update: (id: string, req: UpdateProductRequest) => api.put<ProductSearchResult>(`/api/v1/products/${id}`, req),
}
