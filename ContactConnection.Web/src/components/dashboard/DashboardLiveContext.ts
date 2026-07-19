import { createContext, useContext } from 'react'

export interface AgentStateEvent {
  agentId: string
  code: string
  label: string
  since: string
}

// Broadcasts the most recent supervisor:{tenantId} agent-state push to every widget on the
// canvas. Widgets decide for themselves whether the event is relevant (e.g. AgentListWidget
// patches a matching row; AgentStateCounterWidget just re-fetches its aggregate).
export const DashboardLiveContext = createContext<AgentStateEvent | null>(null)

export function useDashboardLiveAgentState() {
  return useContext(DashboardLiveContext)
}
