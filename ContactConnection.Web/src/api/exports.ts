import { api } from './client'
import { useAuthStore } from '../stores/authStore'
import { getSubdomainFromHostname } from '../utils/subdomain'

// Export Worker (S180): export definitions, the vendor lifecycle, Preview, test files / Run now, run history + download.

export type ExportStatus = 'draft' | 'testing' | 'approved' | 'live' | 'paused'
export type RowGrain = 'call' | 'interaction' | 'cart_line'
export type LayoutMode = 'columns' | 'document'
export type ExportFormat = 'delimited' | 'fixed' | 'xlsx'
export type DataSource = 'production' | 'practice'

export interface ExportColumn {
  header: string
  template: string
  width?: number | null
  align?: 'left' | 'right'
  padChar?: string | null
  quote?: 'auto' | 'always' | 'never'
  type?: 'text' | 'number'
}

export interface ExportSpec {
  clientId: string | null
  campaignIds: string[]
  mediaAgency: string | null
  rowGrain: RowGrain
  condition: string | null
  layoutMode: LayoutMode
  format: ExportFormat
  delimiter: string
  includeHeader: boolean
  lineEnding: 'crlf' | 'lf'
  columns: ExportColumn[]
  documentTemplate: string | null
  skipBlankLines: boolean
  fileNameTemplate: string
  testFileSuffix: string
  timeZone: string
}

export interface ExportSchedule {
  frequency: 'daily' | 'monthly'
  daysOfWeek: number[]
  dayOfMonth: number
  timeOfDay: string
  timeZone: string
  window: 'previous_day' | 'previous_week' | 'previous_month' | 'last_hours' | 'since_last_run'
  lastHours: number
  autoDeliver: boolean
}

export type DeliveryType = 'sftp' | 'ftps' | 'email'

export interface DeliveryTarget {
  id: string
  name: string
  type: DeliveryType
  enabled: boolean
  host?: string | null
  port?: number | null
  username?: string | null
  passwordCredential?: string | null
  privateKeyCredential?: string | null
  hostKeyFingerprint?: string | null
  ftpsImplicit?: boolean
  certificateFingerprint?: string | null
  remoteDirectory?: string | null
  emailTo: string[]
  emailSubject?: string | null
  encryption: 'none' | 'pgp' | 'zip'
  pgpPublicKey?: string | null
  zipPasswordCredential?: string | null
}

export interface ExportDeliveryRow {
  id: string
  targetId: string
  targetName: string
  targetType: DeliveryType
  status: 'queued' | 'running' | 'succeeded' | 'failed'
  attempts: number
  maxAttempts: number
  nextAttemptAt: string
  error: string | null
  sentAs: string | null
  requestedByName: string | null
  queuedAt: string
  deliveredAt: string | null
}

export interface NextRun { runAt: string; windowStart: string; windowEnd: string }

export interface ConnectionTest { success: boolean; message: string; fingerprint: string | null; matchesPinned: boolean | null }

export interface ActivityRow { id: string; runId: string | null; action: string; actorName: string | null; detail: string | null; at: string }

export interface StarterTemplate { key: string; name: string; description: string; notes: string; spec: ExportSpec; schedule: ExportSchedule }

export interface ExportRunRow {
  id: string
  definitionId: string
  definitionName: string
  specRevision: number
  kind: 'manual' | 'test' | 'rerun' | 'scheduled'
  isTest: boolean
  dataSource: DataSource
  windowStart: string
  windowEnd: string
  status: 'queued' | 'running' | 'succeeded' | 'failed'
  attempts: number
  error: string | null
  requestedByName: string | null
  rowCount: number | null
  callCount: number | null
  fileName: string | null
  fileSize: number | null
  sha256: string | null
  queuedAt: string
  startedAt: string | null
  finishedAt: string | null
  scheduledFor: string | null
  deliver: boolean
  fileDeletedAt: string | null
  deliveries: ExportDeliveryRow[]
}

export interface ExportDefinition {
  id: string
  name: string
  description: string | null
  status: ExportStatus
  spec: ExportSpec
  specRevision: number
  changedSinceApproval: boolean
  approval: {
    at: string; vendorContact: string; recordedBy: string; note: string | null; runId: string; specRevision: number
  } | null
  schedule: ExportSchedule | null
  deliveryTargets: DeliveryTarget[]
  lastScheduledFor: string | null
  scheduleDescription: string | null
  nextRuns: NextRun[]
  createdAt: string
  updatedAt: string
  lastRun: ExportRunRow | null
}

export interface PreviewResult {
  success: boolean
  error: string | null
  rowCount: number
  callCount: number
  truncated: boolean
  text: string
  fileName: string
  grid: boolean
}

export interface VersionRow {
  versionNumber: number
  createdByName: string
  changeSummary: string | null
  createdAt: string
  isActive: boolean
}

export type LifecycleAction = 'start_testing' | 'approve' | 'go_live' | 'pause' | 'back_to_draft'

