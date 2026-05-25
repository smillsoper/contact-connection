import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface PortalAuthState {
  token: string | null
  adminId: string | null
  email: string | null
  firstName: string | null
  lastName: string | null
  setAuth: (token: string, adminId: string, email: string, firstName: string, lastName: string) => void
  clearAuth: () => void
}

export const usePortalAuthStore = create<PortalAuthState>()(
  persist(
    (set) => ({
      token: null,
      adminId: null,
      email: null,
      firstName: null,
      lastName: null,
      setAuth: (token, adminId, email, firstName, lastName) =>
        set({ token, adminId, email, firstName, lastName }),
      clearAuth: () => set({ token: null, adminId: null, email: null, firstName: null, lastName: null }),
    }),
    { name: 'cc-portal-auth' },
  ),
)
