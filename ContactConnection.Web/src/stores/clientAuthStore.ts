import { create } from 'zustand'
import { persist } from 'zustand/middleware'

/** Client portal session (S181) — kept apart from the agent session so the two never mix in one browser. */
export interface ClientProfile {
  id: string
  email: string
  firstName: string
  lastName: string
  canPlayRecordings: boolean
  timeZone: string | null
  tenantTimeZone: string
  defaultDashboardId: string | null
  tenantName: string
  tenantSubdomain: string
  mfaEnabled: boolean
  /** The tenant's MFA setting — 'on' means required for client users; otherwise two-step is their choice. */
  mfaRequirement: 'on' | 'optional' | 'off' | string
}

interface ClientAuthState {
  token: string | null
  subdomain: string | null
  profile: ClientProfile | null
  setAuth: (token: string, subdomain: string, profile: ClientProfile) => void
  setProfile: (profile: ClientProfile) => void
  clear: () => void
}

export const useClientAuthStore = create<ClientAuthState>()(
  persist(
    (set) => ({
      token: null,
      subdomain: null,
      profile: null,
      setAuth: (token, subdomain, profile) => set({ token, subdomain, profile }),
      setProfile: (profile) => set({ profile }),
      clear: () => set({ token: null, profile: null }),
    }),
    { name: 'cc-client-auth' },
  ),
)
