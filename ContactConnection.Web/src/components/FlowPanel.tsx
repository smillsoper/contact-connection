import { useState, useEffect, useCallback, useRef } from 'react'
import * as signalR from '@microsoft/signalr'
import { useAuthStore } from '../stores/authStore'
import { useCallStore } from '../stores/callStore'
import { useFlowSessionsStore, type FlowSessionEntry } from '../stores/flowSessionsStore'
import { flowsApi, type AddressValidationResult, type ZipLookupResult, type AutocompleteSuggestion, type AutocompleteSelectionResult } from '../api/flows'
import type { FlowNodeState } from '../types/flow'
import NodeDisplay from './NodeDisplay'
import AddressValidationModal from './AddressValidationModal'

// ── Per-session view ──────────────────────────────────────────────────────────
// Encapsulates all session machinery for one flow tab. Closes itself (via onEnd)
// when the flow reaches its end node — the only valid exit path.

type SessionState =
  | { phase: 'running'; node: FlowNodeState }
  | { phase: 'error'; message: string }
  | { phase: 'ending' }   // end node reached, brief display before close

interface FlowSessionViewProps {
  entry: FlowSessionEntry
  hub: signalR.HubConnection | null
  onEnd: () => void
}

function FlowSessionView({ entry, hub, onEnd }: FlowSessionViewProps) {
  const [state, setState] = useState<SessionState>({ phase: 'running', node: entry.initialNode })
  const [advancing, setAdvancing] = useState(false)
  const [validating, setValidating] = useState(false)
  const [zipLookupResult, setZipLookupResult] = useState<ZipLookupResult | null>(null)
  const [zipLookupPending, setZipLookupPending] = useState(false)
  const [autocompleteSuggestions, setAutocompleteSuggestions] = useState<AutocompleteSuggestion[]>([])
  const [autocompletePending, setAutocompletePending] = useState(false)
  const [autocompleteSelection, setAutocompleteSelection] = useState<AutocompleteSelectionResult | null>(null)
  const [validationModal, setValidationModal] = useState<{
    result: AddressValidationResult
    address: Record<string, string>
  } | null>(null)
  const onEndRef = useRef(onEnd)
  useEffect(() => { onEndRef.current = onEnd })

  // Join SignalR session room for live updates
  useEffect(() => {
    if (!hub) return
    hub.invoke('JoinSession', entry.sessionId).catch(console.error)
    return () => { hub.invoke('LeaveSession', entry.sessionId).catch(console.error) }
  }, [hub, entry.sessionId])

  // Detect end node → transition to ending phase
  useEffect(() => {
    if (state.phase === 'running' && state.node.nodeType === 'end') {
      setState({ phase: 'ending' })
    }
  }, [state])

  // Start close timer exactly once when entering ending phase
  useEffect(() => {
    if (state.phase !== 'ending') return
    const timer = setTimeout(() => onEndRef.current(), 2000)
    return () => clearTimeout(timer)
  }, [state.phase])

  const advance = useCallback(
    async (input?: string) => {
      if (state.phase !== 'running') return
      setAdvancing(true)
      try {
        const next = await flowsApi.advance(entry.sessionId, { inputValue: input })
        setState({ phase: 'running', node: next })
      } catch (e) {
        setState({ phase: 'error', message: String(e) })
      } finally {
        setAdvancing(false)
      }
    },
    [state, entry.sessionId],
  )

  const jump = useCallback(
    async (sectionNodeId: string) => {
      if (state.phase !== 'running') return
      setAdvancing(true)
      try {
        const next = await flowsApi.advance(entry.sessionId, { jumpToSectionNodeId: sectionNodeId })
        setState({ phase: 'running', node: next })
      } catch (e) {
        setState({ phase: 'error', message: String(e) })
      } finally {
        setAdvancing(false)
      }
    },
    [state, entry.sessionId],
  )

  const validateAddress = useCallback(
    async (address: Record<string, string>) => {
      if (state.phase !== 'running') return
      setValidating(true)
      try {
        const result = await flowsApi.validateAddress(entry.sessionId, address)
        if (result.outcomeKey === 'exact_match') {
          const finalAddress = { ...address, ...(result.correctedFields ?? {}) }
          await advance(JSON.stringify(finalAddress))
        } else {
          setValidationModal({ result, address })
        }
      } catch (err) {
        setValidationModal({
          result: {
            outcomeKey: 'error',
            outcomeLabel: 'Validation Error',
            message: err instanceof Error ? err.message : 'An unexpected error occurred calling the address validation API.',
            correctedFields: null,
            matches: null,
          },
          address,
        })
      } finally {
        setValidating(false)
      }
    },
    [state, advance, entry.sessionId],
  )

  const lookupZip = useCallback(
    async (zip: string) => {
      if (state.phase !== 'running') return
      setZipLookupPending(true)
      try {
        const result = await flowsApi.lookupZip(entry.sessionId, zip)
        setZipLookupResult(result)
      } catch { /* best-effort */ }
      finally { setZipLookupPending(false) }
    },
    [state, entry.sessionId],
  )

  const searchAutocomplete = useCallback(
    async (text: string, sessionToken: string) => {
      if (state.phase !== 'running') return
      setAutocompletePending(true)
      try {
        const result = await flowsApi.autocompleteAddress(entry.sessionId, text, sessionToken)
        setAutocompleteSuggestions(result.suggestions)
      } catch { /* best-effort */ }
      finally { setAutocompletePending(false) }
    },
    [state, entry.sessionId],
  )

  const selectAutocomplete = useCallback(
    async (placeId: string, sessionToken: string) => {
      if (state.phase !== 'running') return
      setAutocompletePending(true)
      setAutocompleteSuggestions([])
      try {
        const result = await flowsApi.selectAutocompleteAddress(entry.sessionId, placeId, sessionToken)
        setAutocompleteSelection(result)
      } catch { /* best-effort */ }
      finally { setAutocompletePending(false) }
    },
    [state, entry.sessionId],
  )

  function handleValidationSelect(selectedAddress: Record<string, string>) {
    setValidationModal(null)
    advance(JSON.stringify(selectedAddress))
  }

  function handleValidationChange() {
    setValidationModal(null)
  }

  if (state.phase === 'ending') {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-center">
          <div className="w-10 h-10 rounded-full bg-emerald-900/50 border border-emerald-700 flex items-center justify-center mx-auto mb-3">
            <svg className="w-5 h-5 text-emerald-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
            </svg>
          </div>
          <p className="text-emerald-400 text-sm font-medium">Flow complete</p>
          <p className="text-gray-600 text-xs mt-1">Closing tab…</p>
        </div>
      </div>
    )
  }

  if (state.phase === 'error') {
    return (
      <div className="flex items-center justify-center h-full text-red-400 text-sm px-6 text-center">
        {state.message}
      </div>
    )
  }

  return (
    <div className="relative h-full">
      {validationModal && (
        <AddressValidationModal
          result={validationModal.result}
          originalAddress={validationModal.address}
          onSelectAddress={handleValidationSelect}
          onChangeAddress={handleValidationChange}
        />
      )}
      <NodeDisplay
        node={state.node}
        onAdvance={advance}
        onJump={jump}
        advancing={advancing}
        validating={validating}
        onValidateAddress={validateAddress}
        zipLookupResult={zipLookupResult}
        zipLookupPending={zipLookupPending}
        onLookupZip={lookupZip}
        onClearZipLookup={() => setZipLookupResult(null)}
        autocompleteSuggestions={autocompleteSuggestions}
        autocompletePending={autocompletePending}
        autocompleteSelection={autocompleteSelection}
        onAutocompleteSearch={searchAutocomplete}
        onAutocompleteSelect={selectAutocomplete}
        onClearAutocomplete={() => { setAutocompleteSuggestions([]); setAutocompleteSelection(null) }}
      />
    </div>
  )
}

