import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface PortalAuthState {
  token: string | null
  adminId: string | null
  email: string | null
  firstName: string | null
  lastName: string | null
  /** S184: 'owner' (everything) or 'support' (tenants only, no billing). Missing on sign-ins from before roles = owner. */
  platformRole: 'owner' | 'support' | null
  setAuth: (token: string, adminId: string, email: string, firstName: string, lastName: string, platformRole: 'owner' | 'support') => void
  clearAuth: () => void
}

/** Whether the signed-in Portal user is the Owner (S184). */
export const useIsPortalOwner = () => usePortalAuthStore((s) => s.platformRole !== 'support')

export const usePortalAuthStore = create<PortalAuthState>()(
  persist(
    (set) => ({
      token: null,
      adminId: null,
      email: null,
      firstName: null,
      lastName: null,
      platformRole: null,
      setAuth: (token, adminId, email, firstName, lastName, platformRole) =>
        set({ token, adminId, email, firstName, lastName, platformRole }),
      clearAuth: () => set({ token: null, adminId: null, email: null, firstName: null, lastName: null, platformRole: null }),
    }),
    { name: 'cc-portal-auth' },
  ),
)
