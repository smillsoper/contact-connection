import { api } from './client'

// Disposition catalog (S181, docs/dispositions-kpi-plan.md): reporting categories, scoped dispositions, Unmapped list.

export interface DispositionCategory {
  id: string
  key: string | null
  name: string
  description: string | null
  salesOpportunity: boolean
  excludedFromKpis: boolean
  displayOrder: number
  isActive: boolean
  isSystem: boolean
  /** S181, retain-by-disposition campaigns: keep / conversation / discard; null = keep. */
  recordingAction: RecordingAction | null
  recordingRetentionDays: number | null
}

export type RecordingAction = 'keep' | 'conversation' | 'discard'

export interface Disposition {
  id: string
  name: string
  code: string | null
  categoryId: string
  clientId: string | null
  campaignId: string | null
  aliases: string[]
  displayOrder: number
  isActive: boolean
  interactions: number
  /** Null = its category's rule. */
  recordingAction: RecordingAction | null
  recordingRetentionDays: number | null
}

export interface UnmappedDisposition {
  text: string
  count: number
  productionCount: number
  lastSeen: string | null
  campaigns: { id: string; name: string }[]
}

export interface CategoryInput {
  name: string; description: string | null; salesOpportunity: boolean; excludedFromKpis: boolean; displayOrder?: number
  recordingAction?: RecordingAction | null; recordingRetentionDays?: number | null
}
export interface DispositionInput {
  name: string; code: string | null; categoryId: string; clientId: string | null; campaignId: string | null; aliases: string[]; displayOrder?: number
  recordingAction?: RecordingAction | null; recordingRetentionDays?: number | null
}

export const RECORDING_ACTION_LABEL: Record<RecordingAction, string> = {
  keep: 'Keep whole call', conversation: 'Keep conversation only', discard: 'Discard',
}

/** "Discard", "Keep 365 days", "Conversation only, campaign period"… — for the lists. */
export function describeRecordingRule(action: RecordingAction | null, days: number | null, inherit: string) {
  if (!action && !days) return inherit
  if (action === 'discard') return 'Discard'
  const what = action === 'conversation' ? 'Conversation only' : 'Keep'
  return days ? `${what}, ${days} days` : `${what}, campaign period`
}

export const dispositionsApi = {
  categories: () => api.get<DispositionCategory[]>('/api/v1/disposition-categories'),
  createCategory: (c: CategoryInput) => api.post<DispositionCategory>('/api/v1/disposition-categories', c),
  updateCategory: (id: string, c: CategoryInput) => api.put<DispositionCategory>(`/api/v1/disposition-categories/${id}`, c),
  setCategoryActive: (id: string, active: boolean) => api.post<DispositionCategory>(`/api/v1/disposition-categories/${id}/active`, { active }),

  list: () => api.get<Disposition[]>('/api/v1/dispositions'),
  forCampaign: (campaignId: string) => api.get<Disposition[]>(`/api/v1/dispositions/for-campaign/${campaignId}`),
  create: (d: DispositionInput) => api.post<{ disposition: Disposition; relinked: number }>('/api/v1/dispositions', d),
  update: (id: string, d: DispositionInput) => api.put<{ disposition: Disposition; relinked: number }>(`/api/v1/dispositions/${id}`, d),
  setActive: (id: string, active: boolean) => api.post<{ disposition: Disposition; relinked: number }>(`/api/v1/dispositions/${id}/active`, { active }),
  remove: (id: string) => api.delete<void>(`/api/v1/dispositions/${id}`),

  unmapped: () => api.get<UnmappedDisposition[]>('/api/v1/dispositions/unmapped'),
  resolve: (text: string, target: { dispositionId: string } | { create: DispositionInput }) =>
    api.post<{ relinked: number }>('/api/v1/dispositions/unmapped/resolve', { text, ...target }),
}
