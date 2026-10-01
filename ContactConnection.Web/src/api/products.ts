import { api } from './client'

export type ProductInventoryStatus = 'Available' | 'CanBackorder' | 'NoBackorder' | 'Discontinued' | 'OutOfStock'

export interface ProductSearchResult {
  id: string
  sku: string
  description: string
  weight: number
  searchable: boolean
  /** Tax provider product code (e.g. Avalara PF050714); null = campaign default. */
  taxCode?: string | null
  /** Scope (S169) — no client = tenant-wide; empty campaignIds = all of the client's campaigns. */
  clientId?: string | null
  campaignIds?: string[]
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
  taxCode?: string | null
  clientId?: string | null
  campaignIds?: string[]
}

export interface UpdateProductRequest {
  weight: number
  inventoryStatus: ProductInventoryStatus
  qtyAvailable: number
  decrementOnOrder: boolean
  minimumQty: number
  searchable: boolean
  taxCode?: string | null
  clientId?: string | null
  campaignIds?: string[]
}

/** Client/campaign narrowing (S169). `callRecordId` = only products with offers that fit that call. */
export interface ProductSearchScope {
  clientId?: string
  campaignId?: string
  tenantWideOnly?: boolean
  callRecordId?: string
}

export const productsApi = {
  // Free-text search over description/SKU, optionally scoped to a category/attribute set.
  // Also used as the admin "list all products" call (empty query, includeAll=true so a product
  // toggled non-searchable doesn't disappear from the admin's own list).
  search: (query = '', page = 1, pageSize = 50, includeAll = false, scope: ProductSearchScope = {}) => {
    const extra = Object.entries(scope)
      .filter(([, v]) => v !== undefined && v !== '' && v !== false)
      .map(([k, v]) => `&${k}=${encodeURIComponent(String(v))}`).join('')
    return api.get<ProductSearchResult[]>(
      `/api/v1/products?query=${encodeURIComponent(query)}&page=${page}&pageSize=${pageSize}&includeAll=${includeAll}${extra}`,
    )
  },

  get: (id: string) => api.get<ProductSearchResult>(`/api/v1/products/${id}`),

  create: (req: CreateProductRequest) => api.post<ProductSearchResult>('/api/v1/products', req),

  update: (id: string, req: UpdateProductRequest) => api.put<ProductSearchResult>(`/api/v1/products/${id}`, req),
}
