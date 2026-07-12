import { api } from './client'

export interface BlockListEntry {
  id: string
  phoneNumber: string
  matchType: 'exact' | 'prefix'
  reason: string | null
  expiresAt: string | null
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export const blockListApi = {
  getAll: () => api.get<BlockListEntry[]>('/api/v1/block-list'),
  add: (body: { phoneNumber: string; matchType?: string; reason?: string; expiresAt?: string }) =>
    api.post<BlockListEntry>('/api/v1/block-list', body),
  update: (id: string, body: { reason?: string; expiresAt?: string }) =>
    api.put<BlockListEntry>(`/api/v1/block-list/${id}`, body),
  remove: (id: string) => api.delete<void>(`/api/v1/block-list/${id}`),
}
