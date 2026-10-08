import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import * as signalR from '@microsoft/signalr'
import { api } from '../../api/client'
import type { AgentListRow } from '../../api/dashboardWidgets'
import type { FlowNodeState } from '../../types/flow'
import { useAuthStore } from '../../stores/authStore'
import { getSubdomainFromHostname } from '../../utils/subdomain'
import { reconnectForever } from '../../utils/hubRetry'
import NodeDisplay from '../NodeDisplay'
import { CloseIcon, EyeIcon } from '../icons/Icons'

/**
 * Follow-along script view (S184): a supervisor sees exactly the step an agent's script is on — the same screen the
 * agent has, read-only — updating live as they work, and (with override) can send them to a section; the agent's
 * screen moves with a notice saying who moved it.
 */
export default function FollowScriptModal({ agentName, calls, onClose }: {
  agentName: string
  calls: AgentListRow['live_calls']
  onClose: () => void
}) {
  const sessions = useMemo(() => calls.flatMap((c) => (c.sessions ?? []).map((s) => ({ ...s, callRecordId: c.call_record_id }))), [calls])
  const [active, setActive] = useState(sessions[0]?.session_id ?? null)
  const [nodes, setNodes] = useState<Record<string, FlowNodeState | 'ended'>>({})
  const [status, setStatus] = useState<'connecting' | 'live' | 'offline'>('connecting')
  const [jumpTo, setJumpTo] = useState('')
  const [busy, setBusy] = useState(false)
  const [note, setNote] = useState<{ ok: boolean; text: string } | null>(null)
  const [lastMove, setLastMove] = useState<number | null>(null)
  const canMove = useAuthStore((s) => s.hasPermission('supervisor.override'))
  const ids = useRef(sessions.map((s) => s.session_id))

  useEffect(() => {
    const { token, tenantSubdomain } = useAuthStore.getState()
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/flow?access_token=${token ?? ''}`, { headers: { 'X-Tenant-Subdomain': getSubdomainFromHostname() ?? tenantSubdomain ?? '' } })
      .withAutomaticReconnect(reconnectForever)
      .build()
    const take = (state: FlowNodeState) => {
      if (!ids.current.includes(state.sessionId)) return
      setNodes((n) => ({ ...n, [state.sessionId]: state }))
      setLastMove(Date.now())
    }
    conn.on('receiveNodeState', take)
    conn.on('receiveSessionUpdated', (state: FlowNodeState) => take(state))
    const seed = () => Promise.all(ids.current.map((id) =>
      api.get<FlowNodeState>(`/api/v1/flow-sessions/${id}`)
        .then((s) => setNodes((n) => ({ ...n, [id]: s })))
        .catch(() => setNodes((n) => ({ ...n, [id]: 'ended' })))))
    const join = () => Promise.all(ids.current.map((id) => conn.invoke('JoinSession', id)))
    conn.onreconnecting(() => setStatus('connecting'))
    conn.onreconnected(() => { void join().then(seed).then(() => setStatus('live')).catch(() => setStatus('offline')) })
    conn.start().then(join).then(seed).then(() => setStatus('live')).catch(() => setStatus('offline'))
    return () => {
      for (const id of ids.current) void conn.invoke('LeaveSession', id).catch(() => {})
      void conn.stop()
    }
  }, [])

  const node = active ? nodes[active] : undefined
  const current = active ? sessions.find((s) => s.session_id === active) : undefined
  const targets = node && node !== 'ended' ? (node.jumpTargets ?? []).filter((t) => !t.isLocked && !t.isCurrentSection) : []

  async function send() {
    if (!active || !jumpTo) return
    setBusy(true); setNote(null)
    try {
      const state = await api.post<FlowNodeState>(`/api/v1/flow-sessions/${active}/supervisor-jump`, { sectionNodeId: jumpTo })
      setNodes((n) => ({ ...n, [active]: state }))
      setNote({ ok: true, text: `${agentName.split(' ')[0]} is now on ${state.currentSectionName ?? state.label}.` })
      setJumpTo('')
    } catch (e) {
      setNote({ ok: false, text: e instanceof Error ? e.message : 'Could not move the script.' })
    } finally { setBusy(false) }
  }

  const ago = lastMove ? Math.max(0, Math.round((Date.now() - lastMove) / 1000)) : null

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div className="flex flex-col w-full max-w-4xl h-[88vh] rounded-xl border border-gray-700 bg-gray-950 shadow-2xl">
        <div className="flex items-center gap-3 px-4 py-2.5 border-b border-gray-800">
          <EyeIcon size={16} className="text-sky-300" />
          <h2 className="text-white font-semibold">{agentName}'s script</h2>
          <span className={`text-xs ${status === 'live' ? 'text-emerald-400' : status === 'offline' ? 'text-amber-300' : 'text-gray-400'}`}>
            {status === 'live' ? 'Live — follows every step' : status === 'offline' ? 'Live updates unavailable — reopen to refresh' : 'Connecting…'}
          </span>
          <button onClick={onClose} className="ml-auto text-gray-400 hover:text-white" title="Close"><CloseIcon size={18} /></button>
        </div>

        {sessions.length > 1 && (
          <div className="flex gap-1 px-4 pt-2 border-b border-gray-800">
            {sessions.map((s) => (
              <button key={s.session_id} onClick={() => setActive(s.session_id)}
                className={`px-3 py-1.5 text-xs rounded-t ${active === s.session_id ? 'bg-gray-800 text-white' : 'text-gray-400 hover:text-gray-200'}`}>
                {s.flow_name ?? 'Script'}
              </button>
            ))}
          </div>
        )}

        <div className="flex items-center gap-3 px-4 py-2 border-b border-gray-800 text-xs text-gray-400">
          <span className="text-gray-200">{current?.flow_name ?? 'Script'}</span>
          {node && node !== 'ended' && node.currentSectionName && <span>Section: <span className="text-gray-200">{node.currentSectionName}</span></span>}
          {ago !== null && <span className="text-gray-500">moved {ago < 5 ? 'just now' : `${ago}s ago`}</span>}
          {canMove && targets.length > 0 && (
            <div className="ml-auto flex items-center gap-2">
              <select value={jumpTo} onChange={(e) => setJumpTo(e.target.value)}
                className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-white">
                <option value="">Send them to a section…</option>
                {targets.map((t) => <option key={t.sectionNodeId} value={t.sectionNodeId}>{t.name}</option>)}
              </select>
              <button onClick={() => void send()} disabled={!jumpTo || busy}
                className="bg-sky-600 hover:bg-sky-500 text-white rounded px-2.5 py-1 disabled:opacity-40">{busy ? 'Moving…' : 'Send'}</button>
            </div>
          )}
        </div>
        {note && <p className={`px-4 py-1.5 text-xs ${note.ok ? 'text-emerald-400' : 'text-red-400'}`}>{note.text}</p>}

        <div className="flex-1 min-h-0 overflow-y-auto">
          {!node ? <p className="p-6 text-sm text-gray-500">Loading…</p>
            : node === 'ended' || node.isTerminal ? <p className="p-6 text-sm text-gray-400">This script has finished.</p>
            : (
              // The agent's own screen, read-only: same component, nothing clickable.
              <div className="pointer-events-none select-none p-4" aria-readonly>
                <NodeDisplay node={node} onAdvance={() => {}} advancing={false} />
              </div>
            )}
        </div>
        <p className="px-4 py-1.5 text-[11px] text-gray-600 border-t border-gray-800">
          You see each step as {agentName.split(' ')[0]} reaches it — not what they're typing before they press Next.
        </p>
      </div>
    </div>,
    document.body,
  )
}
