import { useCallback, useEffect, useMemo, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import PortalShell from '../../components/portal/PortalShell'
import {
  acknowledgeHealth, getHealth, muteHealth, removeHealthPerson, sendHealthTestAlert, setHealthLevels, setHealthRecipients,
  type HealthCheckRow, type HealthOverview, type HealthStatus,
} from '../../api/portal'
import { useIsPortalOwner, usePortalAuthStore } from '../../stores/portalAuthStore'
import { reconnectForever } from '../../utils/hubRetry'

/**
 * Platform health (S184). The Worker measures everything once a minute; this page shows the result live (pushed over
 * /hubs/platform), the last 24 hours per check, and the incident log. Owners adjust levels, mute checks during planned
 * work and choose who gets alert emails; Support can view and acknowledge.
 */

const DOT: Record<HealthStatus, string> = { ok: 'bg-emerald-400', warning: 'bg-amber-400', critical: 'bg-red-500', unknown: 'bg-gray-500' }
const TEXT: Record<HealthStatus, string> = { ok: 'text-emerald-300', warning: 'text-amber-300', critical: 'text-red-400', unknown: 'text-gray-400' }
const LABEL: Record<HealthStatus, string> = { ok: 'OK', warning: 'Warning', critical: 'Critical', unknown: 'Not judged' }
const SLOT: Record<HealthStatus, string> = { ok: 'bg-emerald-500/70', warning: 'bg-amber-400', critical: 'bg-red-500', unknown: 'bg-gray-600' }
const AREAS = ['Core services', 'Telephony', 'Integrations', 'Background jobs', 'Background work', 'Expiring']

const ago = (iso: string | null, now: number) => {
  if (!iso) return ''
  const s = Math.max(0, Math.round((now - new Date(iso).getTime()) / 1000))
  return s < 60 ? `${s}s ago` : s < 3600 ? `${Math.round(s / 60)} min ago` : s < 86400 ? `${Math.round(s / 3600)} h ago` : `${Math.round(s / 86400)} d ago`
}
const fmtTime = (iso: string) => new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
const fmtValue = (c: HealthCheckRow) => (c.value == null ? '' : `${Math.round(c.value * 10) / 10}${c.unit ? ` ${c.unit}` : ''}`)

export default function HealthPage() {
  const isOwner = useIsPortalOwner()
  const [data, setData] = useState<HealthOverview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [now, setNow] = useState(() => Date.now())
  const [live, setLive] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)

  const load = useCallback(() => {
    getHealth().then((d) => { setData(d); setError(null) }).catch((e: Error) => setError(e.message))
  }, [])

  useEffect(() => {
    load()
    const tick = setInterval(() => setNow(Date.now()), 5000)   // only the "x ago" labels
    const conn = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/platform', { accessTokenFactory: () => usePortalAuthStore.getState().token ?? '' })
      .withAutomaticReconnect(reconnectForever)
      .configureLogging(signalR.LogLevel.Warning)
      .build()
    conn.on('receiveHealthChanged', () => load())
    conn.onreconnected(() => { setLive(true); load() })
    conn.onreconnecting(() => setLive(false))
    conn.start().then(() => setLive(true)).catch(() => setLive(false))
    return () => { clearInterval(tick); void conn.stop() }
  }, [load])

  const worst = useMemo<HealthStatus>(() => {
    const s = data?.checks.filter((c) => !c.mutedUntil).map((c) => c.status) ?? []
    return s.includes('critical') ? 'critical' : s.includes('warning') ? 'warning' : s.length ? 'ok' : 'unknown'
  }, [data])
  const problems = data?.checks.filter((c) => (c.status === 'critical' || c.status === 'warning') && !c.mutedUntil) ?? []

  async function act(p: Promise<unknown>) {
    try { await p; load() } catch (e) { setError(e instanceof Error ? e.message : 'That didn\'t work.') }
  }

  return (
    <PortalShell>
      <div className="p-6 max-w-6xl">
        <div className="flex flex-wrap items-start justify-between gap-4 mb-5">
          <div>
            <h1 className="text-white text-xl font-semibold flex items-center gap-3">
              Platform health
              <span className={`inline-flex items-center gap-1.5 text-xs font-medium px-2 py-0.5 rounded-full border ${worst === 'ok'
                ? 'border-emerald-800 bg-emerald-950/50 text-emerald-300' : worst === 'warning' ? 'border-amber-800 bg-amber-950/40 text-amber-300'
                : worst === 'critical' ? 'border-red-800 bg-red-950/40 text-red-300' : 'border-gray-700 text-gray-400'}`}>
                <span className={`w-2 h-2 rounded-full ${DOT[worst]}`} />
                {worst === 'ok' ? 'All systems normal' : worst === 'unknown' ? 'Waiting for the first check' : `${problems.length} need${problems.length === 1 ? 's' : ''} attention`}
              </span>
            </h1>
            <p className="text-gray-500 text-sm mt-1">
              Checked once a minute by the Worker{data?.checkedAt ? ` · last ${ago(data.checkedAt, now)}` : ''}
              <span className={`ml-2 text-xs ${live ? 'text-emerald-500' : 'text-gray-600'}`}>{live ? '● live' : '○ reconnecting'}</span>
            </p>
          </div>
        </div>
        {error && <p className="mb-4 text-sm text-red-400">{error}</p>}
        {!data && !error && <p className="text-gray-400 text-sm">Loading…</p>}

        {data && AREAS.map((area) => {
          const rows = data.checks.filter((c) => c.area === area)
          if (rows.length === 0) return null
          return (
            <section key={area} className="mb-6">
              <h2 className="text-gray-400 text-xs font-semibold uppercase tracking-wider mb-2">{area}</h2>
              <div className="bg-gray-900 rounded-xl border border-gray-800 divide-y divide-gray-800">
                {rows.map((c) => (
                  <CheckRow key={c.key} c={c} now={now} isOwner={isOwner} editing={editing === c.key}
                    onEdit={() => setEditing(editing === c.key ? null : c.key)} onDone={() => { setEditing(null); load() }} act={act} />
                ))}
              </div>
            </section>
          )
        })}

        {data && (
          <section className="mb-6">
            <h2 className="text-gray-400 text-xs font-semibold uppercase tracking-wider mb-2">Incidents</h2>
            <div className="bg-gray-900 rounded-xl border border-gray-800">
              {data.incidents.length === 0 && <p className="text-gray-500 text-sm px-4 py-3">No incidents recorded.</p>}
              {data.incidents.map((i) => (
                <div key={i.id} className="flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-2.5 border-b border-gray-800 last:border-0 text-sm">
                  <span className={`w-2 h-2 rounded-full ${DOT[i.severity]}`} />
                  <span className="text-white w-56 truncate">{i.name}</span>
                  <span className="text-gray-400 w-40">{fmtTime(i.startedAt)}</span>
                  <span className={i.resolvedAt ? 'text-gray-500 w-44' : 'text-red-300 w-44'}>
                    {i.resolvedAt ? `resolved after ${Math.max(1, Math.round((new Date(i.resolvedAt).getTime() - new Date(i.startedAt).getTime()) / 60000))} min` : 'ongoing'}
                  </span>
                  <span className="text-gray-500 text-xs flex-1 min-w-0 truncate" title={i.detail ?? ''}>{i.detail}</span>
                </div>
              ))}
            </div>
          </section>
        )}

        {data?.alerting && <AlertingCard alerting={data.alerting} onChanged={load} />}

        <section className="mb-6 bg-gray-900 rounded-xl border border-gray-800 p-5">
          <h2 className="text-white text-sm font-semibold mb-1">Outside uptime monitor</h2>
          <p className="text-gray-400 text-sm">
            If the server or its internet connection goes down, nothing here can send an alert. Point a free outside monitor
            (UptimeRobot, Better Stack…) at <code className="text-sky-300 bg-gray-800 rounded px-1.5 py-0.5">https://contactconnection.io/api/v1/health</code>, checking
            every minute — it answers <code className="text-gray-300">{'{"status":"ok"}'}</code> while the API, database and Redis are up.
          </p>
        </section>
      </div>
    </PortalShell>
  )
}

