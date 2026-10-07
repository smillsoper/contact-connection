import { useEffect, useMemo, useState } from 'react'
import { createPortal } from 'react-dom'
import { dedicationsApi, type Dedication, type DedicationMode, type DedicationWindow } from '../../api/dedications'
import { listCampaigns, type Campaign } from '../../api/telephony'
import { CloseIcon, DeleteIcon } from '../icons/Icons'

/**
 * Dedicate an agent to campaigns (S183), from the supervisor dashboard's Agent List. While a dedication is active the
 * agent takes calls ONLY from those campaigns — even ones they aren't assigned to — and their other assignments resume
 * by themselves when it ends. For a while, until a date and time, or on weekly windows.
 */

const DAY_LETTERS = ['S', 'M', 'T', 'W', 'T', 'F', 'S']
const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
const inputCls = 'bg-gray-800 text-white rounded-lg px-2.5 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500'

export default function DedicationModal({ agentId, agentName, canManage, onClose }: {
  agentId: string
  agentName: string
  canManage: boolean
  onClose: () => void
}) {
  const [current, setCurrent] = useState<Dedication[] | null>(null)
  const [campaigns, setCampaigns] = useState<Campaign[]>([])
  const [filter, setFilter] = useState('')
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [mode, setMode] = useState<DedicationMode>('duration')
  const [hours, setHours] = useState(1)
  const [minutes, setMinutes] = useState(0)
  const [until, setUntil] = useState('')
  const [windows, setWindows] = useState<DedicationWindow[]>([{ days: [1, 2, 3, 4, 5], start: '09:00', end: '17:00' }])
  const [lastDay, setLastDay] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = () => dedicationsApi.list(agentId).then(setCurrent).catch((e: Error) => setError(e.message))
  useEffect(() => {
    load()
    if (canManage) listCampaigns().then((all) => setCampaigns(all.filter((c) => c.status === 'active').sort((a, b) => a.name.localeCompare(b.name)))).catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [agentId, canManage])

  const shown = useMemo(() => {
    const f = filter.trim().toLowerCase()
    return f ? campaigns.filter((c) => c.name.toLowerCase().includes(f)) : campaigns
  }, [campaigns, filter])

  function toggle(id: string) {
    setPicked((p) => { const n = new Set(p); if (n.has(id)) n.delete(id); else n.add(id); return n })
  }
  function patchWindow(i: number, patch: Partial<DedicationWindow>) {
    setWindows((ws) => ws.map((w, j) => (j === i ? { ...w, ...patch } : w)))
  }

  async function save() {
    setBusy(true); setError(null)
    try {
      await dedicationsApi.create({
        agentId, campaignIds: [...picked], mode, note: note.trim() || undefined,
        ...(mode === 'duration' ? { minutes: hours * 60 + minutes } : {}),
        ...(mode === 'until' ? { endsAt: until ? new Date(until).toISOString() : undefined } : {}),
        ...(mode === 'schedule' ? { windows, lastDay: lastDay || undefined } : {}),
      })
      setPicked(new Set()); setNote('')
      await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save.')
    } finally { setBusy(false) }
  }

  async function end(id: string) {
    setBusy(true); setError(null)
    try { await dedicationsApi.end(id); await load() }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not end it.') }
    finally { setBusy(false) }
  }

  const durationOk = hours * 60 + minutes > 0
  const canSave = picked.size > 0 && !busy && (mode !== 'duration' || durationOk) && (mode !== 'until' || !!until)
    && (mode !== 'schedule' || (windows.length > 0 && windows.every((w) => w.days.length > 0 && w.start !== w.end)))

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div className="w-full max-w-xl max-h-[90vh] overflow-y-auto rounded-xl border border-gray-700 bg-gray-900 shadow-2xl">
        <div className="flex items-center justify-between px-5 py-3 border-b border-gray-800">
          <h2 className="text-white font-semibold">Dedicate {agentName}</h2>
          <button onClick={onClose} className="text-gray-500 hover:text-white"><CloseIcon size={16} /></button>
        </div>
        <div className="p-5 space-y-5 text-sm">
          <p className="text-xs text-gray-400">
            While a dedication is on, {agentName} takes calls <b className="text-gray-200">only</b> from its campaigns — even ones they aren't
            assigned to. Their other campaigns pause and come back by themselves when it ends.
          </p>

          <section>
            <h3 className="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-2">Current</h3>
            {current === null ? <p className="text-gray-500 text-xs">Loading…</p>
              : current.length === 0 ? <p className="text-gray-500 text-xs">Not dedicated — taking calls from their assigned campaigns.</p>
              : (
                <ul className="space-y-2">
                  {current.map((d) => (
                    <li key={d.id} className="rounded-lg border border-gray-800 bg-gray-950/60 px-3 py-2">
                      <div className="flex items-start gap-2">
                        <div className="flex-1 min-w-0">
                          <p className="text-gray-200">{d.campaigns.map((c) => c.name).join(', ')}</p>
                          <p className="text-xs text-gray-400">{d.summary}</p>
                          <p className="text-[11px] text-gray-500">
                            <span className={d.activeNow ? 'text-emerald-400' : 'text-amber-300'}>{d.activeNow ? 'On now' : 'Waiting for its next window'}</span>
                            {' · '}by {d.createdByName || 'a supervisor'}{d.note ? ` · ${d.note}` : ''}
                          </p>
                        </div>
                        {canManage && <button disabled={busy} onClick={() => end(d.id)} className="text-xs text-red-300 hover:text-red-200 disabled:opacity-50">End</button>}
                      </div>
                    </li>
                  ))}
                </ul>
              )}
          </section>

          {canManage && (
            <section className="space-y-4">
              <h3 className="text-xs font-semibold uppercase tracking-wide text-gray-500">New dedication</h3>

              <div>
                <div className="flex items-center justify-between mb-1.5">
                  <span className="text-gray-300">Campaigns{picked.size > 0 && <span className="text-gray-500"> · {picked.size} chosen</span>}</span>
                  <input value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Filter…" className={`${inputCls} w-40 py-1 text-xs`} />
                </div>
                <div className="max-h-44 overflow-y-auto rounded-lg border border-gray-800 divide-y divide-gray-800/60">
                  {shown.map((c) => (
                    <label key={c.id} className="flex items-center gap-2 px-3 py-1.5 hover:bg-gray-800/50 cursor-pointer">
                      <input type="checkbox" checked={picked.has(c.id)} onChange={() => toggle(c.id)} className="accent-indigo-500" />
                      <span className="text-gray-200">{c.name}</span>
                    </label>
                  ))}
                  {shown.length === 0 && <p className="px-3 py-2 text-xs text-gray-500">No active campaigns match.</p>}
                </div>
              </div>

              <div>
                <span className="text-gray-300 block mb-1.5">How long</span>
                <div className="flex flex-wrap gap-2 mb-3">
                  {([['duration', 'For a while'], ['until', 'Until a date & time'], ['schedule', 'Weekly schedule']] as const).map(([m, label]) => (
                    <button key={m} onClick={() => setMode(m)}
                      className={`px-3 py-1 rounded-full text-xs border ${mode === m ? 'border-indigo-500 bg-indigo-600/30 text-white' : 'border-gray-700 text-gray-400 hover:text-gray-200'}`}>{label}</button>
                  ))}
                </div>
                {mode === 'duration' && (
                  <div className="flex items-center gap-2 text-gray-300">
                    <input type="number" min={0} max={744} value={hours} onChange={(e) => setHours(Math.max(0, Number(e.target.value) || 0))} className={`${inputCls} w-20`} /> hours
                    <input type="number" min={0} max={59} step={5} value={minutes} onChange={(e) => setMinutes(Math.min(59, Math.max(0, Number(e.target.value) || 0)))} className={`${inputCls} w-20`} /> minutes
                    <span className="text-xs text-gray-500">from now</span>
                  </div>
                )}
                {mode === 'until' && (
                  <input type="datetime-local" value={until} onChange={(e) => setUntil(e.target.value)} className={inputCls} />
                )}
                {mode === 'schedule' && (
                  <div className="space-y-2">
                    {windows.map((w, i) => (
                      <div key={i} className="flex flex-wrap items-center gap-2">
                        <div className="flex gap-0.5">
                          {DAY_LETTERS.map((l, d) => (
                            <button key={d} title={DAY_NAMES[d]}
                              onClick={() => patchWindow(i, { days: w.days.includes(d) ? w.days.filter((x) => x !== d) : [...w.days, d].sort() })}
                              className={`w-6 h-6 rounded text-[11px] font-medium ${w.days.includes(d) ? 'bg-indigo-600 text-white' : 'bg-gray-800 text-gray-500 hover:text-gray-300'}`}>{l}</button>
                          ))}
                        </div>
                        <input type="time" value={w.start} onChange={(e) => patchWindow(i, { start: e.target.value })} className={`${inputCls} py-1`} />
                        <span className="text-gray-500">to</span>
                        <input type="time" value={w.end} onChange={(e) => patchWindow(i, { end: e.target.value })} className={`${inputCls} py-1`} />
                        {windows.length > 1 && (
                          <button onClick={() => setWindows((ws) => ws.filter((_, j) => j !== i))} className="text-gray-500 hover:text-red-300" title="Remove this window"><DeleteIcon size={14} /></button>
                        )}
                      </div>
                    ))}
                    <button onClick={() => setWindows((ws) => [...ws, { days: [6], start: '09:00', end: '13:00' }])}
                      className="text-xs text-indigo-400 hover:text-indigo-300">+ Add another window</button>
                    <div className="flex items-center gap-2 text-gray-300 pt-1">
                      Last day <input type="date" value={lastDay} onChange={(e) => setLastDay(e.target.value)} className={`${inputCls} py-1`} />
                      <span className="text-xs text-gray-500">optional — leave empty to keep it until ended</span>
                    </div>
                    <p className="text-[11px] text-gray-500">Times are in your account's time zone. An end earlier than the start runs past midnight.</p>
                  </div>
                )}
              </div>

              <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={200} placeholder="Note (optional) — e.g. covering the spring promo"
                className={`${inputCls} w-full`} />

              <div className="flex items-center justify-end gap-3">
                {error && <p className="text-xs text-red-400 flex-1">{error}</p>}
                <button onClick={onClose} className="text-gray-400 hover:text-white text-sm px-3 py-1.5">Close</button>
                <button disabled={!canSave} onClick={save}
                  className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded-lg px-4 py-1.5 text-sm font-medium">
                  {busy ? 'Saving…' : 'Dedicate'}
                </button>
              </div>
            </section>
          )}
          {!canManage && error && <p className="text-xs text-red-400">{error}</p>}
        </div>
      </div>
    </div>,
    document.body,
  )
}
