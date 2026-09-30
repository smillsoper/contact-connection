import { create } from 'zustand'

interface AgentStateStore {
  agentStateCode: string
  agentStateExpiresAt: Date | null
  /** Supervisor status lock ("Locked by Sue: …") — the status picker is disabled while set. */
  lockMessage: string | null
  setAgentStateCode: (code: string, expiresAt?: Date | null) => void
  setLockMessage: (message: string | null) => void
}

export const useAgentStateStore = create<AgentStateStore>((set) => ({
  agentStateCode: 'unavailable',
  agentStateExpiresAt: null,
  lockMessage: null,
  setAgentStateCode: (agentStateCode, expiresAt = null) =>
    set({ agentStateCode, agentStateExpiresAt: expiresAt }),
  setLockMessage: (lockMessage) => set({ lockMessage }),
}))
