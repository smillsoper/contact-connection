import { api } from './client'

export interface DashboardSummary {
  id: string
  name: string
  is_shared: boolean
  created_by_agent_id: string
  created_at: string
  updated_at: string
}

export interface DashboardDetail extends DashboardSummary {
  layout: string
}

export const dashboardsApi = {
  list: () => api.get<DashboardSummary[]>('/api/v1/dashboards'),

  getDetail: (id: string) => api.get<DashboardDetail>(`/api/v1/dashboards/${id}`),

  create: (name: string, isShared: boolean, layout: string) =>
    api.post<DashboardDetail>('/api/v1/dashboards', { name, isShared, layout }),

  update: (id: string, name: string, isShared: boolean, layout: string) =>
    api.put<DashboardDetail>(`/api/v1/dashboards/${id}`, { name, isShared, layout }),

  delete: (id: string) => api.delete<void>(`/api/v1/dashboards/${id}`),
}
