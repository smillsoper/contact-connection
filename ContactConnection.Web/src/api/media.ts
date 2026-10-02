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
  stationFacilityId: number | null
  stationLatitude: number | null
  stationLongitude: number | null
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
  /** Set when the station was picked from the FCC list; omit for free text. */
  stationFacilityId?: number | null
  mediaType?: string | null
  adType?: string | null
  startDate?: string
  endDate?: string | null
  isDefaultLocal?: boolean
  fieldValues: Record<string, string>
}

/** A station from the platform's FCC list (imported daily from the FCC LMS database). */
export interface BroadcastStation {
  facilityId: number
  callSign: string
  serviceCode: string
  communityCity: string | null
  communityState: string | null
  latitude: number
  longitude: number
  networkAffiliation: string | null
}

export const mediaApi = {
  searchStations: (q: string, service?: 'tv' | 'radio') =>
    api.get<BroadcastStation[]>(`/api/v1/broadcast-stations?q=${encodeURIComponent(q)}${service ? `&service=${service}` : ''}`),

  agencies: () => api.get<MediaAgency[]>('/api/v1/media-agencies'),
  createAgency: (name: string, fields: MediaAgencyField[]) => api.post<MediaAgency>('/api/v1/media-agencies', { name, fields }),
  updateAgency: (id: string, name: string, fields: MediaAgencyField[], isActive: boolean) =>
    api.put<MediaAgency>(`/api/v1/media-agencies/${id}`, { name, fields, isActive }),

  assignments: (phoneNumberId: string) => api.get<MediaAssignmentList>(`/api/v1/phone-numbers/${phoneNumberId}/media-assignments`),
  addAssignment: (phoneNumberId: string, input: AssignmentInput) =>
    api.post<MediaAssignment>(`/api/v1/phone-numbers/${phoneNumberId}/media-assignments`, input),
  updateAssignment: (id: string, input: AssignmentInput) => api.put<MediaAssignment>(`/api/v1/media-assignments/${id}`, input),
  deleteAssignment: (id: string) => api.delete<void>(`/api/v1/media-assignments/${id}`),
  changes: (phoneNumberId: string) => api.get<MediaAssignmentChange[]>(`/api/v1/phone-numbers/${phoneNumberId}/media-assignments/changes`),

  replayNumbers: () => api.get<ReplayNumber[]>('/api/v1/media-replay/numbers'),
  previewReplay: (input: ReplayInput) => api.post<MediaReplayPreview>('/api/v1/media-replay/preview', input),
  startReplay: (input: ReplayInput) => api.post<{ id: string }>('/api/v1/media-replay', input),
  replays: () => api.get<MediaReplayBatch[]>('/api/v1/media-replay'),
}

/** One change to a number's assignments (S171). */
export interface MediaAssignmentChange {
  id: string
  assignmentId: string
  action: 'created' | 'edited' | 'deleted' | 'ended' | 'default_changed'
  summary: string
  changedBy: string | null
  changedAt: string
}

export interface ReplayNumber { id: string; number: string; clientNumber: string | null; label: string | null }

/** Calls that started in [from, to), tenant-local "yyyy-MM-ddTHH:mm"; no phoneNumberId = all numbers. */
export interface ReplayInput { phoneNumberId?: string | null; from: string; to: string; reason?: string }

export interface MediaReplayPreview { calls: number; changedCalls: number; changes: { from: string; to: string; calls: number }[] }

export interface MediaReplayBatch {
  id: string
  status: 'pending' | 'running' | 'completed' | 'failed'
  reason: string
  requestedBy: string | null
  totalCalls: number
  processedCalls: number
  changedCalls: number
  error: string | null
  createdAt: string
  completedAt: string | null
  from: string
  to: string
  scope: string
}

/** Suggestions for the media / ad type fields — free text is allowed too. */
export const MEDIA_TYPES = ['TV', 'Radio', 'Print', 'Digital', 'Direct Mail', 'Streaming', 'Podcast']
export const AD_TYPES = ['SF', 'LF', 'MF', 'PI', 'Paid']