function CheckRow({ c, now, isOwner, editing, onEdit, onDone, act }: {
  c: HealthCheckRow; now: number; isOwner: boolean; editing: boolean
  onEdit: () => void; onDone: () => void; act: (p: Promise<unknown>) => Promise<void>
}) {
  const problem = c.status === 'warning' || c.status === 'critical'
  const muted = !!c.mutedUntil
  return (
    <div className={`px-4 py-3 ${muted ? 'opacity-60' : ''}`}>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <span className={`w-2.5 h-2.5 rounded-full shrink-0 ${DOT[c.status]} ${c.status === 'critical' && !muted ? 'animate-pulse' : ''}`} />
        <div className="w-56 min-w-0">
          <p className="text-white text-sm truncate" title={c.description}>{c.name}</p>
          <p className={`text-xs ${TEXT[c.status]}`}>
            {LABEL[c.status]}{c.statusSince && c.status !== 'ok' ? ` · ${ago(c.statusSince, now)}` : ''}
          </p>
        </div>
        <div className="w-36 text-sm text-gray-200 tabular-nums">{fmtValue(c)}</div>
        <div className="flex-1 min-w-[12rem] text-xs text-gray-400 truncate" title={c.detail}>{c.detail}</div>
        <History slots={c.history} />
        <div className="flex items-center gap-2 text-xs shrink-0">
          {muted && <span className="text-gray-400" title={`Muted until ${fmtTime(c.mutedUntil!)}`}>muted</span>}
          {problem && !c.acknowledgedAt && (
            <button onClick={() => void act(acknowledgeHealth(c.key))} className="text-amber-300 hover:text-amber-200 border border-amber-800 rounded px-2 py-0.5">Acknowledge</button>
          )}
          {problem && c.acknowledgedAt && <span className="text-gray-500" title={`by ${c.acknowledgedBy}`}>acknowledged</span>}
          {isOwner && (
            <button onClick={onEdit} className="text-gray-400 hover:text-white px-1" title="Levels and mute">{editing ? 'Close' : 'Settings'}</button>
          )}
        </div>
      </div>
      {editing && <CheckSettings c={c} onDone={onDone} act={act} />}
    </div>
  )
}

