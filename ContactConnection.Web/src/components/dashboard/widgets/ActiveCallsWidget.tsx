import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { dashboardWidgetsApi, type ActiveCallRow } from '../../../api/dashboardWidgets'
import { supervisorApi, MONITOR_MODE_LABEL, type MonitorMode, type MonitorState } from '../../../api/supervisor'
import { useAuthStore } from '../../../stores/authStore'
import type { WidgetFilterConfig } from '../../../types/dashboard'
import {
  useDashboardLiveAgentSessions, useDashboardLiveAgentState, useDashboardLiveCallState, useDashboardLiveRegistration,
} from '../DashboardLiveContext'

// Active Calls (S180, Sprint 2 item 1): every call an agent is on right now. Click a call for the detail view with the
// supervisor tools (Monitor / Coach / Barge / Take over) — the same server actions the Agent List widget uses.
// Live: refetches on the supervisor pushes (call state, agent state, agent scripts); the timers tick locally.

function elapsed(iso: string | null): string {
  if (!iso) return '—'
  const s = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 1000))
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}` : `${m}:${String(sec).padStart(2, '0')}`
}

function fmtPhone(n: string | null): string {
  const d = (n ?? '').replace(/\D/g, '').replace(/^1(?=\d{10}$)/, '')
  return d.length === 10 ? `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}` : n || '—'
}

const DIRECTION: Record<string, { label: string; cls: string }> = {
  inbound: { label: 'IN', cls: 'bg-sky-950/60 text-sky-300 border-sky-800' },
  callback: { label: 'CB', cls: 'bg-teal-950/60 text-teal-300 border-teal-800' },
  outbound: { label: 'OUT', cls: 'bg-violet-950/60 text-violet-300 border-violet-800' },
}

function Flags({ r }: { r: ActiveCallRow }) {
  return (
    <span className="inline-flex gap-1 flex-wrap">
      {r.onHold && <span className="px-1 rounded bg-amber-950/60 text-amber-300 border border-amber-800 text-[10px]">hold</span>}
      {r.secureCapture && <span className="px-1 rounded bg-indigo-950/60 text-indigo-300 border border-indigo-800 text-[10px]">card capture</span>}
      {r.recording && <span className="px-1 rounded bg-red-950/60 text-red-300 border border-red-800 text-[10px]">● rec</span>}
      {r.tierLabel && <span className="px-1 rounded-full bg-fuchsia-600/30 text-fuchsia-300 text-[10px] uppercase font-semibold">{r.tierLabel}</span>}
    </span>
  )
}

export default function ActiveCallsWidget({ config }: { config: WidgetFilterConfig }) {
  const [rows, setRows] = useState<ActiveCallRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [selected, setSelected] = useState<string | null>(null)
  const [, setTick] = useState(0)
  const liveCall = useDashboardLiveCallState()
  const liveAgent = useDashboardLiveAgentState()
  const liveSessions = useDashboardLiveAgentSessions()
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const sup = useSupervisorTools()

  const load = useCallback(() => {
    dashboardWidgetsApi.activeCalls(config)
      .then((r) => { setRows(r); setError(null) })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.campaignId, config.clientId])

  useEffect(() => { load() }, [load])

  useEffect(() => {
    const id = setInterval(() => setTick((t) => t + 1), 1000)
    return () => clearInterval(id)
  }, [])

  // A call starts / ends / is held, an agent's status changes (manual outbound dials go On Call), a script opens or
  // closes — refetch, debounced together.
  useEffect(() => {
    if (!liveCall && !liveAgent && !liveSessions) return
    if (debounceRef.current) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(load, 400)
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current) }
  }, [liveCall, liveAgent, liveSessions, load])

  // A call that ends while its detail is open closes the detail.
  const current = rows.find((r) => r.callRecordId === selected) ?? null
  useEffect(() => { if (selected && rows.length && !current) setSelected(null) }, [rows, selected, current])

  if (error) return <div className="text-xs text-red-400">{error}</div>

  return (
    <div className="h-full overflow-auto text-xs">
      {sup.monitorBar}
      {sup.note && <p className="text-[11px] text-sky-300 mb-1">{sup.note}</p>}
      {sup.error && <p className="text-[11px] text-red-400 mb-1">{sup.error}</p>}
      {rows.length === 0 ? (
        <p className="text-gray-600 py-1">No calls in progress.</p>
      ) : (
        <table className="w-full">
          <thead>
            <tr className="text-left text-gray-500 border-b border-gray-800">
              <th className="py-1 pr-2 font-medium">Customer</th>
              <th className="py-1 pr-2 font-medium">Agent</th>
              <th className="py-1 pr-2 font-medium">Campaign</th>
              <th className="py-1 pr-2 font-medium">Section</th>
              <th className="py-1 pr-2 font-medium">Time</th>
              <th className="py-1 font-medium" />
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => {
              const dir = DIRECTION[r.direction] ?? DIRECTION.inbound
              return (
                <tr key={`${r.callRecordId}-${r.agentId}`} onClick={() => setSelected(r.callRecordId)}
                  className="border-b border-gray-800/60 last:border-0 cursor-pointer hover:bg-gray-800/50">
                  <td className="py-1.5 pr-2 text-gray-200 whitespace-nowrap">
                    <span className={`mr-1.5 px-1 rounded border text-[9px] font-semibold ${dir.cls}`}>{dir.label}</span>
                    {fmtPhone(r.customerNumber)}
                  </td>
                  <td className="py-1.5 pr-2 text-gray-300">{r.agentName ?? '—'}</td>
                  <td className="py-1.5 pr-2 text-gray-400">{r.campaignName ?? '—'}</td>
                  <td className="py-1.5 pr-2 text-emerald-300">{r.sectionName ?? <span className="text-gray-600">—</span>}</td>
                  <td className="py-1.5 pr-2 text-gray-300 font-mono">{elapsed(r.connectedAt)}</td>
                  <td className="py-1.5"><Flags r={r} /></td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}

      {current && <CallDetail call={current} sup={sup} onClose={() => setSelected(null)} />}
    </div>
  )
}

function CallDetail({ call, sup, onClose }: { call: ActiveCallRow; sup: SupervisorTools; onClose: () => void }) {
  const canOpenCalls = useAuthStore((s) => s.hasPermission('calls.view') || s.hasPermission('calls.manage'))
  const dir = DIRECTION[call.direction] ?? DIRECTION.inbound
  const listeningHere = sup.monitoring?.agentId === call.agentId
  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-4" onClick={onClose}>
      <div className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-lg p-5 space-y-4 text-sm" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-start gap-3">
          <div className="flex-1">
            <p className="text-white text-base font-semibold">
              <span className={`mr-2 px-1.5 rounded border text-[10px] align-middle ${dir.cls}`}>{dir.label}</span>
              {fmtPhone(call.customerNumber)}
            </p>
            <p className="text-gray-400 text-xs mt-0.5">
              {call.direction === 'outbound' ? 'Calling from' : 'Called'} {fmtPhone(call.ourNumber)} · {call.campaignName ?? 'No campaign'}
            </p>
          </div>
          <span className="text-white font-mono text-lg">{elapsed(call.connectedAt)}</span>
          <button onClick={onClose} className="text-gray-500 hover:text-white">✕</button>
        </div>

        <dl className="grid grid-cols-[7rem_1fr] gap-y-1.5 text-xs">
          <dt className="text-gray-500">Agent</dt><dd className="text-gray-200">{call.agentName ?? '—'}</dd>
          <dt className="text-gray-500">Script</dt><dd className="text-gray-200">{call.scriptName ?? <span className="text-gray-600">none open</span>}</dd>
          <dt className="text-gray-500">Section</dt><dd className="text-emerald-300">{call.sectionName ?? <span className="text-gray-600">{call.scriptName ? 'this script has no sections' : '—'}</span>}</dd>
          <dt className="text-gray-500">Status</dt><dd><Flags r={call} />{!call.onHold && !call.secureCapture && !call.recording && !call.tierLabel && <span className="text-gray-300">talking</span>}</dd>
        </dl>

        {sup.canMonitor && (
          <div className="border-t border-gray-800 pt-3 space-y-2">
            <p className="text-gray-400 text-xs font-medium">Supervise <span className="text-gray-600 font-normal">— uses your agent portal's softphone</span></p>
            {!call.supervisable ? (
              <p className="text-xs text-gray-500">Monitoring isn't available on manual outbound calls yet.</p>
            ) : listeningHere ? (
              <p className="text-xs text-sky-300">You're {MONITOR_MODE_LABEL[sup.monitoring!.mode].toLowerCase()} this call — use the bar above to switch or end.</p>
            ) : (
              <div className="flex flex-wrap gap-2">
                <button disabled={sup.busy} onClick={() => sup.start(call.agentId, 'listen')} className="px-2.5 py-1 rounded bg-gray-800 hover:bg-gray-700 text-gray-200">Monitor</button>
                <button disabled={sup.busy} onClick={() => sup.start(call.agentId, 'coach')} className="px-2.5 py-1 rounded bg-gray-800 hover:bg-gray-700 text-gray-200">Coach</button>
                {sup.canOverride && <button disabled={sup.busy} onClick={() => sup.start(call.agentId, 'barge')} className="px-2.5 py-1 rounded bg-gray-800 hover:bg-gray-700 text-gray-200">Barge in</button>}
                {sup.canOverride && <button disabled={sup.busy} onClick={() => { sup.takeOver(call.agentId); onClose() }} className="px-2.5 py-1 rounded border border-amber-800 text-amber-300 hover:bg-amber-950">Take over…</button>}
              </div>
            )}
          </div>
        )}

        {canOpenCalls && (
          <div className="border-t border-gray-800 pt-3">
            <Link to={`/admin/calls/${call.callRecordId}`} target="_blank" className="text-indigo-400 hover:text-indigo-300 text-xs">
              Open the call record (live) →
            </Link>
          </div>
        )}
      </div>
    </div>
  )
}

/** Monitor / Coach / Barge / Take over — the supervisor actions, shared shape with the Agent List widget's. */
interface SupervisorTools {
  canMonitor: boolean
  canOverride: boolean
  monitoring: MonitorState | null
  busy: boolean
  error: string | null
  note: string | null
  monitorBar: React.ReactNode
  start: (agentId: string, mode: MonitorMode) => void
  takeOver: (agentId: string) => void
}

function useSupervisorTools(): SupervisorTools {
  const canMonitor = useAuthStore((s) => s.hasPermission('supervisor.monitor') || s.hasPermission('supervisor.override'))
  const canOverride = useAuthStore((s) => s.hasPermission('supervisor.override'))
  const myId = useAuthStore((s) => s.agentId)
  const liveReg = useDashboardLiveRegistration()
  const [monitoring, setMonitoring] = useState<MonitorState | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [note, setNote] = useState<string | null>(null)
  const [myRegistered, setMyRegistered] = useState(false)

  useEffect(() => { if (canOverride) supervisorApi.me().then((m) => setMyRegistered(m.registered)).catch(() => {}) }, [canOverride])
  useEffect(() => { if (liveReg && liveReg.agentId === myId) setMyRegistered(liveReg.registered) }, [liveReg, myId])
  useEffect(() => { if (canMonitor) supervisorApi.current().then((m) => setMonitoring(m ?? null)).catch(() => {}) }, [canMonitor])
  useEffect(() => {
    const onEnded = () => setMonitoring(null)
    window.addEventListener('cc:monitor-ended', onEnded)
    return () => window.removeEventListener('cc:monitor-ended', onEnded)
  }, [])
  useEffect(() => {
    if (!note) return
    const t = setTimeout(() => setNote(null), 10000)
    return () => clearTimeout(t)
  }, [note])

  async function run(action: () => Promise<MonitorState | void>) {
    setBusy(true); setError(null)
    try { const m = await action(); if (m) setMonitoring(m) }
    catch (e) { setError(e instanceof Error ? e.message : 'Failed.') }
    finally { setBusy(false) }
  }

  function takeOver(agentId: string) {
    setError(null)
    setMonitoring(null)   // the server ends any listen-in as part of the take-over
    if (myRegistered) {
      setBusy(true)
      supervisorApi.takeOver(agentId)
        .then((r) => setNote(r.phone
          ? 'Taken over — the call and script are in your agent portal. Switch to that tab.'
          : 'Taken over — the script is in your agent portal. Switch to that tab.'))
        .catch((e: Error) => setError(e.message))
        .finally(() => setBusy(false))
      return
    }
    // No portal open: open one inside the click (so the browser doesn't block it); it takes the call over on registering.
    window.open(`/agent?takeover=${agentId}`, 'cc-agent-portal')
  }

  const monitorBar = monitoring && (
    <div className="mb-2 rounded-lg bg-sky-950/50 border border-sky-800 px-2 py-1.5 flex flex-wrap items-center gap-1.5 text-[11px]">
      <span className="text-sky-200 font-medium mr-1">🎧 {MONITOR_MODE_LABEL[monitoring.mode]} {monitoring.agentName}</span>
      {(['listen', 'coach', 'barge'] as MonitorMode[]).map((m) => (
        <button key={m} disabled={busy || monitoring.mode === m || (m === 'barge' && !canOverride)}
          onClick={() => run(() => supervisorApi.setMode(m))}
          className={`px-1.5 py-0.5 rounded ${monitoring.mode === m ? 'bg-sky-700 text-white' : 'text-sky-300 hover:bg-sky-900'} disabled:cursor-default`}>
          {m === 'listen' ? 'Listen' : m === 'coach' ? 'Coach' : 'Barge'}
        </button>
      ))}
      {canOverride && <button onClick={() => takeOver(monitoring.agentId)} className="px-1.5 py-0.5 rounded text-amber-300 hover:bg-amber-950">Take over</button>}
      <button disabled={busy} onClick={() => run(async () => { await supervisorApi.stop(); setMonitoring(null) })}
        className="ml-auto px-1.5 py-0.5 rounded text-gray-300 hover:bg-gray-800">End</button>
    </div>
  )

  return {
    canMonitor, canOverride, monitoring, busy, error, note, monitorBar,
    start: (agentId, mode) => void run(() => supervisorApi.start(agentId, mode)),
    takeOver,
  }
}