export const defaultSpec = (timeZone: string): ExportSpec => ({
  clientId: null, campaignIds: [], mediaAgency: null, rowGrain: 'call', condition: null,
  layoutMode: 'columns', format: 'delimited', delimiter: ',', includeHeader: true, lineEnding: 'crlf',
  columns: [
    { header: 'Date', template: "{{ call.started_at | format_time: 'MM/dd/yyyy' }}" },
    { header: 'Time', template: "{{ call.started_at | format_time: 'HH:mm:ss' }}" },
    { header: 'Caller', template: '{{ call.ani }}' },
    { header: 'Campaign', template: '{{ call.campaign.name }}' },
    { header: 'Order', template: '{{ call.order_number }}' },
  ],
  documentTemplate: null, skipBlankLines: true,
  fileNameTemplate: "{{ export.name }}_{{ window.end_inclusive | format_time: 'yyyyMMdd' }}.csv",
  testFileSuffix: '_TEST', timeZone,
})

export const exportsApi = {
  list: () => api.get<ExportDefinition[]>('/api/v1/exports'),
  get: (id: string) => api.get<ExportDefinition>(`/api/v1/exports/${id}`),
  create: (name: string, description: string | null, spec: ExportSpec) =>
    api.post<ExportDefinition>('/api/v1/exports', { name, description, spec }),
  update: (id: string, name: string, description: string | null, spec: ExportSpec) =>
    api.put<ExportDefinition>(`/api/v1/exports/${id}`, { name, description, spec }),
  remove: (id: string) => api.delete<void>(`/api/v1/exports/${id}`),
  versions: (id: string) => api.get<VersionRow[]>(`/api/v1/exports/${id}/versions`),
  lifecycle: (id: string, action: LifecycleAction, extra?: { vendorContact?: string; note?: string; runId?: string }) =>
    api.post<ExportDefinition>(`/api/v1/exports/${id}/lifecycle`, { action, ...extra }),
  preview: (body: { spec: ExportSpec; name: string; from: string; to: string; dataSource: DataSource; isTest: boolean; maxCalls: number }) =>
    api.post<PreviewResult>('/api/v1/exports/preview', body),
  runs: (id: string) => api.get<ExportRunRow[]>(`/api/v1/exports/${id}/runs`),
  queueRun: (id: string, body: { from: string; to: string; isTest: boolean; dataSource: DataSource; deliver?: boolean }) =>
    api.post<ExportRunRow>(`/api/v1/exports/${id}/runs`, body),
  rerun: (runId: string, deliver = false) => api.post<ExportRunRow>(`/api/v1/export-runs/${runId}/rerun`, { deliver }),
  templates: () => api.get<StarterTemplate[]>('/api/v1/exports/templates'),
  saveSchedule: (id: string, schedule: ExportSchedule | null) => api.put<ExportDefinition>(`/api/v1/exports/${id}/schedule`, { schedule }),
  schedulePreview: (schedule: ExportSchedule, dataTimeZone: string) =>
    api.post<{ error: string | null; description: string | null; nextRuns: NextRun[] }>('/api/v1/exports/schedule-preview', { schedule, dataTimeZone }),
  saveTargets: (id: string, targets: DeliveryTarget[]) => api.put<ExportDefinition>(`/api/v1/exports/${id}/delivery-targets`, { targets }),
  testTarget: (id: string, target: DeliveryTarget) => api.post<ConnectionTest>(`/api/v1/exports/${id}/delivery-targets/test`, { target }),
  send: (runId: string, targetIds: string[]) => api.post<void>(`/api/v1/export-runs/${runId}/send`, { targetIds }),
  retryDelivery: (deliveryId: string) => api.post<void>(`/api/v1/export-deliveries/${deliveryId}/retry`),
  activity: (id: string) => api.get<ActivityRow[]>(`/api/v1/exports/${id}/activity`),

  async download(run: ExportRunRow) {
    const { token, tenantSubdomain } = useAuthStore.getState()
    const subdomain = getSubdomainFromHostname() ?? tenantSubdomain
    const res = await fetch(`/api/v1/export-runs/${run.id}/download`, {
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(subdomain ? { 'X-Tenant-Subdomain': subdomain } : {}),
      },
    })
    if (!res.ok) throw new Error((await res.text()) || res.statusText)
    const url = URL.createObjectURL(await res.blob())
    const a = document.createElement('a')
    a.href = url
    a.download = run.fileName ?? 'export'
    a.click()
    URL.revokeObjectURL(url)
  },
}

export const STATUS_STYLE: Record<ExportStatus, string> = {
  draft: 'bg-gray-700 text-gray-300',
  testing: 'bg-amber-900/60 text-amber-300',
  approved: 'bg-sky-900/60 text-sky-300',
  live: 'bg-emerald-900/60 text-emerald-300',
  paused: 'bg-gray-700 text-amber-300',
}

export const STATUS_LABEL: Record<ExportStatus, string> = {
  draft: 'Draft', testing: 'Testing with vendor', approved: 'Vendor approved', live: 'Live', paused: 'Paused',
}

export const defaultSchedule = (): ExportSchedule => ({
  frequency: 'daily', daysOfWeek: [], dayOfMonth: 1, timeOfDay: '02:00', timeZone: 'America/Los_Angeles',
  window: 'previous_day', lastHours: 24, autoDeliver: true,
})

export const newTarget = (type: DeliveryType): DeliveryTarget => ({
  id: crypto.randomUUID(), name: '', type, enabled: true, emailTo: [], encryption: 'none',
  port: null, ftpsImplicit: false,
})
