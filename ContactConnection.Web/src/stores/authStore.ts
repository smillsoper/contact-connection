import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface AuthState {
  token: string | null
  agentId: string | null
  tenantSubdomain: string | null
  role: string | null
  firstName: string | null
  lastName: string | null
  setAuth: (
    token: string,
    agentId: string,
    tenantSubdomain: string,
    role?: string,
    firstName?: string,
    lastName?: string,
  ) => void
  clearAuth: () => void
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      token: null,
      agentId: null,
      tenantSubdomain: null,
      role: null,
      firstName: null,
      lastName: null,
      setAuth: (token, agentId, tenantSubdomain, role, firstName, lastName) =>
        set({ token, agentId, tenantSubdomain, role: role ?? null, firstName: firstName ?? null, lastName: lastName ?? null }),
      clearAuth: () =>
        set({ token: null, agentId: null, tenantSubdomain: null, role: null, firstName: null, lastName: null }),
    }),
    { name: 'cc-auth' },
  ),
)
