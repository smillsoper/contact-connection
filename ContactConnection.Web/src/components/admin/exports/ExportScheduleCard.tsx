import { useEffect, useState } from 'react'
import { defaultSchedule, exportsApi, type ExportDefinition, type ExportSchedule, type NextRun } from '../../../api/exports'

// Export schedule (S180, session 2): when it runs (its own time zone) and which calls each file covers (whole days in the
// export's time zone). Saved separately from the layout — the vendor approves what's in the file, not when it's sent.

const input = 'w-full bg-gray-900 border border-gray-700 rounded px-2 py-1.5 text-sm text-gray-100 focus:outline-none focus:border-indigo-500'
const label = 'block text-xs text-gray-400 mb-1'
const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
const ZONES = ['America/New_York', 'America/Chicago', 'America/Denver', 'America/Phoenix', 'America/Los_Angeles',
  'America/Anchorage', 'Pacific/Honolulu', 'UTC']

function fmt(iso: string, zone: string, withTime = true) {
  return new Date(iso).toLocaleString(undefined, {
    timeZone: zone, weekday: 'short', month: 'short', day: 'numeric',
    ...(withTime ? { hour: 'numeric', minute: '2-digit', timeZoneName: 'short' } : {}),
  })
}

/** A window [start, end) as the days it covers, in the export's zone ("Mon, Oct 5" or "Mon, Sep 28 – Sun, Oct 4"). */
function days(run: NextRun, zone: string) {
  const startsAtMidnight = new Date(run.windowStart).toLocaleTimeString('en-US', { timeZone: zone, hour12: false }).startsWith('00:00')
  if (!startsAtMidnight) return `${fmt(run.windowStart, zone)} → ${fmt(run.windowEnd, zone)}`
  const lastDay = new Date(new Date(run.windowEnd).getTime() - 1000).toISOString()
  const a = fmt(run.windowStart, zone, false)
  const b = fmt(lastDay, zone, false)
  return a === b ? a : `${a} – ${b}`
}

