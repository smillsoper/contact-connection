import { create } from 'zustand'
import type { FlowNodeState } from '../types/flow'

export interface FlowSessionEntry {
  id: string
  label: string
  sessionId: string
  initialNode: FlowNodeState
}

interface FlowSessionsState {
  sessions: FlowSessionEntry[]
  activeSessionId: string | null
  addSession: (entry: FlowSessionEntry) => void
  removeSession: (id: string) => void
  setActiveSession: (id: string) => void
}

export const useFlowSessionsStore = create<FlowSessionsState>((set) => ({
  sessions: [],
  activeSessionId: null,

  addSession: (entry) =>
    set((s) => ({
      sessions: [...s.sessions, entry],
      // Auto-switch to new tab
      activeSessionId: entry.id,
    })),

  removeSession: (id) =>
    set((s) => {
      const remaining = s.sessions.filter((t) => t.id !== id)
      const nextActive =
        s.activeSessionId === id
          ? remaining.length > 0
            ? remaining[remaining.length - 1].id
            : null
          : s.activeSessionId
      return { sessions: remaining, activeSessionId: nextActive }
    }),

  setActiveSession: (id) => set({ activeSessionId: id }),
}))
