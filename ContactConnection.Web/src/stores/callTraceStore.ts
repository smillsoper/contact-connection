import { create } from 'zustand'
import type { CallTraceStep, CaptureMode } from '../api/callTraces'

export interface TracePresetFilters {
  campaignId?: string
  flowId?: string
  dnis?: string
}

export interface TraceCallTab {
  callRecordId: string
  steps: CallTraceStep[]
  ended: boolean
}

export type TraceWindowStatus = 'configuring' | 'running' | 'stopped'

export interface TraceWindow {
  id: string
  preset: TracePresetFilters
  status: TraceWindowStatus
  subscriptionId?: string
  effectiveCaptureMode?: CaptureMode
  effectiveCaptureValue?: number
  stopReason?: string
  calls: TraceCallTab[]
  activeCallRecordId?: string
  left: number
  top: number
}

let nextOffset = 0
const WINDOW_ID = () => `trace-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`

interface CallTraceState {
  windows: TraceWindow[]
  openWindow: (preset?: TracePresetFilters) => string
  closeWindow: (id: string) => void
  focusWindow: (id: string) => void
  setRunning: (id: string, subscriptionId: string, mode: CaptureMode, value: number) => void
  addCallMatched: (id: string, callRecordId: string) => void
  addStep: (id: string, step: CallTraceStep) => void
  markCallEnded: (id: string, callRecordId: string) => void
  setStopped: (id: string, reason: string) => void
  setActiveCall: (id: string, callRecordId: string) => void
}

export const useCallTraceStore = create<CallTraceState>((set) => ({
  windows: [],

  openWindow: (preset) => {
    const id = WINDOW_ID()
    const offset = (nextOffset++ % 6) * 32
    set((s) => ({
      windows: [...s.windows, {
        id,
        preset: preset ?? {},
        status: 'configuring',
        calls: [],
        left: 120 + offset,
        top: 90 + offset,
      }],
    }))
    return id
  },

  closeWindow: (id) => set((s) => ({ windows: s.windows.filter((w) => w.id !== id) })),

  focusWindow: (id) => set((s) => {
    const win = s.windows.find((w) => w.id === id)
    if (!win) return s
    return { windows: [...s.windows.filter((w) => w.id !== id), win] }
  }),

  setRunning: (id, subscriptionId, mode, value) => set((s) => ({
    windows: s.windows.map((w) => w.id === id
      ? { ...w, status: 'running', subscriptionId, effectiveCaptureMode: mode, effectiveCaptureValue: value }
      : w),
  })),

  addCallMatched: (id, callRecordId) => set((s) => ({
    windows: s.windows.map((w) => {
      if (w.id !== id) return w
      if (w.calls.some((c) => c.callRecordId === callRecordId)) return w
      return {
        ...w,
        calls: [...w.calls, { callRecordId, steps: [], ended: false }],
        activeCallRecordId: w.activeCallRecordId ?? callRecordId,
      }
    }),
  })),

  addStep: (id, step) => set((s) => ({
    windows: s.windows.map((w) => {
      if (w.id !== id) return w
      const exists = w.calls.some((c) => c.callRecordId === step.callRecordId)
      const calls = exists
        ? w.calls.map((c) => c.callRecordId === step.callRecordId ? { ...c, steps: [...c.steps, step] } : c)
        : [...w.calls, { callRecordId: step.callRecordId, steps: [step], ended: false }]
      return { ...w, calls, activeCallRecordId: w.activeCallRecordId ?? step.callRecordId }
    }),
  })),

  markCallEnded: (id, callRecordId) => set((s) => ({
    windows: s.windows.map((w) => w.id === id
      ? { ...w, calls: w.calls.map((c) => c.callRecordId === callRecordId ? { ...c, ended: true } : c) }
      : w),
  })),

  setStopped: (id, reason) => set((s) => ({
    windows: s.windows.map((w) => w.id === id ? { ...w, status: 'stopped', stopReason: reason } : w),
  })),

  setActiveCall: (id, callRecordId) => set((s) => ({
    windows: s.windows.map((w) => w.id === id ? { ...w, activeCallRecordId: callRecordId } : w),
  })),
}))
