import { useState, useEffect, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'
import { useAuthStore } from '../stores/authStore'
import { flowsApi } from '../api/flows'
import type { FlowNodeState } from '../types/flow'
import NodeDisplay from './NodeDisplay'

type PanelState =
  | { phase: 'idle' }
  | { phase: 'loading' }
  | { phase: 'running'; node: FlowNodeState }
  | { phase: 'error'; message: string }

export default function FlowPanel() {
  const { token, tenantSubdomain } = useAuthStore()
  const [state, setState] = useState<PanelState>({ phase: 'idle' })
  const [advancing, setAdvancing] = useState(false)
  const [flows, setFlows] = useState<{ id: string; name: string }[]>([])
  const [selectedFlowId, setSelectedFlowId] = useState('')
  const [hub, setHub] = useState<signalR.HubConnection | null>(null)

  // Load available flows on mount
  useEffect(() => {
    flowsApi.list().then(setFlows).catch(console.error)
  }, [])

  // Establish SignalR connection
  useEffect(() => {
    if (!token) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/flow?access_token=${token}`, {
        headers: { 'X-Tenant-Subdomain': tenantSubdomain ?? '' },
      })
      .withAutomaticReconnect()
      .build()

    connection.start().catch(console.error)
    setHub(connection)

    return () => {
      connection.stop()
    }
  }, [token, tenantSubdomain])

  // Join session room when a session becomes active
  useEffect(() => {
    if (!hub || state.phase !== 'running') return
    hub.invoke('JoinSession', state.node.sessionId).catch(console.error)
    return () => {
      hub.invoke('LeaveSession', state.node.sessionId).catch(console.error)
    }
  }, [hub, state])

  async function startSession() {
    if (!selectedFlowId) return
    setState({ phase: 'loading' })
    try {
      const node = await flowsApi.startSession({ flowId: selectedFlowId })
      setState({ phase: 'running', node })
    } catch (e) {
      setState({ phase: 'error', message: String(e) })
    }
  }

  const advance = useCallback(
    async (input?: string) => {
      if (state.phase !== 'running') return
      setAdvancing(true)
      try {
        const next = await flowsApi.advance(state.node.sessionId, { inputValue: input })
        setState({ phase: 'running', node: next })
      } catch (e) {
        setState({ phase: 'error', message: String(e) })
      } finally {
        setAdvancing(false)
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [state],
  )

  const jump = useCallback(
    async (sectionNodeId: string) => {
      if (state.phase !== 'running') return
      setAdvancing(true)
      try {
        const next = await flowsApi.advance(state.node.sessionId, { jumpToSectionNodeId: sectionNodeId })
        setState({ phase: 'running', node: next })
      } catch (e) {
        setState({ phase: 'error', message: String(e) })
      } finally {
        setAdvancing(false)
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [state],
  )

  return (
    <div className="flex flex-col h-full">
      {/* Flow selector toolbar */}
      <div className="flex items-center gap-3 px-4 py-3 border-b border-gray-800 shrink-0">
        <select
          value={selectedFlowId}
          onChange={(e) => setSelectedFlowId(e.target.value)}
          className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 border border-gray-700"
        >
          <option value="">Select flow…</option>
          {flows.map((f) => (
            <option key={f.id} value={f.id}>{f.name}</option>
          ))}
        </select>

        <button
          onClick={startSession}
          disabled={!selectedFlowId || state.phase === 'loading' || state.phase === 'running'}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded-lg px-4 py-1.5 text-sm font-medium transition-colors"
        >
          Start
        </button>

        {state.phase === 'running' && (
          <button
            onClick={() => setState({ phase: 'idle' })}
            className="text-gray-400 hover:text-white text-sm transition-colors ml-auto"
          >
            End session
          </button>
        )}
      </div>

      {/* Main content */}
      <div className="flex-1 overflow-y-auto">
        {state.phase === 'idle' && (
          <div className="flex items-center justify-center h-full text-gray-600 text-sm">
            Select a flow and press Start
          </div>
        )}

        {state.phase === 'loading' && (
          <div className="flex items-center justify-center h-full text-gray-500 text-sm">
            Starting session…
          </div>
        )}

        {state.phase === 'error' && (
          <div className="flex items-center justify-center h-full text-red-400 text-sm px-6 text-center">
            {state.message}
          </div>
        )}

        {state.phase === 'running' && (
          <NodeDisplay
            node={state.node}
            onAdvance={advance}
            onJump={jump}
            advancing={advancing}
          />
        )}
      </div>
    </div>
  )
}
