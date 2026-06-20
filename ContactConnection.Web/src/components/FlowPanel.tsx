import { useState, useEffect, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'
import { useAuthStore } from '../stores/authStore'
import { useCallStore } from '../stores/callStore'
import { flowsApi, type AddressValidationResult, type ZipLookupResult, type AutocompleteSuggestion, type AutocompleteSelectionResult } from '../api/flows'
import type { FlowNodeState } from '../types/flow'
import NodeDisplay from './NodeDisplay'
import AddressValidationModal from './AddressValidationModal'

type PanelState =
  | { phase: 'idle' }
  | { phase: 'loading' }
  | { phase: 'running'; node: FlowNodeState }
  | { phase: 'error'; message: string }

export default function FlowPanel() {
  const { token, tenantSubdomain } = useAuthStore()
  const { setCallRecordId } = useCallStore()
  const [state, setState] = useState<PanelState>({ phase: 'idle' })
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

    // ESL screen pop — inbound call parked for this agent
    connection.on('receiveIncomingCall', (callRecordId: string, _callerNumber: string, _callerName: string) => {
      setCallRecordId(callRecordId)
    })

    connection.start().catch(console.error)
    setHub(connection)

    return () => {
      connection.stop()
    }
  }, [token, tenantSubdomain]) // eslint-disable-line react-hooks/exhaustive-deps

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

  const validateAddress = useCallback(
    async (address: Record<string, string>) => {
      if (state.phase !== 'running') return
      setValidating(true)
      try {
        const result = await flowsApi.validateAddress(state.node.sessionId, address)
        if (result.outcomeKey === 'exact_match') {
          // Auto-advance: merge corrected fields into original address
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [state, advance],
  )

  const lookupZip = useCallback(
    async (zip: string) => {
      if (state.phase !== 'running') return
      setZipLookupPending(true)
      try {
        const result = await flowsApi.lookupZip(state.node.sessionId, zip)
        setZipLookupResult(result)
      } catch {
        // Silently ignore — ZIP lookup is best-effort
      } finally {
        setZipLookupPending(false)
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [state],
  )

  const searchAutocomplete = useCallback(
    async (text: string, sessionToken: string) => {
      if (state.phase !== 'running') return
      setAutocompletePending(true)
      try {
        const result = await flowsApi.autocompleteAddress(state.node.sessionId, text, sessionToken)
        setAutocompleteSuggestions(result.suggestions)
      } catch {
        // best-effort
      } finally {
        setAutocompletePending(false)
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [state],
  )

  const selectAutocomplete = useCallback(
    async (placeId: string, sessionToken: string) => {
      if (state.phase !== 'running') return
      setAutocompletePending(true)
      setAutocompleteSuggestions([])
      try {
        const result = await flowsApi.selectAutocompleteAddress(state.node.sessionId, placeId, sessionToken)
        setAutocompleteSelection(result)
      } catch {
        // best-effort
      } finally {
        setAutocompletePending(false)
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [state],
  )

  function handleValidationSelect(selectedAddress: Record<string, string>) {
    setValidationModal(null)
    advance(JSON.stringify(selectedAddress))
  }

  function handleValidationChange() {
    setValidationModal(null)
  }

  return (
    <div className="flex flex-col h-full relative">
      {validationModal && (
        <AddressValidationModal
          result={validationModal.result}
          originalAddress={validationModal.address}
          onSelectAddress={handleValidationSelect}
          onChangeAddress={handleValidationChange}
        />
      )}
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
        )}
      </div>
    </div>
  )
}
