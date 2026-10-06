import { api } from './client'

export interface DashboardSummary {
  id: string
  name: string
  is_shared: boolean
  /** S181: shown to client users, locked to scope_client_id (+ optionally some of its campaigns). */
  is_client_dashboard: boolean
  scope_client_id: string | null
  scope_campaign_ids: string[]
  created_by_agent_id: string
  created_at: string
  updated_at: string
}

export interface DashboardDetail extends DashboardSummary {
  layout: string
  /** Owner or tenant admin (and holds reports.manage) — anyone else can only save their own copy. */
  can_edit: boolean
}

export interface DashboardScope {
  isClientDashboard: boolean
  scopeClientId: string | null
  scopeCampaignIds: string[]
}

export const dashboardsApi = {
  list: () => api.get<DashboardSummary[]>('/api/v1/dashboards'),

  getDetail: (id: string) => api.get<DashboardDetail>(`/api/v1/dashboards/${id}`),

  create: (name: string, isShared: boolean, layout: string, scope?: DashboardScope) =>
    api.post<DashboardDetail>('/api/v1/dashboards', { name, isShared, layout, ...scope }),

  update: (id: string, name: string, isShared: boolean, layout: string, scope?: DashboardScope) =>
    api.put<DashboardDetail>(`/api/v1/dashboards/${id}`, { name, isShared, layout, ...scope }),

  delete: (id: string) => api.delete<void>(`/api/v1/dashboards/${id}`),
}
