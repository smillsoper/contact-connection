import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { dashboardWidgetsApi, type AgentListRow } from '../../../api/dashboardWidgets'
import type { WidgetFilterConfig } from '../../../types/dashboard'
import { useDashboardLiveAgentState, useDashboardLiveRegistration, useDashboardLiveAgentSessions } from '../DashboardLiveContext'
import { useAuthStore } from '../../../stores/authStore'
import { agentLockApi } from '../../../api/agentLock'
import { supervisorApi, MONITOR_MODE_LABEL, type MonitorMode, type MonitorState } from '../../../api/supervisor'
import { ChevronDownIcon, ChevronUpIcon, CloseIcon, ExternalLinkIcon, HeadsetIcon, LockIcon, PhoneIcon, ScreenIcon } from '../../icons/Icons'
import ScreenViewModal from '../ScreenViewModal'
import DedicationModal from '../DedicationModal'

type SortColumn = 'name' | 'state' | 'time'
type SortDirection = 'asc' | 'desc'

function SortArrow({ active, direction }: { active: boolean; direction: SortDirection }) {
  if (!active) return null
  return <span className="ml-1 text-gray-400">{direction === 'asc' ? <ChevronUpIcon size={11} className="inline -mt-0.5" /> : <ChevronDownIcon size={11} className="inline -mt-0.5" />}</span>
}

const STATE_DOT: Record<string, string> = {
  available: 'bg-green-500',
  unavailable: 'bg-red-500',
  unavailable_break: 'bg-amber-500',
  unavailable_lunch: 'bg-orange-500',
  unavailable_custom: 'bg-orange-400',
  on_call: 'bg-violet-500',
  acw: 'bg-blue-500',
  callback_pending: 'bg-sky-500',
  logged_out: 'bg-gray-500',
}

function formatDuration(sinceIso: string | null): string {
  if (!sinceIso) return '—'
  const seconds = Math.max(0, Math.floor((Date.now() - new Date(sinceIso).getTime()) / 1000))
  const m = Math.floor(seconds / 60)
  const s = seconds % 60
  return `${m}:${s.toString().padStart(2, '0')}`
}

