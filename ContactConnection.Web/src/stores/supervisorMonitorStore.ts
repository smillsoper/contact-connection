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
  connecting: (agentName, mode) => set({ monitor: { agentName, mode, status: 'connecting' } }),
  connected: () => set((s) => (s.monitor ? { monitor: { ...s.monitor, status: 'connected' } } : s)),
  setMode: (mode) => set((s) => (s.monitor ? { monitor: { ...s.monitor, mode } } : s)),
  clear: () => set({ monitor: null }),
}))