// ── Tab bar ───────────────────────────────────────────────────────────────────

interface TabBarProps {
  sessions: FlowSessionEntry[]
  activeSessionId: string | null
  onSelect: (id: string) => void
}

function TabBar({ sessions, activeSessionId, onSelect }: TabBarProps) {
  return (
    <div className="flex items-end gap-0 px-4 border-b border-gray-800 shrink-0 overflow-x-auto">
      {sessions.map((s) => {
        const isActive = s.id === activeSessionId
        return (
          <button
            key={s.id}
            onClick={() => onSelect(s.id)}
            className={`px-4 py-2 text-xs font-medium whitespace-nowrap border-b-2 transition-colors ${
              isActive
                ? 'border-indigo-500 text-white'
                : 'border-transparent text-gray-500 hover:text-gray-300'
            }`}
          >
            {s.label}
          </button>
        )
      })}
    </div>
  )
}

// ── FlowPanel ─────────────────────────────────────────────────────────────────

export default function FlowPanel() {
  const { token, tenantSubdomain } = useAuthStore()
  const { setQueued, callRecordId } = useCallStore()
  const { sessions, activeSessionId, addSession, removeSession, setActiveSession } = useFlowSessionsStore()
  const [hub, setHub] = useState<signalR.HubConnection | null>(null)

  // Manual flow selector (shown when no sessions are active)
  const [flows, setFlows] = useState<{ id: string; name: string }[]>([])
  const [selectedFlowId, setSelectedFlowId] = useState('')
  const [starting, setStarting] = useState(false)

  useEffect(() => {
    flowsApi.list().then(setFlows).catch(console.error)
  }, [])

  // SignalR connection — shared across all tabs
  useEffect(() => {
    if (!token) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/flow?access_token=${token}`, {
        headers: { 'X-Tenant-Subdomain': tenantSubdomain ?? '' },
      })
      .withAutomaticReconnect()
      .build()

    // ESL screen pop — inbound call queued for this agent (DID route → telephony flow → tf_route_to_queue)
    connection.on('receiveIncomingCall', (callRecordId: string, callerNumber: string, callerName: string, destinationNumber: string) => {
      setQueued(callerNumber, callerName, callRecordId, destinationNumber)
    })

    connection.start().catch(console.error)
    setHub(connection)

    return () => { connection.stop() }
  }, [token, tenantSubdomain]) // eslint-disable-line react-hooks/exhaustive-deps

  async function handleStartSession() {
    if (!selectedFlowId || starting) return
    setStarting(true)
    try {
      const node = await flowsApi.startSession({
        flowId: selectedFlowId,
        callRecordId: callRecordId ?? undefined,
      })
      const flow = flows.find((f) => f.id === selectedFlowId)
      addSession({
        id: node.sessionId,
        label: flow?.name ?? 'Flow',
        sessionId: node.sessionId,
        initialNode: node,
      })
      setSelectedFlowId('')
    } catch (e) {
      console.error('Failed to start session', e)
    } finally {
      setStarting(false)
    }
  }

  const hasSessions = sessions.length > 0

  return (
    <div className="flex flex-col h-full relative">
      {/* Toolbar — shown when no sessions active */}
      {!hasSessions && (
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
            onClick={handleStartSession}
            disabled={!selectedFlowId || starting}
            className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded-lg px-4 py-1.5 text-sm font-medium transition-colors"
          >
            {starting ? 'Starting…' : 'Start'}
          </button>
        </div>
      )}

      {/* Tab bar — shown when 1+ sessions active */}
      {hasSessions && (
        <TabBar
          sessions={sessions}
          activeSessionId={activeSessionId}
          onSelect={setActiveSession}
        />
      )}

      {/* Session views — all mounted, only active one visible */}
      <div className="flex-1 overflow-y-auto relative">
        {!hasSessions && (
          <div className="flex items-center justify-center h-full text-gray-600 text-sm">
            Select a flow and press Start
          </div>
        )}

        {sessions.map((s) => (
          <div
            key={s.id}
            className={`absolute inset-0 overflow-y-auto ${s.id === activeSessionId ? 'block' : 'hidden'}`}
          >
            <FlowSessionView
              entry={s}
              hub={hub}
              onEnd={() => removeSession(s.id)}
            />
          </div>
        ))}
      </div>
    </div>
  )
}
