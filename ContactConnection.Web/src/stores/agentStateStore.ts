import { create } from 'zustand'

interface AgentStateStore {
  agentStateCode: string
  agentStateExpiresAt: Date | null
  setAgentStateCode: (code: string, expiresAt?: Date | null) => void
}

export const useAgentStateStore = create<AgentStateStore>((set) => ({
  agentStateCode: 'unavailable',
  agentStateExpiresAt: null,
  setAgentStateCode: (agentStateCode, expiresAt = null) =>
    set({ agentStateCode, agentStateExpiresAt: expiresAt }),
}))
