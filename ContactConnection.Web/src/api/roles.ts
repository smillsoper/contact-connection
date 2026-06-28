import { api } from './client'

export interface Role {
  id: string
  name: string
  isBuiltIn: boolean
  permissions: string[]
  defaultLandingPage: string
  createdAt: string
  updatedAt: string
}

export interface PermissionCatalogItem {
  key: string
  group: string
}

export interface LandingPageOption {
  key: string
  label: string
}

export const PERMISSION_LABELS: Record<string, string> = {
  'agents.view':           'View Agents',
  'agents.manage':         'Manage Agents',
  'roles.manage':          'Manage Roles',
  'flows.view':            'View Flows',
  'flows.manage':          'Manage Flows',
  'flows.publish':         'Publish Flows',
  'telephony.view':        'View Telephony',
  'telephony.manage':      'Manage Telephony',
  'calls.view':            'View Call Records',
  'calls.export':          'Export Call Data',
  'integrations.view':     'View Integrations',
  'integrations.manage':   'Manage Integrations',
  'supervisor.monitor':    'Live Call Monitoring',
  'supervisor.override':   'Override Commitments',
  'reports.view':          'View Reports',
  'reports.manage':        'Manage Reports',
}

export const PERMISSION_GROUPS: Record<string, string[]> = {
  Agents:      ['agents.view', 'agents.manage'],
  Roles:       ['roles.manage'],
  Flows:       ['flows.view', 'flows.manage', 'flows.publish'],
  Telephony:   ['telephony.view', 'telephony.manage'],
  'Call Records': ['calls.view', 'calls.export'],
  Integrations: ['integrations.view', 'integrations.manage'],
  Supervisor:  ['supervisor.monitor', 'supervisor.override'],
  Reports:     ['reports.view', 'reports.manage'],
}

export const LANDING_PAGE_LABELS: Record<string, string> = {
  agent_portal:    'Agent Portal',
  admin_dashboard: 'Admin Dashboard',
  flows:           'Flow Designer',
  telephony:       'Telephony',
  reports:         'Reports',
}

export const rolesApi = {
  getAll:  ()         => api.get<Role[]>('/api/v1/roles'),
  getById: (id: string) => api.get<Role>(`/api/v1/roles/${id}`),
  create:  (body: { name: string; permissions: string[]; defaultLandingPage: string }) =>
    api.post<Role>('/api/v1/roles', body),
  update:  (id: string, body: { name: string; permissions: string[]; defaultLandingPage: string }) =>
    api.put<Role>(`/api/v1/roles/${id}`, body),
  delete:  (id: string) => api.delete<void>(`/api/v1/roles/${id}`),
}
