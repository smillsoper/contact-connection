import { requestPortalFocus } from '../lib/extensionBridge'
import { useState, useEffect, useCallback, useRef, type ReactNode } from 'react'
import CallerHistoryButton from './CallerHistory'
import * as signalR from '@microsoft/signalr'
import { useAuthStore } from '../stores/authStore'
import { useCallStore } from '../stores/callStore'
import { useFlowSessionsStore, type FlowSessionEntry } from '../stores/flowSessionsStore'
import { useAgentStateStore } from '../stores/agentStateStore'
import { useSupervisorMonitorStore } from '../stores/supervisorMonitorStore'
import { useIntercomStore } from '../stores/intercomStore'
import type { MonitorMode } from '../api/supervisor'
import { flowsApi, type FlowSummary, type AddressValidationResult, type ZipLookupResult, type AutocompleteSuggestion, type AutocompleteSelectionResult } from '../api/flows'
import { api } from '../api/client'
import type { FlowNodeState } from '../types/flow'
import NodeDisplay from './NodeDisplay'
import AddressValidationModal from './AddressValidationModal'
import { CloseIcon } from './icons/Icons'

/** One integration a script reaches, for the designer sandbox launch (S181). */
interface ScriptIntegration { key: string; kind: 'tax' | 'payment' | 'api'; name: string; detail: string | null; options: string[]; defaultEnvironment: string }
const ENV_LABEL: Record<string, string> = { sandbox: 'Sandbox', production: 'Production', simulated: 'Simulated (not called)' }

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

  // Auto-advance race fix (project_shared_call_variables "known follow-up"): trigger_telephony_event
  // is fire-and-continue, so a script node telling the agent to wait on a triggered telephony branch
  // has no guarantee the agent won't click Continue before that branch (and its {{shared.*}} write)
  // actually finishes. A node author opts in via waitForTelephonyEventName (the event name to wait
  // for) — when set, skip the agent's own judgment entirely and advance the instant that branch
  // reaches its own tf_end, via receiveTelephonyEventEnded (pushed correctly-timed, unlike
  // receiveSecureCollectEnded which fires before downstream set_variable/play nodes run).
  const lastTelephonyEventEnded = useCallStore((s) => s.lastTelephonyEventEnded)
  const bumpCartVersion = useCallStore((s) => s.bumpCartVersion)
  const autoAdvancedForRef = useRef<string | null>(null)

  // Join SignalR session room for live updates
  useEffect(() => {
    if (!hub) return
    hub.invoke('JoinSession', entry.sessionId).catch(console.error)
    return () => { hub.invoke('LeaveSession', entry.sessionId).catch(console.error) }
  }, [hub, entry.sessionId])

  // Call Records (S165): a supervisor corrected this call's data (or resubmitted its order) while
  // the agent is still in the script — show the refreshed node, refetch the cart, and say who
  // changed what. Same nodeId keeps whatever the agent is typing in an input (NodeDisplay only
  // resets its input on a node change); script text re-resolves with the corrected values.
  const [liveNotice, setLiveNotice] = useState<string | null>(null)
  useEffect(() => {
    if (!hub) return
    const handler = (node: FlowNodeState, message: string) => {
      if (node.sessionId !== entry.sessionId) return
      setState((prev) => (prev.phase === 'running' ? { phase: 'running', node } : prev))
      bumpCartVersion()
      setLiveNotice(message)
    }
    hub.on('receiveSessionUpdated', handler)
    return () => { hub.off('receiveSessionUpdated', handler) }
  }, [hub, entry.sessionId, bumpCartVersion])
  useEffect(() => {
    if (!liveNotice) return
    const timer = setTimeout(() => setLiveNotice(null), 10000)
    return () => clearTimeout(timer)
  }, [liveNotice])

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
        bumpCartVersion()
      } catch (e) {
        setState({ phase: 'error', message: String(e) })
      } finally {
        setAdvancing(false)
      }
    },
    [state, entry.sessionId, bumpCartVersion],
  )

  useEffect(() => {
    if (state.phase !== 'running') return
    const waitEventName = state.node.waitForTelephonyEventName
    if (!waitEventName) return
    // A practice run (training / sandbox, S179) has no telephony call, so no branch will ever report back — carry on
    // as if the event had finished.
    if (state.node.runMode && state.node.runMode !== 'production') {
      const key = `${state.node.nodeId}:practice`
      if (autoAdvancedForRef.current === key) return
      autoAdvancedForRef.current = key
      advance()
      return
    }
    if (!lastTelephonyEventEnded) return
    if (lastTelephonyEventEnded.eventName !== waitEventName) return
    const key = `${state.node.nodeId}:${lastTelephonyEventEnded.at}`
    if (autoAdvancedForRef.current === key) return
    autoAdvancedForRef.current = key
    advance()
  }, [state, lastTelephonyEventEnded, advance])

  const jump = useCallback(
    async (sectionNodeId: string) => {
      if (state.phase !== 'running') return
      setAdvancing(true)
      try {
        const next = await flowsApi.advance(entry.sessionId, { jumpToSectionNodeId: sectionNodeId })
        setState({ phase: 'running', node: next })
        bumpCartVersion()
      } catch (e) {
        setState({ phase: 'error', message: String(e) })
      } finally {
        setAdvancing(false)
      }
    },
    [state, entry.sessionId, bumpCartVersion],
  )

  const validateAddress = useCallback(
    async (address: Record<string, string>) => {
      if (state.phase !== 'running') return
      setValidating(true)
      try {
        const result = await flowsApi.validateAddress(entry.sessionId, address)
        if (result.outcomeKey === 'exact_match') {
          const finalAddress = { ...address, ...(result.correctedFields ?? {}), isVerified: 'true' }
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
      {liveNotice && (
        <div className="absolute top-2 left-1/2 -translate-x-1/2 z-20 max-w-[90%] bg-sky-950/95 border border-sky-700 text-sky-100 text-xs rounded-lg px-3 py-2 shadow-lg flex items-start gap-3">
          <span className="break-words">{liveNotice}</span>
          <button onClick={() => setLiveNotice(null)} className="text-sky-400 hover:text-white shrink-0" aria-label="Dismiss"><CloseIcon size={14} /></button>
        </div>
      )}
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
  /** Right-aligned tools for the active call (S181: caller history). */
  right?: ReactNode
}

function TabBar({ sessions, activeSessionId, onSelect, right }: TabBarProps) {
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
      {right}
    </div>
  )
}

// ── FlowPanel ─────────────────────────────────────────────────────────────────

export default function FlowPanel() {
  const { token, tenantSubdomain } = useAuthStore()
  const { setQueued, setAutoConnecting, withdrawOffer, reset: resetCall, callStatus, callRecordId, setCallRecordId, clearCallRecordId, bumpCartVersion } = useCallStore()
  const { sessions, activeSessionId, addSession, removeSession, setActiveSession } = useFlowSessionsStore()
  const setAgentStateCode = useAgentStateStore((s) => s.setAgentStateCode)
  const [hub, setHub] = useState<signalR.HubConnection | null>(null)

  // Manual flow selector (shown when no sessions are active)
  const [flows, setFlows] = useState<FlowSummary[]>([])
  const [selectedFlowId, setSelectedFlowId] = useState('')
  const [starting, setStarting] = useState(false)

  // Reopen the agent's still-open scripts on load (S171): a reload, a dropped connection or a crashed
  // browser no longer loses a script mid-call. Each comes back on the step the agent was on — they can
  // carry on with the caller, or finish the record if the caller is gone. addSession is idempotent,
  // so a tab that's already open (or a double-run effect) is never duplicated.
  const [restoredCount, setRestoredCount] = useState(0)
  useEffect(() => {
    let cancelled = false
    flowsApi.mySessions().then((states) => {
      if (cancelled || states.length === 0) return
      const open = new Set(useFlowSessionsStore.getState().sessions.map((x) => x.id))
      const fresh = states.filter((st) => !open.has(st.sessionId))
      for (const st of fresh)
        addSession({ id: st.sessionId, label: st.flowName ?? 'Script Flow', sessionId: st.sessionId, callRecordId: st.callRecordId, initialNode: st })
      if (fresh.length > 0) setRestoredCount(fresh.length)
    }).catch(() => { /* not fatal — the agent can still work; nothing to restore */ })
    return () => { cancelled = true }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (!restoredCount) return
    const t = setTimeout(() => setRestoredCount(0), 8000)
    return () => clearTimeout(t)
  }, [restoredCount])

  // Keep useCallStore.callRecordId in sync with whichever flow tab is actually active — with 2+
  // tabs open (a manual preview alongside a real bridged call's script-pop, or several bridged
  // calls in sequence), each tab's own callRecordId can differ, and call-scoped UI (the cart strip)
  // needs to reflect whichever one the agent is currently looking at, not just whichever call last
  // happened to set the single global value.
  useEffect(() => {
    const active = sessions.find((s) => s.id === activeSessionId)
    if (active) setCallRecordId(active.callRecordId)
  }, [activeSessionId, sessions, setCallRecordId])

  // A call hanging up runs the softphone's reset(), which clears callRecordId — but the script tab is
  // still open (the agent finishes it in ACW), so the cart strip went blank until the agent switched
  // tabs (S170). Put the active tab's call back once the softphone is idle again. Only while idle and
  // only when cleared: a new call sets/clears callRecordId itself (queued, auto-connecting, ringing,
  // dialing), so this never overrides a live call's id.
  useEffect(() => {
    if (callRecordId || callStatus !== 'idle') return
    const active = sessions.find((s) => s.id === activeSessionId)
    if (active?.callRecordId) setCallRecordId(active.callRecordId)
  }, [callRecordId, callStatus, activeSessionId, sessions, setCallRecordId])

  // CRM script flows only — a telephony flow needs a real call on the line, so it can't be
  // started from this manual test toolbar.
  // Designers also see unpublished drafts — they can run those in a sandbox (S183). Everyone else gets published flows.
  // Reloaded whenever the picker opens, so a save or publish in the designer shows up without a page refresh.
  const loadFlows = useCallback(() => {
    ;(useAuthStore.getState().hasPermission('flows.manage') ? flowsApi.listAllByType('crm') : flowsApi.list())
      .then((all) => setFlows(all.filter((f) => f.flow_type === 'crm'))).catch(console.error)
  }, [])
  useEffect(loadFlows, [loadFlows])

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
    connection.on('receiveIncomingCall', (callRecordId: string, callerNumber: string, callerName: string, destinationNumber: string, campaignId: string, tierLabel?: string | null) => {
      console.log('[SignalR] receiveIncomingCall', { callRecordId, callerNumber, callerName, destinationNumber, campaignId, tierLabel })
      setQueued(callerNumber, callerName, callRecordId, destinationNumber, campaignId, tierLabel)
    })

    // Parallel queuing — the offer moved to a higher routing tier (or this agent is no longer
    // eligible for it): drop the pop so the agent can't reach for a call that's no longer theirs.
    connection.on('receiveOfferWithdrawn', (callRecordId: string) => {
      console.log('[SignalR] receiveOfferWithdrawn', { callRecordId })
      withdrawOffer(callRecordId)
    })

    // RingStrategy.AutoAnswerBestAgent — the system picked this agent, no click required.
    // SoftphonePanel arms auto-answer off of callStatus === 'auto-connecting'; the actual
    // whisper/bridge INVITE follows shortly after this push.
    connection.on('receiveAutoConnecting', (callRecordId: string, callerNumber: string, callerName: string, destinationNumber: string, campaignId: string, tierLabel?: string | null) => {
      console.log('[SignalR] receiveAutoConnecting', { callRecordId, callerNumber, callerName, destinationNumber, campaignId, tierLabel })
      setAutoConnecting(callerNumber, callerName, callRecordId, destinationNumber, campaignId, tierLabel)
    })

    // Follows receiveAutoConnecting when delivery didn't pan out (e.g. softphone unreachable) —
    // no call is actually coming for this callRecordId, so drop back out of "Connecting…"
    // rather than leaving the agent stuck. Only relevant if we're still showing that same call.
    connection.on('receiveAutoConnectFailed', (failedCallRecordId: string) => {
      console.log('[SignalR] receiveAutoConnectFailed', { failedCallRecordId })
      const current = useCallStore.getState()
      if (current.callStatus === 'auto-connecting' && current.callRecordId === failedCallRecordId) {
        resetCall()
      }
    })

    // "Playing greeting…" indicator (project_agent_connect_tone) — a connect prompt is playing
    // to the caller during the auto-connecting window. Only relevant if it's for the call
    // currently on screen.
    connection.on('receivePlayingGreeting', (callRecordId: string, playing: boolean) => {
      const current = useCallStore.getState()
      if (current.callRecordId === callRecordId) current.setPlayingGreeting(playing)
    })

    // Script pop delivered after whisper bridge (agent_selected → tf_whisper → tf_end → CHANNEL_BRIDGE → agent_answer)
    connection.on('receiveScriptPop', (sessionJson: string) => {
      requestPortalFocus('script pop')
      try {
        const node = JSON.parse(sessionJson) as FlowNodeState
        addSession({
          id:           node.sessionId,
          label:        node.flowName ?? 'Script Flow',
          sessionId:    node.sessionId,
          callRecordId: node.callRecordId,
          initialNode:  node,
        })
      } catch { /* ignore malformed payload */ }
    })

    // Supervisor listen-in (S167): arm the softphone for the eavesdrop INVITE, or a mode switch.
    // Manual outbound (S179): the server placed the call; these say when the customer answered and how it ended.
    connection.on('receiveOutboundConnecting', (callRecordId: string, number: string) => {
      useCallStore.getState().setOutboundPending({ callRecordId, number })
    })
    connection.on('receiveOutboundAnswered', (callRecordId: string) => {
      const s = useCallStore.getState()
      if (s.callStatus === 'dialing' && s.callRecordId === callRecordId) s.setOnCall()
    })
    connection.on('receiveOutboundEnded', (_callRecordId: string, reason: string | null) => {
      if (reason) useCallStore.getState().setOutboundNotice(reason)
    })

    connection.on('receiveSupervisorConnecting', (kind: string, label: string, mode: string) => {
      if (kind === 'intercom') { useIntercomStore.getState().start(label, 'caller'); return }
      if (kind === 'intercom-incoming') { useIntercomStore.getState().start(label, 'callee'); return }
      const monitor = useSupervisorMonitorStore.getState()
      if (kind === 'monitor') monitor.connecting(label, mode as MonitorMode)
      else monitor.setMode(mode as MonitorMode)
    })
    connection.on('receiveIntercomEnded', () => {
      window.dispatchEvent(new Event('cc:intercom-ended'))
      useIntercomStore.getState().clear()
    })
    connection.on('receiveMonitorEnded', () => {
      window.dispatchEvent(new Event('cc:monitor-ended'))
      useSupervisorMonitorStore.getState().clear()
    })

    // Supervisor lock (Call Records "Finalize") — disable / re-enable the status picker.
    connection.on('receiveAgentLockChanged', (locked: boolean, message: string | null) => {
      const agentState = useAgentStateStore.getState()
      agentState.setLockMessage(locked ? (message ?? 'Locked by supervisor') : null)
      agentState.setAgentStateCode('unavailable')
    })

    // An automatic AI call summary is ready for review (S171) — the wrap-up card listens for this.
    connection.on('receiveAiSummaryReady', (callRecordId: string) => {
      window.dispatchEvent(new CustomEvent('cc:ai-summary-ready', { detail: callRecordId }))
    })

    // Personal queue + dedications (S183) — MyQueuePanel listens for these.
    connection.on('receiveMyQueueChanged', () => { window.dispatchEvent(new Event('cc:my-queue-changed')) })
    connection.on('receiveDedicationChanged', () => { window.dispatchEvent(new Event('cc:dedication-changed')) })

    // Sign-in lock: sign out now. AgentShell owns the sign-out (it clears SIP + auth).
    connection.on('receiveForceSignOut', (message: string) => {
      window.dispatchEvent(new CustomEvent('cc:force-signout', { detail: message }))
    })

    // Server-side agent state change (on_call at pickup, acw on hangup, available after acw)
    connection.on('receiveAgentStateChange', (code: string, label: string, expiresAtIso: string | null) => {
      const expiresAt = expiresAtIso ? new Date(expiresAtIso) : null
      setAgentStateCode(code, expiresAt, label)
    })

    // tf_secure_collect: a capture field started/advanced on the caller's parked leg (agent is on
    // park_with_moh) — never carries digits. Only relevant if it's for the call currently on screen.
    connection.on('receiveSecureCollectProgress', (callRecordId: string, fieldKey: string, fieldIndex: number, fieldCount: number) => {
      const current = useCallStore.getState()
      if (current.callRecordId === callRecordId) current.setSecureCollectProgress(fieldKey, fieldIndex, fieldCount)
    })

    // tf_secure_collect: the capture finished — "collected" | "failed" | "timeout" | "caller_hung_up".
    connection.on('receiveSecureCollectEnded', (callRecordId: string, outcome: string) => {
      const current = useCallStore.getState()
      if (current.callRecordId === callRecordId)
        current.setSecureCollectEnded(outcome as 'collected' | 'failed' | 'timeout' | 'caller_hung_up')
    })

    // A CRM trigger_telephony_event branch reached its own tf_end — the correctly-timed signal a
    // waitForTelephonyEventName script node auto-advances on (see FlowSessionView).
    connection.on('receiveTelephonyEventEnded', (callRecordId: string, eventName: string, outcome: string) => {
      const current = useCallStore.getState()
      if (current.callRecordId === callRecordId)
        current.setTelephonyEventEnded(callRecordId, eventName, outcome)
    })

    // SignalR groups are tied to the connection id, not the (stable) HubConnection object —
    // an automatic reconnect gets a new connection id server-side, so any explicit
    // Groups.AddToGroupAsync join (JoinSession below; agent:{agentId} is auto-rejoined by
    // FlowHub.OnConnectedAsync on every connect) is silently lost unless re-invoked here.
    // Without this, a mid-session network blip leaves the flow view looking "connected" while
    // no more node-state pushes for that session ever arrive.
    connection.onreconnected(() => {
      for (const entry of useFlowSessionsStore.getState().sessions) {
        connection.invoke('JoinSession', entry.sessionId).catch(console.error)
      }
      // Pushes may have been missed while disconnected — the personal queue catches up.
      window.dispatchEvent(new Event('cc:dedication-changed'))
    })

    connection.start()
      .then(() => console.log('[SignalR] FlowHub connected'))
      .catch((err) => console.error('[SignalR] FlowHub connection failed:', err))
    setHub(connection)

    return () => { connection.stop() }
  }, [token, tenantSubdomain]) // eslint-disable-line react-hooks/exhaustive-deps

  // Launch modes (S179): a live call starts its script in production on that call; otherwise the agent picks a practice
  // mode they're allowed — training (training.mode) or a designer sandbox (flows.manage, with a credential choice).
  const canTrain = useAuthStore((s) => s.hasPermission('training.mode'))
  const canSandbox = useAuthStore((s) => s.hasPermission('flows.manage'))
  const [launchMode, setLaunchMode] = useState<'training' | 'sandbox'>(canSandbox ? 'sandbox' : 'training')
  // Draft / published (S183): a designer sandbox can run either; a never-published flow only as its draft.
  const [runDraft, setRunDraft] = useState(false)
  const selectedFlow = flows.find((f) => f.id === selectedFlowId)
  const selectedPublished = !!selectedFlow?.is_active && selectedFlow?.published_version != null
  const sandboxing = !callRecordId && launchMode === 'sandbox'
  const useDraft = sandboxing && (runDraft || !selectedPublished)
  const canStartSelected = !selectedFlow || selectedPublished || sandboxing
  const practiceAllowed = canTrain || canSandbox
  // Designer sandbox (S181): each integration the script reaches runs where the designer chooses — e.g. production tax
  // with the sandbox payment gateway for a client's sandbox order checks.
  const [integrations, setIntegrations] = useState<ScriptIntegration[]>([])
  const [envChoices, setEnvChoices] = useState<Record<string, string>>({})
  useEffect(() => {
    if (!selectedFlowId || launchMode !== 'sandbox' || callRecordId) { setIntegrations([]); return }
    let live = true
    api.get<ScriptIntegration[]>(`/api/v1/flows/${selectedFlowId}/integrations`)
      .then((list) => {
        if (!live) return
        setIntegrations(list)
        setEnvChoices(Object.fromEntries(list.map((i) => [i.key, i.defaultEnvironment])))
      })
      .catch(() => { if (live) setIntegrations([]) })
    return () => { live = false }
  }, [selectedFlowId, launchMode, callRecordId])

  async function handleStartSession() {
    if (!selectedFlowId || starting) return
    setStarting(true)
    try {
      // No real call in progress (testing/previewing a script from this toolbar) — mint a stub
      // call record first so call-record-scoped features (cart, custom fields, etc.) have
      // something to attach to, same as a real inbound/outbound call would provide.
      let recordId = callRecordId
      if (!recordId) {
        // The stub takes on the flow's home campaign (if any) so tax, order numbers, payment
        // credentials and campaign-scoped fields behave like a real call on that campaign.
        // Launch modes (S179): with no live call it's always a practice run — training or a designer sandbox.
        const stub = await api.post<{ id: string }>('/api/v1/call-records/manual',
          { flowId: selectedFlowId, mode: launchMode, credentialSet: 'sandbox',
            integrations: launchMode === 'sandbox' ? envChoices : undefined })
        recordId = stub.id
        setCallRecordId(recordId)
      }

      const node = await flowsApi.startSession({
        flowId: selectedFlowId,
        callRecordId: recordId,
        useDraft: useDraft || undefined,
      })
      const flow = flows.find((f) => f.id === selectedFlowId)
      addSession({
        id: node.sessionId,
        label: flow?.name ?? 'Flow',
        sessionId: node.sessionId,
        callRecordId: node.callRecordId,
        initialNode: node,
      })
      bumpCartVersion()
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
            onMouseDown={loadFlows}
            onFocus={loadFlows}
            className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 border border-gray-700"
          >
            <option value="">Select flow…</option>
            {flows.map((f) => (
              <option key={f.id} value={f.id}>{f.name}{f.is_active && f.published_version != null ? '' : ' (draft)'}</option>
            ))}
          </select>

          {sandboxing && selectedFlow && (
            selectedPublished ? (
              <select value={runDraft ? 'draft' : 'published'} onChange={(e) => setRunDraft(e.target.value === 'draft')}
                className="bg-gray-800 text-white rounded-lg px-2 py-1.5 text-sm border border-gray-700" title="Which version of the script this sandbox run uses">
                <option value="published">Published v{selectedFlow.published_version}</option>
                <option value="draft">Draft v{selectedFlow.version}{selectedFlow.has_unpublished_changes ? '' : ' (same)'}</option>
              </select>
            ) : (
              <span className="text-xs text-amber-300" title="Never published — it can only run as a draft in a sandbox">Draft v{selectedFlow.version}</span>
            )
          )}

          {callRecordId ? (
            <span className="text-xs text-emerald-300" title="Starts on the live call">On this call</span>
          ) : practiceAllowed ? (
            <>
              <select value={launchMode} onChange={(e) => setLaunchMode(e.target.value as 'training' | 'sandbox')}
                className="bg-gray-800 text-white rounded-lg px-2 py-1.5 text-sm border border-gray-700" title="Practice mode">
                {canTrain && <option value="training">Training</option>}
                {canSandbox && <option value="sandbox">Sandbox (designer)</option>}
              </select>
            </>
          ) : (
            <span className="text-xs text-gray-500">Scripts start when you're on a call</span>
          )}

          <button
            onClick={handleStartSession}
            disabled={!selectedFlowId || starting || (!callRecordId && !practiceAllowed) || !canStartSelected}
            title={canStartSelected ? undefined : 'Not published yet — run it in a designer sandbox, or publish it first'}
            className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded-lg px-4 py-1.5 text-sm font-medium transition-colors"
          >
            {starting ? 'Starting…' : 'Start'}
          </button>
        </div>
      )}

      {!hasSessions && !callRecordId && launchMode === 'sandbox' && integrations.length > 0 && (
        <div className="px-4 py-3 border-b border-gray-800 shrink-0">
          <div className="flex items-center gap-3 mb-2">
            <span className="text-xs text-gray-400">Integrations for this run</span>
            <button className="text-[11px] text-sky-300 hover:text-sky-200"
              onClick={() => setEnvChoices(Object.fromEntries(integrations.map((i) => [i.key, i.options.includes('sandbox') ? 'sandbox' : i.options[0]])))}>
              All sandbox
            </button>
            <button className="text-[11px] text-amber-300 hover:text-amber-200"
              onClick={() => setEnvChoices(Object.fromEntries(integrations.map((i) => [i.key, 'production'])))}>
              All production
            </button>
          </div>
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-x-6 gap-y-1.5">
            {integrations.map((i) => (
              <div key={i.key} className="flex items-center gap-2 min-w-0" title={i.detail ?? undefined}>
                <span className="text-sm text-gray-200 truncate flex-1">{i.name}</span>
                <select value={envChoices[i.key] ?? i.defaultEnvironment}
                  onChange={(e) => setEnvChoices({ ...envChoices, [i.key]: e.target.value })}
                  className={`rounded-lg px-2 py-1 text-xs border bg-gray-800 ${envChoices[i.key] === 'production' ? 'border-amber-600 text-amber-200' : 'border-gray-700 text-white'}`}>
                  {i.options.map((o) => <option key={o} value={o}>{ENV_LABEL[o] ?? o}</option>)}
                </select>
              </div>
            ))}
          </div>
          {Object.values(envChoices).includes('production') && (
            <p className="text-[11px] text-amber-300 mt-2">Production integrations are real — real tax lookups, real card charges, real orders in the client's system.</p>
          )}
        </div>
      )}

      {restoredCount > 0 && (
        <div className="mx-3 mt-2 rounded-lg border border-sky-800 bg-sky-950/50 px-3 py-1.5 text-xs text-sky-200 flex items-center gap-2">
          <span className="flex-1">
            Reopened {restoredCount === 1 ? 'the script' : `${restoredCount} scripts`} you had open — each is on the step you left it.
          </span>
          <button onClick={() => setRestoredCount(0)} className="text-sky-300 hover:text-white" title="Dismiss"><CloseIcon size={14} /></button>
        </div>
      )}

      {/* Tab bar — shown when 1+ sessions active */}
      {hasSessions && (
        <TabBar
          sessions={sessions}
          activeSessionId={activeSessionId}
          onSelect={setActiveSession}
          right={<CallerHistoryButton callRecordId={sessions.find((s) => s.id === activeSessionId)?.callRecordId ?? null} />}
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
              onEnd={() => {
                removeSession(s.id)
                // The stub call record the toolbar minted (see handleStartSession) has no purpose
                // once every flow-preview tab using it is done — clear it so a stale cart doesn't
                // linger in the UI and the next "Start" mints a fresh one instead of reusing this
                // one. Gated on callStatus === 'idle' so this never clobbers a real concurrent call's
                // call record if one happens to be active elsewhere at the same time.
                if (useFlowSessionsStore.getState().sessions.length === 0 && callStatus === 'idle') {
                  clearCallRecordId()
                }
              }}
            />
          </div>
        ))}
      </div>
    </div>
  )
}
