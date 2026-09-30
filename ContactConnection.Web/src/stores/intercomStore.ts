import { create } from 'zustand'

// Supervisor ↔ agent internal call (S167) — not a customer call: no call record, no call screen.
// The supervisor's side ('caller') auto-answers the server's INVITE; the agent's side ('callee')
// rings with Answer / Decline. Set by FlowPanel's hub listeners, driven by SoftphonePanel.
export interface IntercomCall {
  peerName: string
  role: 'caller' | 'callee'
  status: 'connecting' | 'ringing' | 'connected'
}

interface IntercomStore {
  call: IntercomCall | null
  start: (peerName: string, role: 'caller' | 'callee') => void
  setStatus: (status: IntercomCall['status']) => void
  clear: () => void
}

export const useIntercomStore = create<IntercomStore>((set) => ({
  call: null,
  start: (peerName, role) => set({ call: { peerName, role, status: 'connecting' } }),
  setStatus: (status) => set((s) => (s.call ? { call: { ...s.call, status } } : s)),
  clear: () => set({ call: null }),
}))
