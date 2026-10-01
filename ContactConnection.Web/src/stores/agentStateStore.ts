import { create } from 'zustand'

interface AgentStateStore {
  agentStateCode: string
  /** Server label — what a custom unavailable code ("unavailable_custom") displays as. */
  agentStateLabel: string | null
  agentStateExpiresAt: Date | null
  /** Supervisor status lock ("Locked by Sue: …") — the status picker is disabled while set. */
  lockMessage: string | null
  setAgentStateCode: (code: string, expiresAt?: Date | null, label?: string | null) => void
  setLockMessage: (message: string | null) => void
}

export const useAgentStateStore = create<AgentStateStore>((set) => ({
  agentStateCode: 'unavailable',
  agentStateLabel: null,
  agentStateExpiresAt: null,
  lockMessage: null,
  setAgentStateCode: (agentStateCode, expiresAt = null, label = null) =>
    set({ agentStateCode, agentStateExpiresAt: expiresAt, agentStateLabel: label }),
  setLockMessage: (lockMessage) => set({ lockMessage }),
}))
