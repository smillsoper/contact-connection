import { create } from 'zustand'

export type SipRegistrationStatus = 'idle' | 'registering' | 'registered' | 'failed'

interface SipState {
  sipExtension: string | null
  sipPassword: string | null   // plaintext, memory only — never written to localStorage
  registrationStatus: SipRegistrationStatus
  setSipCredentials: (extension: string, password: string) => void
  setRegistrationStatus: (status: SipRegistrationStatus) => void
  clear: () => void
}

// Intentionally NOT using persist middleware — SIP password must not survive page refresh.
// Agent gets a fresh one from the login response on the next login.
export const useSipStore = create<SipState>((set) => ({
  sipExtension: null,
  sipPassword: null,
  registrationStatus: 'idle',
  setSipCredentials: (extension, password) =>
    set({ sipExtension: extension, sipPassword: password, registrationStatus: 'idle' }),
  setRegistrationStatus: (status) => set({ registrationStatus: status }),
  clear: () =>
    set({ sipExtension: null, sipPassword: null, registrationStatus: 'idle' }),
}))
