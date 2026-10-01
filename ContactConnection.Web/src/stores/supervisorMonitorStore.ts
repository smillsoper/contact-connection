import { create } from 'zustand'
import type { MonitorMode } from '../api/supervisor'

// The supervisor's own listen-in, as their softphone sees it (S167). FlowPanel's hub listeners set
// it from ReceiveSupervisorConnecting / ReceiveMonitorEnded; SoftphonePanel auto-answers the eavesdrop
// INVITE while status is 'connecting' and shows the monitor card.
interface SupervisorMonitorStore {
  monitor: { agentName: string; mode: MonitorMode; status: 'connecting' | 'connected' } | null
  connecting: (agentName: string, mode: MonitorMode) => void
  connected: () => void
  setMode: (mode: MonitorMode) => void
  clear: () => void
}

export const useSupervisorMonitorStore = create<SupervisorMonitorStore>((set) => ({
  monitor: null,
  // The listen-in INVITE can be answered before this push arrives (S171) — don't knock a connected
  // monitor back to 'connecting' (that would re-arm the softphone for a call that already came).
  connecting: (agentName, mode) => set((s) => (s.monitor?.status === 'connected'
    ? { monitor: { ...s.monitor, agentName, mode } }
    : { monitor: { agentName, mode, status: 'connecting' } })),
  connected: () => set((s) => ({ monitor: s.monitor ? { ...s.monitor, status: 'connected' } : { agentName: '', mode: 'listen', status: 'connected' } })),
  setMode: (mode) => set((s) => (s.monitor ? { monitor: { ...s.monitor, mode } } : s)),
  clear: () => set({ monitor: null }),
}))