function History({ slots }: { slots: (HealthStatus | null)[] | null }) {
  const cells = slots ?? Array<HealthStatus | null>(96).fill(null)
  return (
    <div className="flex gap-px h-4 items-stretch shrink-0" title="Last 24 hours, 15 minutes per bar (newest on the right)">
      {cells.map((s, i) => <span key={i} className={`w-[3px] rounded-[1px] ${s ? SLOT[s] : 'bg-gray-800'}`} />)}
    </div>
  )
}

function CheckSettings({ c, onDone, act }: { c: HealthCheckRow; onDone: () => void; act: (p: Promise<unknown>) => Promise<void> }) {
  const [warn, setWarn] = useState(c.warn?.toString() ?? '')
  const [crit, setCrit] = useState(c.crit?.toString() ?? '')
  const [err, setErr] = useState<string | null>(null)
  const custom = c.warn !== c.defaultWarn || c.crit !== c.defaultCrit

  async function save() {
    const w = Number(warn), k = Number(crit)
    if (warn === '' || crit === '' || Number.isNaN(w) || Number.isNaN(k)) { setErr('Enter both levels as numbers.'); return }
    try { await setHealthLevels(c.key, w, k); onDone() } catch (e) { setErr(e instanceof Error ? e.message : 'Save failed.') }
  }

  const input = 'w-24 bg-gray-800 text-white rounded px-2 py-1 text-sm outline-none focus:ring-2 focus:ring-indigo-500'
  return (
    <div className="mt-3 ml-6 flex flex-wrap items-end gap-x-6 gap-y-3 bg-gray-950/50 border border-gray-800 rounded-lg p-3">
      {c.adjustable ? (
        <div className="flex flex-wrap items-end gap-3">
          <label className="text-xs text-amber-300">Warning {c.higherIsWorse ? 'at or above' : 'at or below'}
            <input value={warn} onChange={(e) => setWarn(e.target.value)} className={`${input} block mt-1`} />
          </label>
          <label className="text-xs text-red-300">Critical {c.higherIsWorse ? 'at or above' : 'at or below'}
            <input value={crit} onChange={(e) => setCrit(e.target.value)} className={`${input} block mt-1`} />
          </label>
          <span className="text-xs text-gray-500 pb-1.5">{c.unit}</span>
          <button onClick={() => void save()} className="bg-indigo-600 hover:bg-indigo-500 text-white rounded px-3 py-1 text-xs">Save levels</button>
          {custom && (
            <button onClick={() => void act(setHealthLevels(c.key, null, null, true)).then(onDone)} className="text-gray-400 hover:text-white text-xs">
              Reset to {c.defaultWarn} / {c.defaultCrit}
            </button>
          )}
        </div>
      ) : <p className="text-xs text-gray-500">This check is up or down — no levels to set.</p>}
      <div className="flex items-center gap-2 text-xs">
        <span className="text-gray-400">Mute alerts</span>
        {[[60, '1 h'], [240, '4 h'], [1440, '24 h']].map(([m, l]) => (
          <button key={m} onClick={() => void act(muteHealth(c.key, m as number)).then(onDone)} className="border border-gray-700 hover:border-gray-500 text-gray-300 rounded px-2 py-0.5">{l}</button>
        ))}
        {c.mutedUntil && <button onClick={() => void act(muteHealth(c.key, 0)).then(onDone)} className="text-sky-300 hover:text-sky-200">Unmute</button>}
      </div>
      {err && <p className="text-xs text-red-400 w-full">{err}</p>}
    </div>
  )
}

