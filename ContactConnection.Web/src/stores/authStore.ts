import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface AuthState {
  token: string | null
  agentId: string | null
  tenantSubdomain: string | null
  role: string | null
  firstName: string | null
  lastName: string | null
  permissions: string[]
  landingPage: string | null
  setAuth: (
    token: string,
    agentId: string,
    tenantSubdomain: string,
    role?: string,
    firstName?: string,
    lastName?: string,
    permissions?: string[],
    landingPage?: string,
  ) => void
  clearAuth: () => void
  hasPermission: (permission: string) => boolean
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      token: null,
      agentId: null,
      tenantSubdomain: null,
      role: null,
      firstName: null,
      lastName: null,
      permissions: [],
      landingPage: null,
      setAuth: (token, agentId, tenantSubdomain, role, firstName, lastName, permissions, landingPage) =>
        set({
          token,
          agentId,
          tenantSubdomain,
          role: role ?? null,
          firstName: firstName ?? null,
          lastName: lastName ?? null,
          permissions: permissions ?? [],
          landingPage: landingPage ?? null,
        }),
      clearAuth: () =>
        set({ token: null, agentId: null, tenantSubdomain: null, role: null, firstName: null, lastName: null, permissions: [], landingPage: null }),
      hasPermission: (permission: string) => get().permissions.includes(permission),
    }),
    { name: 'cc-auth' },
  ),
)

// Map landing page keys to routes. "reports" is a pre-existing key name (LandingPage.Reports on
// the backend) that predates the Supervisor Dashboards feature and never had a real destination
// — found dangling (pointing at a /reports route that was never built) while wiring role-based
// gating for dashboards in Session 92; repointed at the actual live feature it now corresponds to.
export const LANDING_ROUTES: Record<string, string> = {
  agent_portal:    '/agent',
  admin_dashboard: '/admin',
  flows:           '/flows',
  telephony:       '/admin/telephony',
  reports:         '/dashboards',
}

export function getLandingRoute(landingPage: string | null): string {
  if (!landingPage) return '/agent'
  return LANDING_ROUTES[landingPage] ?? '/agent'
}