export default function ExportScheduleCard({ def, onSaved }: { def: ExportDefinition; onSaved: (d: ExportDefinition) => void }) {
  const [schedule, setSchedule] = useState<ExportSchedule | null>(def.schedule)
  const [dirty, setDirty] = useState(false)
  const [preview, setPreview] = useState<{ error: string | null; description: string | null; nextRuns: NextRun[] } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => { setSchedule(def.schedule); setDirty(false) }, [def.id, def.schedule]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!schedule) { setPreview(null); return }
    const t = setTimeout(() => {
      exportsApi.schedulePreview(schedule, def.spec.timeZone).then(setPreview).catch(() => setPreview(null))
    }, 300)
    return () => clearTimeout(t)
  }, [schedule, def.spec.timeZone])

  const set = (patch: Partial<ExportSchedule>) => { setSchedule({ ...(schedule ?? defaultSchedule()), ...patch }); setDirty(true) }

  async function save() {
    setSaving(true); setError(null)
    try { onSaved(await exportsApi.saveSchedule(def.id, schedule)); setDirty(false) }
    catch (e) { setError(e instanceof Error ? e.message : 'Save failed.') }
    finally { setSaving(false) }
  }

  return (
    <div className="bg-gray-800/60 border border-gray-700 rounded-lg p-4 mb-4">
      <div className="flex flex-wrap items-center justify-between gap-3 mb-3">
        <h2 className="text-sm font-semibold text-gray-100">Schedule</h2>
        <div className="flex items-center gap-3">
          <label className="flex items-center gap-2 text-sm text-gray-300">
            <input type="checkbox" checked={schedule !== null}
              onChange={(e) => { setSchedule(e.target.checked ? (def.schedule ?? defaultSchedule()) : null); setDirty(true) }} />
            Runs on a schedule
          </label>
          <button className="px-3 py-1.5 rounded text-sm bg-indigo-600 hover:bg-indigo-500 text-white disabled:opacity-50"
            disabled={!dirty || saving || !!preview?.error} onClick={save}>Save schedule</button>
        </div>
      </div>
      {error && <p className="text-red-400 text-sm mb-2">{error}</p>}
      {!schedule ? <p className="text-sm text-gray-500">Files are made only when someone presses Run now or Generate test file.</p> : (
        <>
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-3">
            <div>
              <label className={label}>Runs</label>
              <select className={input} value={schedule.frequency} onChange={(e) => set({ frequency: e.target.value as ExportSchedule['frequency'] })}>
                <option value="daily">Daily</option>
                <option value="monthly">Monthly</option>
              </select>
            </div>
            {schedule.frequency === 'monthly' && (
              <div>
                <label className={label}>On day</label>
                <input type="number" min={1} max={28} className={input} value={schedule.dayOfMonth} onChange={(e) => set({ dayOfMonth: Number(e.target.value) })} />
              </div>
            )}
            <div>
              <label className={label}>At</label>
              <input type="time" className={input} value={schedule.timeOfDay} onChange={(e) => set({ timeOfDay: e.target.value })} />
            </div>
            <div>
              <label className={label}>Run-time zone</label>
              <select className={input} value={schedule.timeZone} onChange={(e) => set({ timeZone: e.target.value })}>
                {!ZONES.includes(schedule.timeZone) && <option value={schedule.timeZone}>{schedule.timeZone}</option>}
                {ZONES.map((z) => <option key={z} value={z}>{z}</option>)}
              </select>
            </div>
          </div>
          {schedule.frequency === 'daily' && (
            <div className="flex flex-wrap items-center gap-3 mb-3 text-sm text-gray-300">
              <span className="text-xs text-gray-400">Only on</span>
              {DAYS.map((d, i) => (
                <label key={d} className="flex items-center gap-1">
                  <input type="checkbox" checked={schedule.daysOfWeek.includes(i)}
                    onChange={(e) => set({ daysOfWeek: e.target.checked ? [...schedule.daysOfWeek, i] : schedule.daysOfWeek.filter((x) => x !== i) })} />
                  {d}
                </label>
              ))}
              <span className="text-xs text-gray-500">(none checked = every day)</span>
            </div>
          )}
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-3">
            <div className="sm:col-span-2">
              <label className={label}>Each file covers</label>
              <select className={input} value={schedule.window} onChange={(e) => set({ window: e.target.value as ExportSchedule['window'] })}>
                <option value="previous_day">The previous day</option>
                <option value="previous_week">The previous week (Mon–Sun)</option>
                <option value="previous_month">The previous month</option>
                <option value="last_hours">The last N hours</option>
                <option value="since_last_run">Everything since the last file</option>
              </select>
            </div>
            {schedule.window === 'last_hours' && (
              <div>
                <label className={label}>Hours</label>
                <input type="number" min={1} max={744} className={input} value={schedule.lastHours} onChange={(e) => set({ lastHours: Number(e.target.value) })} />
              </div>
            )}
            <label className="flex items-end gap-2 text-sm text-gray-300 pb-1.5">
              <input type="checkbox" checked={schedule.autoDeliver} onChange={(e) => set({ autoDeliver: e.target.checked })} />
              Send each file to the delivery targets
            </label>
          </div>
          <p className="text-xs text-gray-500 mb-2">Days are whole days in the export's time zone ({def.spec.timeZone}).</p>
          {preview?.error && <p className="text-red-400 text-sm">{preview.error}</p>}
          {preview?.description && (
            <div className="text-sm text-gray-200">
              <p className="mb-1">{preview.description}</p>
              <ul className="text-xs text-gray-400 space-y-0.5">
                {preview.nextRuns.map((r) => (
                  <li key={r.runAt}>{fmt(r.runAt, schedule.timeZone)} — covers {days(r, def.spec.timeZone)}</li>
                ))}
              </ul>
              {def.status !== 'live' && <p className="text-xs text-amber-300 mt-2">The schedule only runs once the export is live.</p>}
            </div>
          )}
        </>
      )}
    </div>
  )
}