export default function AgentListWidget({ config }: { config: WidgetFilterConfig }) {
  const [rows, setRows] = useState<AgentListRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [, setTick] = useState(0)
  const [sortColumn, setSortColumn] = useState<SortColumn>('name')
  const [sortDirection, setSortDirection] = useState<SortDirection>('asc')
  const liveEvent = useDashboardLiveAgentState()
  const liveReg = useDashboardLiveRegistration()
  const liveSessions = useDashboardLiveAgentSessions()
  const canOpenCalls = useAuthStore((s) => s.hasPermission('calls.view') || s.hasPermission('calls.manage'))
  const canUnlock = useAuthStore((s) => s.hasPermission('agents.manage') || s.hasPermission('calls.manage') || s.hasPermission('supervisor.override'))
  const [unlocking, setUnlocking] = useState<string | null>(null)
  const canMonitor = useAuthStore((s) => s.hasPermission('supervisor.monitor') || s.hasPermission('supervisor.override'))
  const canOverride = useAuthStore((s) => s.hasPermission('supervisor.override'))
  // Dedicate an agent to campaigns (S183).
  const canDedicate = useAuthStore((s) => s.hasPermission('supervisor.override') || s.hasPermission('agents.manage'))
  const [dedicating, setDedicating] = useState<{ id: string; name: string } | null>(null)
  // Live screen view (S183)
  const [viewing, setViewing] = useState<{ id: string; name: string } | null>(null)
  const myId = useAuthStore((s) => s.agentId)
  const [monitoring, setMonitoring] = useState<MonitorState | null>(null)
  const [supBusy, setSupBusy] = useState(false)
  const [supError, setSupError] = useState<string | null>(null)
  const [menuFor, setMenuFor] = useState<string | null>(null)
  // The Supervise menu renders in a portal at fixed coordinates under its button — inside the
  // widget's scroll area it was clipped at the widget edge, hiding options (S169).
  const [menuPos, setMenuPos] = useState<{ top: number; right: number } | null>(null)
  const menuRef = useRef<HTMLDivElement | null>(null)
  useEffect(() => {
    if (!menuFor) return
    const close = () => setMenuFor(null)
    const onDown = (e: MouseEvent) => {
      const t = e.target as HTMLElement
      if (menuRef.current?.contains(t) || t.closest('[data-supervise-toggle]')) return
      close()
    }
    document.addEventListener('mousedown', onDown)
    window.addEventListener('scroll', close, true)
    window.addEventListener('resize', close)
    return () => {
      document.removeEventListener('mousedown', onDown)
      window.removeEventListener('scroll', close, true)
      window.removeEventListener('resize', close)
    }
  }, [menuFor])
  const [supNote, setSupNote] = useState<string | null>(null)
  // Is my own softphone (an open agent portal) registered? Kept live by the registration pushes.
  const [myRegistered, setMyRegistered] = useState(false)
  useEffect(() => { if (canOverride) supervisorApi.me().then((m) => setMyRegistered(m.registered)).catch(() => {}) }, [canOverride])
  useEffect(() => { if (liveReg && liveReg.agentId === myId) setMyRegistered(liveReg.registered) }, [liveReg, myId])

  // Current listen-in (survives a dashboard reload); ended remotely → cc:monitor-ended / push.
  useEffect(() => { if (canMonitor) supervisorApi.current().then((m) => setMonitoring(m ?? null)).catch(() => {}) }, [canMonitor])
  useEffect(() => {
    const onEnded = () => setMonitoring(null)
    window.addEventListener('cc:monitor-ended', onEnded)
    return () => window.removeEventListener('cc:monitor-ended', onEnded)
  }, [])

  async function supervise(action: () => Promise<MonitorState | void>) {
    setSupBusy(true); setSupError(null); setMenuFor(null)
    try { const m = await action(); if (m) setMonitoring(m) }
    catch (e) { setSupError(e instanceof Error ? e.message : 'Failed.') }
    finally { setSupBusy(false) }
  }
  function takeOver(agentId: string) {
    setMenuFor(null)
    setSupError(null)
    setMonitoring(null)   // the server ends any listen-in as part of the take-over
    if (myRegistered) {
      // My portal is already open — take the call over into it (no second portal, which would
      // register the same extension twice and show the script in both).
      setSupBusy(true)
      supervisorApi.takeOver(agentId)
        .then((r) => setSupNote(r.phone
          ? 'Taken over — the call and script are in your agent portal. Switch to that tab.'
          : 'Taken over — the script is in your agent portal. Switch to that tab.'))
        .catch((e: Error) => setSupError(e.message))
        .finally(() => setSupBusy(false))
      return
    }
    // No portal open: open one inside the click (so the browser doesn't block it); it performs the
    // take-over itself once its softphone registers.
    window.open(`/agent?takeover=${agentId}`, 'cc-agent-portal')
  }
  function callAgent(agentId: string, name: string) {
    setSupError(null)
    if (!myRegistered) {
      window.open(`/agent?callagent=${agentId}`, 'cc-agent-portal')   // inside the click — not blocked
      return
    }
    setSupBusy(true)
    supervisorApi.callAgent(agentId)
      .then(() => setSupNote(`Calling ${name} from your agent portal's softphone…`))
      .catch((e: Error) => setSupError(e.message))
      .finally(() => setSupBusy(false))
  }

  useEffect(() => {
    if (!supNote) return
    const t = setTimeout(() => setSupNote(null), 10000)
    return () => clearTimeout(t)
  }, [supNote])

  async function unlock(agentId: string) {
    setUnlocking(agentId)
    try {
      await agentLockApi.unlock(agentId)
      setRows((prev) => prev.map((r) => r.agent_id === agentId ? { ...r, status_locked: false, sign_in_locked: false } : r))
    } catch { /* the row stays locked — nothing to undo */ }
    finally { setUnlocking(null) }
  }
  const knownIdsRef = useRef<Set<string>>(new Set())
  const refetchRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const load = useCallback(() => {
    dashboardWidgetsApi.agentList(config)
      .then(setRows)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.campaignId, config.clientId, config.groupId, config.loggedInOnly])

  useEffect(() => { load() }, [load])

  // Keep a fast lookup of which agents we already have rows for.
  useEffect(() => { knownIdsRef.current = new Set(rows.map((r) => r.agent_id)) }, [rows])

  // A live event (state or registration) for an agent we don't have a row for — e.g. one who
  // just logged in — can't be patched in place; pull the full list instead. Debounced so the
  // state + registration events that both land on login only cost one refetch.
  const refetchIfUnknown = useCallback((agentId: string) => {
    if (knownIdsRef.current.has(agentId)) return
    if (refetchRef.current) clearTimeout(refetchRef.current)
    refetchRef.current = setTimeout(load, 250)
  }, [load])

  useEffect(() => () => { if (refetchRef.current) clearTimeout(refetchRef.current) }, [])

  // Re-render every second so the Time column keeps ticking without re-fetching
  useEffect(() => {
    const id = setInterval(() => setTick((t) => t + 1), 1000)
    return () => clearInterval(id)
  }, [])

  // Patch the matching row in place — avoids a full reload flicker on every state change
  useEffect(() => {
    if (!liveEvent) return
    setRows((prev) => prev.map((r) =>
      r.agent_id === liveEvent.agentId
        ? { ...r, state_code: liveEvent.code, state_label: liveEvent.label, since: liveEvent.since }
        : r
    ))
    refetchIfUnknown(liveEvent.agentId)
  }, [liveEvent, refetchIfUnknown])

  // Same, for SIP registration presence — pushed independently of agent status
  useEffect(() => {
    if (!liveReg) return
    setRows((prev) => prev.map((r) =>
      r.agent_id === liveReg.agentId
        ? { ...r, registered: liveReg.registered, registered_since: liveReg.since }
        : r
    ))
    refetchIfUnknown(liveReg.agentId)
  }, [liveReg, refetchIfUnknown])

  // An agent opened or finished a script — the live-call links come from the server, so refetch
  // (debounced: a script that ends and a new one that starts land as two pushes).
  useEffect(() => {
    if (!liveSessions) return
    if (refetchRef.current) clearTimeout(refetchRef.current)
    refetchRef.current = setTimeout(load, 250)
  }, [liveSessions, load])

  function handleSort(column: SortColumn) {
    if (sortColumn === column) {
      setSortDirection((d) => (d === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortColumn(column)
      setSortDirection('asc')
    }
  }

  const sortedRows = useMemo(() => {
    const withDir = (cmp: number) => (sortDirection === 'asc' ? cmp : -cmp)
    return [...rows].sort((a, b) => {
      if (sortColumn === 'name') return withDir(a.name.localeCompare(b.name))
      if (sortColumn === 'state') return withDir(a.state_label.localeCompare(b.state_label))
      // time — sort by the underlying timestamp, not the formatted string; rows with no
      // timestamp always sort last regardless of direction
      const aTime = a.since ? new Date(a.since).getTime() : null
      const bTime = b.since ? new Date(b.since).getTime() : null
      if (aTime === null && bTime === null) return 0
      if (aTime === null) return 1
      if (bTime === null) return -1
      return withDir(aTime - bTime)
    })
  }, [rows, sortColumn, sortDirection])

  if (error) return <div className="text-xs text-red-400">{error}</div>

  const monitorBar = monitoring && (
    <div className="mb-2 rounded-lg bg-sky-950/50 border border-sky-800 px-2 py-1.5 flex flex-wrap items-center gap-1.5 text-[11px]">
      <span className="text-sky-200 font-medium mr-1"><HeadsetIcon size={13} className="inline -mt-0.5 mr-1" />{MONITOR_MODE_LABEL[monitoring.mode]} {monitoring.agentName}</span>
      {(['listen', 'coach', 'barge'] as MonitorMode[]).map((m) => (
        <button key={m} disabled={supBusy || monitoring.mode === m || (m === 'barge' && !canOverride)}
          onClick={() => supervise(() => supervisorApi.setMode(m))}
          className={`px-1.5 py-0.5 rounded ${monitoring.mode === m ? 'bg-sky-700 text-white' : 'text-sky-300 hover:bg-sky-900'} disabled:cursor-default`}>
          {m === 'listen' ? 'Listen' : m === 'coach' ? 'Coach' : 'Barge'}
        </button>
      ))}
      {canOverride && (
        <button onClick={() => takeOver(monitoring.agentId)} className="px-1.5 py-0.5 rounded text-amber-300 hover:bg-amber-950">Take over</button>
      )}
      <button disabled={supBusy} onClick={() => supervise(async () => { await supervisorApi.stop(); setMonitoring(null) })}
        className="ml-auto px-1.5 py-0.5 rounded text-gray-300 hover:bg-gray-800">End</button>
    </div>
  )

  return (
    <div className="h-full overflow-auto">
      {monitorBar}
      {supNote && <p className="text-[11px] text-sky-300 mb-1">{supNote}</p>}
      {supError && <p className="text-[11px] text-red-400 mb-1">{supError} <button className="text-gray-500 hover:text-gray-300 ml-1" onClick={() => setSupError(null)}><CloseIcon size={11} className="inline -mt-0.5" /></button></p>}
      <table className="w-full text-xs">
        <thead>
          <tr className="text-left text-gray-500 border-b border-gray-800">
            <th className="py-1 pr-2 font-medium cursor-pointer select-none hover:text-gray-300" onClick={() => handleSort('name')}>
              Agent<SortArrow active={sortColumn === 'name'} direction={sortDirection} />
            </th>
            <th className="py-1 pr-2 font-medium cursor-pointer select-none hover:text-gray-300" onClick={() => handleSort('state')}>
              State<SortArrow active={sortColumn === 'state'} direction={sortDirection} />
            </th>
            <th className="py-1 pr-2 font-medium select-none" title="SIP softphone registration">Phone</th>
            <th className="py-1 pr-2 font-medium cursor-pointer select-none hover:text-gray-300" onClick={() => handleSort('time')}>
              Time<SortArrow active={sortColumn === 'time'} direction={sortDirection} />
            </th>
            {canOpenCalls && <th className="py-1 font-medium select-none" title="Open the call the agent's script is on — review, correct data, resubmit an order (new tab)">Call</th>}
            {canMonitor && <th className="py-1 font-medium select-none" title="Monitor / Coach / Barge / Take over — your agent portal's softphone must be open">Supervise</th>}
          </tr>
        </thead>
        <tbody>
          {sortedRows.map((r) => (
            <tr key={r.agent_id} className="group border-b border-gray-800/60 last:border-0">
              <td className="py-1.5 pr-2 text-gray-200 max-w-[11rem]">
                <span className="truncate block">{r.name}</span>
                {(r.dedications ?? []).length > 0 ? (
                  <button onClick={() => setDedicating({ id: r.agent_id, name: r.name })}
                    title={(r.dedications ?? []).map((d) => `${d.campaigns.join(', ')} — ${d.summary}${d.active_now ? '' : ' (waiting for its next window)'}`).join('\n')}
                    className={`block max-w-full truncate text-left text-[10px] ${(r.dedications ?? []).some((d) => d.active_now) ? 'text-indigo-300 hover:text-indigo-200' : 'text-gray-500 hover:text-gray-300'}`}>
                    Dedicated · {[...new Set((r.dedications ?? []).flatMap((d) => d.campaigns))].join(', ')}
                  </button>
                ) : canDedicate && (
                  <button onClick={() => setDedicating({ id: r.agent_id, name: r.name })}
                    className="hidden group-hover:block text-[10px] text-gray-500 hover:text-indigo-300">Dedicate…</button>
                )}
                {r.status_locked && (
                  <span className="flex items-center gap-1.5 text-[10px] text-red-300"
                    title={`Locked by ${r.locked_by ?? 'a supervisor'}${r.lock_reason ? `: ${r.lock_reason}` : ''}`}>
                    <LockIcon size={11} className="inline -mt-0.5 mr-1" />{r.sign_in_locked ? 'sign-in locked' : 'locked'}
                    {canUnlock && (
                      <button onClick={() => unlock(r.agent_id)} disabled={unlocking === r.agent_id}
                        className="text-indigo-400 hover:text-indigo-300 disabled:opacity-50">Unlock</button>
                    )}
                  </span>
                )}
              </td>
              <td className="py-1.5 pr-2">
                <span className="inline-flex items-center gap-1.5 text-gray-300">
                  <span className={`w-2 h-2 rounded-full shrink-0 ${STATE_DOT[r.state_code] ?? 'bg-gray-500'}`} />
                  {r.state_label}
                </span>
              </td>
              <td className="py-1.5 pr-2">
                {r.registered ? (
                  <span
                    className="inline-flex items-center gap-1 text-green-500"
                    title={r.registered_since ? `Registered · ${formatDuration(r.registered_since)}` : 'Registered'}
                  >
                    <span className="w-2 h-2 rounded-full shrink-0 bg-green-500" />
                    <span className="text-[10px] uppercase tracking-wide">Reg</span>
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1 text-gray-600" title="Softphone not registered">
                    <span className="w-2 h-2 rounded-full shrink-0 border border-gray-600" />
                    <span className="text-[10px] uppercase tracking-wide">Off</span>
                  </span>
                )}
              </td>
              <td className="py-1.5 pr-2 text-gray-500">{formatDuration(r.since)}</td>
              {canOpenCalls && (
                <td className="py-1.5 whitespace-nowrap">
                  {(r.live_calls ?? []).map((c, i) => (
                    <a
                      key={c.call_record_id}
                      href={`/admin/calls/${c.call_record_id}`}
                      target="_blank"
                      rel="noopener noreferrer"
                      title={`${c.flow_name ?? 'Script'} · open ${formatDuration(c.started_at)} — review / correct this call (new tab)`}
                      className="text-indigo-400 hover:text-indigo-300 mr-2"
                    >
                      {(r.live_calls ?? []).length > 1 ? `Call ${i + 1}` : 'Open'} <ExternalLinkIcon size={11} className="inline -mt-0.5" />
                    </a>
                  ))}
                </td>
              )}
              {canMonitor && (
                <td className="py-1.5 relative whitespace-nowrap">
                  {r.agent_id !== myId && r.state_code !== 'logged_out' && (
                    <button onClick={() => setViewing({ id: r.agent_id, name: r.name })}
                      className="text-violet-300 hover:text-violet-200 mr-2" title={`View ${r.name}'s screen (they'll see that you're watching)`}>
                      <ScreenIcon size={15} />
                    </button>
                  )}
                  {r.agent_id !== myId && !r.on_live_call && r.registered && r.state_code !== 'logged_out' && (
                    <button onClick={() => callAgent(r.agent_id, r.name)} disabled={supBusy}
                      className="text-violet-300 hover:text-violet-200 disabled:opacity-50 mr-2"
                      title={`Call ${r.name} (internal — QA review, training)${myRegistered ? '' : ' — opens your agent portal'}`}>
                      <PhoneIcon size={15} />
                    </button>
                  )}
                  {r.agent_id !== myId && (r.on_live_call || (canOverride && (r.live_calls ?? []).length > 0)) && (
                    <button data-supervise-toggle disabled={supBusy}
                      onClick={(e) => {
                        const rect = e.currentTarget.getBoundingClientRect()
                        setMenuPos({ top: rect.bottom + 4, right: window.innerWidth - rect.right })
                        setMenuFor(menuFor === r.agent_id ? null : r.agent_id)
                      }}
                      className="text-sky-400 hover:text-sky-300 disabled:opacity-50" title={r.on_live_call ? 'On a live call' : 'Script open (no phone call)'}>
                      <span className="inline-flex items-center gap-0.5"><HeadsetIcon size={15} /><ChevronDownIcon size={11} /></span>
                    </button>
                  )}
                  {menuFor === r.agent_id && menuPos && createPortal(
                    <div ref={menuRef} style={{ top: menuPos.top, right: menuPos.right }}
                      className="fixed z-50 w-52 whitespace-normal bg-gray-800 border border-gray-700 rounded-lg shadow-xl overflow-hidden text-xs">
                      {r.on_live_call && (
                        <>
                          <button className="w-full text-left px-3 py-1.5 hover:bg-gray-700 text-gray-200" onClick={() => supervise(() => supervisorApi.start(r.agent_id, 'listen'))}>Monitor <span className="text-gray-500">— listen only</span></button>
                          <button className="w-full text-left px-3 py-1.5 hover:bg-gray-700 text-gray-200" onClick={() => supervise(() => supervisorApi.start(r.agent_id, 'coach'))}>Coach <span className="text-gray-500">— agent hears you</span></button>
                          {canOverride && <button className="w-full text-left px-3 py-1.5 hover:bg-gray-700 text-gray-200" onClick={() => supervise(() => supervisorApi.start(r.agent_id, 'barge'))}>Barge in <span className="text-gray-500">— both hear you</span></button>}
                        </>
                      )}
                      {canOverride && <button className="w-full text-left px-3 py-1.5 hover:bg-gray-700 text-amber-300 border-t border-gray-700" onClick={() => takeOver(r.agent_id)}>Take over…</button>}
                      <p className="px-3 py-1.5 text-[10px] text-gray-500 border-t border-gray-700">
                        {myRegistered ? 'Uses your open agent portal.' : 'Opens your agent portal (softphone needed for calls).'}
                      </p>
                    </div>,
                    document.body,
                  )}
                </td>
              )}
            </tr>
          ))}
          {rows.length === 0 && (
            <tr>
              <td colSpan={4 + (canOpenCalls ? 1 : 0) + (canMonitor ? 1 : 0)} className="py-4 text-center text-gray-600">No agents match this filter.</td>
            </tr>
          )}
        </tbody>
      </table>
      {viewing && <ScreenViewModal agentId={viewing.id} agentName={viewing.name} onClose={() => setViewing(null)} />}
      {dedicating && (
        <DedicationModal agentId={dedicating.id} agentName={dedicating.name} canManage={canDedicate}
          onClose={() => { setDedicating(null); load() }} />
      )}
    </div>
  )
}
