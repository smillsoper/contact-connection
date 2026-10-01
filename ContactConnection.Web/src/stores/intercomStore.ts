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
  begin: (role: IntercomCall['role'], status: IntercomCall['status']) => void
  clear: () => void
}

export const useIntercomStore = create<IntercomStore>((set) => ({
  call: null,
  // The call's INVITE can arrive before this push (S171) — if the softphone already has this call
  // ringing / connected, just fill in the name; never knock it back to 'connecting'.
  start: (peerName, role) => set((s) => (s.call && s.call.role === role && s.call.status !== 'connecting'
    ? { call: { ...s.call, peerName } }
    : { call: { peerName, role, status: 'connecting' } })),
  setStatus: (status) => set((s) => (s.call ? { call: { ...s.call, status } } : s)),
  /** The INVITE arrived first: the call exists before the push names it. */
  begin: (role, status) => set((s) => (s.call ? { call: { ...s.call, role, status } } : { call: { peerName: '', role, status } })),
  clear: () => set({ call: null }),
}))