function AlertingCard({ alerting, onChanged }: { alerting: NonNullable<HealthOverview['alerting']>; onChanged: () => void }) {
  const [extra, setExtra] = useState(alerting.extra.join(', '))
  const [msg, setMsg] = useState<string | null>(null)
  const [confirmRemove, setConfirmRemove] = useState<string | null>(null)
  useEffect(() => { setExtra(alerting.extra.join(', ')) }, [alerting.extra])

  async function save() {
    try {
      await setHealthRecipients(extra.split(/[,;\s]+/).filter(Boolean))
      setMsg('Saved.'); onChanged()
    } catch (e) { setMsg(e instanceof Error ? e.message : 'Save failed.') }
  }
  async function test() {
    try { const r = await sendHealthTestAlert(); setMsg(`Test sent to ${r.sentTo.join(', ')}.`) }
    catch (e) { setMsg(e instanceof Error ? e.message : 'Send failed.') }
  }

  return (
    <section className="mb-6 bg-gray-900 rounded-xl border border-gray-800 p-5">
      <h2 className="text-white text-sm font-semibold mb-1">Who gets alerts</h2>
      <p className="text-gray-500 text-xs mb-3">
        Every Portal Owner (once they've signed in to the Portal), plus the extra addresses below. An alert goes out when a check
        turns warning or critical and again when it recovers; an unacknowledged critical repeats every 30 minutes.
      </p>
      <div className="flex flex-col gap-1.5 mb-4">
        {alerting.owners.length === 0 && <p className="text-amber-300 text-xs">No Owners have signed in since alerts were set up — sign out and back in.</p>}
        {alerting.owners.map((o) => (
          <div key={o.entraOid} className="flex items-center gap-3 text-sm">
            <span className="text-white">{o.name || o.email}</span>
            <span className="text-gray-400">{o.email}</span>
            <span className="text-gray-600 text-xs">Owner</span>
            {confirmRemove === o.entraOid ? (
              <span className="text-xs flex items-center gap-2">
                <span className="text-gray-300">Stop their alerts?</span>
                <button onClick={() => void removeHealthPerson(o.entraOid).then(onChanged)} className="text-red-400 hover:text-red-300">Stop</button>
                <button onClick={() => setConfirmRemove(null)} className="text-gray-400">Keep</button>
              </span>
            ) : (
              <button onClick={() => setConfirmRemove(o.entraOid)} className="text-gray-500 hover:text-red-400 text-xs" title="For someone who has left — they're added back if they sign in again">Remove</button>
            )}
          </div>
        ))}
      </div>
      <label className="text-xs text-gray-400">Extra addresses (comma-separated)</label>
      <div className="flex flex-wrap items-center gap-2 mt-1">
        <input value={extra} onChange={(e) => setExtra(e.target.value)} placeholder="ops@yourcompany.com"
          className="flex-1 min-w-[16rem] bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500" />
        <button onClick={() => void save()} className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium">Save</button>
        <button onClick={() => void test()} className="bg-gray-800 hover:bg-gray-700 text-gray-200 rounded-lg px-4 py-2 text-sm">Send a test alert</button>
      </div>
      {msg && <p className="text-xs text-gray-300 mt-2">{msg}</p>}
    </section>
  )
}
