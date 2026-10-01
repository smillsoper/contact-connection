import { api } from './client'

// Media Agency Phase A (S171) — agencies and each phone number's media assignments.

export interface MediaAgencyField { name: string; required: boolean }
export interface MediaAgency { id: string; name: string; isActive: boolean; fields: MediaAgencyField[] }

export type MediaMarketType = 'national' | 'local'

export interface MediaAssignment {
  id: string
  phoneNumberId: string
  marketType: MediaMarketType
  mediaAgencyId: string
  agencyName: string
  station: string
  mediaType: string | null
  adType: string | null
  startDate: string
  endDate: string | null
  isDefaultLocal: boolean
  fieldValues: Record<string, string>
  createdByName: string | null
  createdAt: string
  inEffect: boolean
}

export interface MediaAssignmentList {
  today: string
  currentAssignmentId: string | null
  assignments: MediaAssignment[]
}

export interface AssignmentInput {
  marketType?: MediaMarketType
  mediaAgencyId?: string
  station: string
  mediaType?: string | null
  adType?: string | null
  startDate?: string
  endDate?: string | null
  isDefaultLocal?: boolean
  fieldValues: Record<string, string>
}

export const mediaApi = {
  agencies: () => api.get<MediaAgency[]>('/api/v1/media-agencies'),
  createAgency: (name: string, fields: MediaAgencyField[]) => api.post<MediaAgency>('/api/v1/media-agencies', { name, fields }),
  updateAgency: (id: string, name: string, fields: MediaAgencyField[], isActive: boolean) =>
    api.put<MediaAgency>(`/api/v1/media-agencies/${id}`, { name, fields, isActive }),

  assignments: (phoneNumberId: string) => api.get<MediaAssignmentList>(`/api/v1/phone-numbers/${phoneNumberId}/media-assignments`),
  addAssignment: (phoneNumberId: string, input: AssignmentInput) =>
    api.post<MediaAssignment>(`/api/v1/phone-numbers/${phoneNumberId}/media-assignments`, input),
  updateAssignment: (id: string, input: AssignmentInput) => api.put<MediaAssignment>(`/api/v1/media-assignments/${id}`, input),
  deleteAssignment: (id: string) => api.delete<void>(`/api/v1/media-assignments/${id}`),
}

/** Suggestions for the media / ad type fields — free text is allowed too. */
export const MEDIA_TYPES = ['TV', 'Radio', 'Print', 'Digital', 'Direct Mail', 'Streaming', 'Podcast']
export const AD_TYPES = ['SF', 'LF', 'MF', 'PI', 'Paid']
