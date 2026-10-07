import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import {
  defaultSpec, exportsApi, STATUS_LABEL, STATUS_STYLE,
  type DataSource, type ExportColumn, type ExportDefinition, type ExportRunRow, type ExportSpec,
  type LifecycleAction, type PreviewResult, type VersionRow, type ActivityRow, type ExportSchedule, type StarterTemplate,
} from '../../api/exports'
import ExportScheduleCard from '../../components/admin/exports/ExportScheduleCard'
import ExportCardDataCard from '../../components/admin/exports/ExportCardDataCard'
import ExportDeliveryCard from '../../components/admin/exports/ExportDeliveryCard'
import { listCampaigns, listClients, type Campaign, type Client } from '../../api/telephony'
import { mediaApi, type MediaAgency } from '../../api/media'
import { when } from './AdminExportsPage'

// Export editor (S180, Export Worker): what's in the file (calls, grain, condition), the layout (Columns: one Liquid
// expression per column → CSV / fixed width / Excel; Document: one Liquid template over every call), the file name,
// Preview against real calls, test files + Run now, the vendor lifecycle, and history.

const input = 'w-full bg-gray-900 border border-gray-700 rounded px-2 py-1.5 text-sm text-gray-100 focus:outline-none focus:border-indigo-500'
const label = 'block text-xs text-gray-400 mb-1'
const card = 'bg-gray-800/60 border border-gray-700 rounded-lg p-4 mb-4'
const btn = 'px-3 py-1.5 rounded text-sm disabled:opacity-50'

function isoDay(offsetDays: number) {
  const d = new Date()
  d.setDate(d.getDate() + offsetDays)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function size(n: number | null) {
  if (n == null) return ''
  return n < 1024 ? `${n} B` : n < 1048576 ? `${(n / 1024).toFixed(1)} KB` : `${(n / 1048576).toFixed(1)} MB`
}

const ZONES = ['America/New_York', 'America/Chicago', 'America/Denver', 'America/Phoenix', 'America/Los_Angeles',
  'America/Anchorage', 'Pacific/Honolulu', 'UTC']

function FieldReference() {
  return (
    <details className={card}>
      <summary className="cursor-pointer text-sm text-gray-200 font-medium">Field reference &amp; filters</summary>
      <div className="mt-3 text-xs text-gray-300 space-y-2 leading-relaxed">
        <p><b className="text-gray-100">call</b>: id, started_at, ended_at, handle_time_seconds, source, status, ani, area_code, dnis,
          first_name, last_name, email, phone, billing_phone, shipping_phone, account_number, client_number,
          contact_id_external, client.name, campaign.name, agent.name, billing_address.(firstName, lastName, address1,
          address2, city, state, zip…), shipping_address.…, media.(agency, station, market_type, media_type, ad_type,
          phone_number, fields.NAME), custom_fields.NAME, disposition, has_order, order_count, order_number,
          order_total, order_tax, interactions[]</p>
        <p><b className="text-gray-100">interaction</b> (rows per interaction / cart line, and each of call.interactions): number,
          type, status, disposition, started_at, agent.name, campaign.name, order_number, order_submitted_at, has_order,
          total, tax, tier_label, cart.(items[], subtotal, shipping, sales_tax, discount, total, ship_method),
          payment.(card_type, card_last4, auth_code, amount)</p>
        <p><b className="text-gray-100">line</b> (rows per cart line): sku, description, quantity, unit_price, extended_price, sales_tax,
          shipping, is_upsell, auto_ship · <b className="text-gray-100">line_number</b> · <b className="text-gray-100">row_number</b></p>
        <p><b className="text-gray-100">export</b>: name, is_test, run_id, data_source, time_zone, generated_at ·
          <b className="text-gray-100"> window</b>: start, end, end_inclusive · Document mode: <b className="text-gray-100">calls</b>, call_count</p>
        <p><b className="text-gray-100">Filters</b>: <code>format_time: 'MM/dd/yyyy'</code> (in the export's time zone) ·
          <code> pad_left: 6, '0'</code> / <code>pad_right: 12</code> (exact width) · <code>digits</code> ·
          <code> number: '000000'</code> · <code>money</code> · <code>csv</code> · <code>xml</code> · plus standard Liquid
          (<code>default</code>, <code>upcase</code>, <code>slice</code>, <code>minus</code>, <code>times</code>…).</p>
        <p>Times are stored in UTC and written in the export's time zone by <code>format_time</code>. Card numbers are never available.</p>
      </div>
    </details>
  )
}

function ColumnsEditor({ spec, set }: { spec: ExportSpec; set: (s: ExportSpec) => void }) {
  const cols = spec.columns
  const update = (i: number, patch: Partial<ExportColumn>) => set({ ...spec, columns: cols.map((c, j) => j === i ? { ...c, ...patch } : c) })
  const move = (i: number, d: number) => {
    const next = [...cols]
    const [c] = next.splice(i, 1)
    next.splice(i + d, 0, c)
    set({ ...spec, columns: next })
  }
  const fixed = spec.format === 'fixed'
  const lineWidth = fixed ? cols.reduce((n, c) => n + (c.width ?? 0), 0) : 0
  return (
    <div>
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-3">
        <div>
          <label className={label}>Format</label>
          <select className={input} value={spec.format} onChange={(e) => set({ ...spec, format: e.target.value as ExportSpec['format'] })}>
            <option value="delimited">Delimited (CSV…)</option>
            <option value="fixed">Fixed width</option>
            <option value="xlsx">Excel (.xlsx)</option>
          </select>
        </div>
        {spec.format === 'delimited' && (
          <div>
            <label className={label}>Delimiter</label>
            <select className={input} value={spec.delimiter} onChange={(e) => set({ ...spec, delimiter: e.target.value })}>
              <option value=",">Comma</option>
              <option value={'\t'}>Tab</option>
              <option value="|">Pipe</option>
              <option value=";">Semicolon</option>
            </select>
          </div>
        )}
        {spec.format !== 'xlsx' && (
          <div>
            <label className={label}>Line ending</label>
            <select className={input} value={spec.lineEnding} onChange={(e) => set({ ...spec, lineEnding: e.target.value as 'crlf' | 'lf' })}>
              <option value="crlf">Windows (CRLF)</option>
              <option value="lf">Unix (LF)</option>
            </select>
          </div>
        )}
        <label className="flex items-end gap-2 text-sm text-gray-300 pb-1.5">
          <input type="checkbox" checked={spec.includeHeader} onChange={(e) => set({ ...spec, includeHeader: e.target.checked })} />
          Header row
        </label>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-xs text-gray-500">
              <th className="pb-1 pr-2 font-medium w-40">Header</th>
              <th className="pb-1 pr-2 font-medium">Value (Liquid)</th>
              {fixed && <th className="pb-1 pr-2 font-medium w-16">Width</th>}
              {fixed && <th className="pb-1 pr-2 font-medium w-24">Align</th>}
              {fixed && <th className="pb-1 pr-2 font-medium w-12">Pad</th>}
              {spec.format === 'delimited' && <th className="pb-1 pr-2 font-medium w-28">Quotes</th>}
              {spec.format === 'xlsx' && <th className="pb-1 pr-2 font-medium w-24">Type</th>}
              <th className="w-24" />
            </tr>
          </thead>
          <tbody>
            {cols.map((c, i) => (
              <tr key={i}>
                <td className="pr-2 py-1"><input className={input} value={c.header} onChange={(e) => update(i, { header: e.target.value })} /></td>
                <td className="pr-2 py-1"><input className={`${input} font-mono`} value={c.template} onChange={(e) => update(i, { template: e.target.value })} /></td>
                {fixed && <td className="pr-2 py-1"><input type="number" min={1} className={input} value={c.width ?? ''} onChange={(e) => update(i, { width: e.target.value ? Number(e.target.value) : null })} /></td>}
                {fixed && (
                  <td className="pr-2 py-1">
                    <select className={input} value={c.align ?? 'left'} onChange={(e) => update(i, { align: e.target.value as 'left' | 'right' })}>
                      <option value="left">Left</option><option value="right">Right</option>
                    </select>
                  </td>
                )}
                {fixed && <td className="pr-2 py-1"><input className={input} maxLength={1} placeholder="␠" value={c.padChar ?? ''} onChange={(e) => update(i, { padChar: e.target.value || null })} /></td>}
                {spec.format === 'delimited' && (
                  <td className="pr-2 py-1">
                    <select className={input} value={c.quote ?? 'auto'} onChange={(e) => update(i, { quote: e.target.value as ExportColumn['quote'] })}>
                      <option value="auto">When needed</option><option value="always">Always</option><option value="never">Never</option>
                    </select>
                  </td>
                )}
                {spec.format === 'xlsx' && (
                  <td className="pr-2 py-1">
                    <select className={input} value={c.type ?? 'text'} onChange={(e) => update(i, { type: e.target.value as 'text' | 'number' })}>
                      <option value="text">Text</option><option value="number">Number</option>
                    </select>
                  </td>
                )}
                <td className="py-1 whitespace-nowrap text-right">
                  <button className="px-1.5 text-gray-400 hover:text-white disabled:opacity-30" disabled={i === 0} onClick={() => move(i, -1)} title="Move up">↑</button>
                  <button className="px-1.5 text-gray-400 hover:text-white disabled:opacity-30" disabled={i === cols.length - 1} onClick={() => move(i, 1)} title="Move down">↓</button>
                  <button className="px-1.5 text-red-400 hover:text-red-300" onClick={() => set({ ...spec, columns: cols.filter((_, j) => j !== i) })} title="Remove">✕</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="flex items-center gap-4 mt-2">
        <button className={`${btn} bg-gray-700 hover:bg-gray-600 text-white`} onClick={() => set({ ...spec, columns: [...cols, { header: '', template: '' }] })}>Add column</button>
        {fixed && <span className="text-xs text-gray-400">Record width: {lineWidth} characters</span>}
      </div>
    </div>
  )
}

function ApproveDialog({ runs, onClose, onApprove }: {
  runs: ExportRunRow[]; onClose: () => void; onApprove: (vendorContact: string, note: string, runId: string) => Promise<void>
}) {
  const tests = runs.filter((r) => r.isTest && r.status === 'succeeded')
  const [contact, setContact] = useState('')
  const [note, setNote] = useState('')
  const [runId, setRunId] = useState(tests[0]?.id ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-4">
      <div className="bg-gray-800 border border-gray-700 rounded-lg p-5 w-full max-w-lg">
        <h2 className="text-white font-semibold mb-1">Record vendor approval</h2>
        <p className="text-xs text-gray-400 mb-4">Which test file did the vendor approve, and who approved it?</p>
        {tests.length === 0 ? <p className="text-amber-300 text-sm mb-4">Generate a test file first — there's none to approve yet.</p> : (
          <>
            <label className={label}>Test file</label>
            <select className={`${input} mb-3`} value={runId} onChange={(e) => setRunId(e.target.value)}>
              {tests.map((r) => <option key={r.id} value={r.id}>{r.fileName} — {when(r.finishedAt)} (revision {r.specRevision})</option>)}
            </select>
            <label className={label}>Approved by (at the vendor)</label>
            <input className={`${input} mb-3`} value={contact} onChange={(e) => setContact(e.target.value)} placeholder="e.g. Jane Doe at Cannella" />
            <label className={label}>Note</label>
            <textarea className={`${input} mb-3`} rows={2} value={note} onChange={(e) => setNote(e.target.value)} placeholder="Optional — e.g. approved by email 10/6" />
          </>
        )}
        {error && <p className="text-red-400 text-sm mb-3">{error}</p>}
        <div className="flex justify-end gap-2">
          <button className={`${btn} bg-gray-700 hover:bg-gray-600 text-white`} onClick={onClose}>Cancel</button>
          <button className={`${btn} bg-sky-600 hover:bg-sky-500 text-white`} disabled={busy || !runId || !contact.trim()}
            onClick={async () => {
              setBusy(true); setError(null)
              try { await onApprove(contact, note, runId) } catch (e) { setError(e instanceof Error ? e.message : 'Failed.'); setBusy(false) }
            }}>Record approval</button>
        </div>
      </div>
    </div>
  )
}

function SendDialog({ run, def, onClose, onSent }: {
  run: ExportRunRow; def: ExportDefinition; onClose: () => void; onSent: () => void
}) {
  const targets = def.deliveryTargets.filter((t) => t.enabled)
  const [chosen, setChosen] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-4">
      <div className="bg-gray-800 border border-gray-700 rounded-lg p-5 w-full max-w-md">
        <h2 className="text-white font-semibold mb-1">Send {run.fileName}</h2>
        <p className="text-xs text-gray-400 mb-4">{run.isTest ? 'A test file — e.g. to the vendor for approval.' : 'This file again, to the targets you pick.'}</p>
        {targets.length === 0 ? <p className="text-amber-300 text-sm mb-4">Add a delivery target first (Delivery, above).</p> : (
          <div className="space-y-1 mb-4">
            {targets.map((t) => (
              <label key={t.id} className="flex items-center gap-2 text-sm text-gray-200">
                <input type="checkbox" checked={chosen.includes(t.id)}
                  onChange={(e) => setChosen(e.target.checked ? [...chosen, t.id] : chosen.filter((x) => x !== t.id))} />
                {t.name} <span className="text-xs text-gray-500">{t.type.toUpperCase()}{t.encryption !== 'none' ? ` · ${t.encryption}` : ''}</span>
              </label>
            ))}
          </div>
        )}
        {error && <p className="text-red-400 text-sm mb-3">{error}</p>}
        <div className="flex justify-end gap-2">
          <button className={`${btn} bg-gray-700 hover:bg-gray-600 text-white`} onClick={onClose}>Cancel</button>
          <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white`} disabled={busy || chosen.length === 0}
            onClick={async () => {
              setBusy(true); setError(null)
              try { await exportsApi.send(run.id, chosen); onSent() } catch (e) { setError(e instanceof Error ? e.message : 'Failed.'); setBusy(false) }
            }}>Send</button>
        </div>
      </div>
    </div>
  )
}

/** "next try in 4:32" — ticks every second until the retry is due. */
function RetryCountdown({ at, attempt, max }: { at: string; attempt: number; max: number }) {
  const [now, setNow] = useState(Date.now())
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [])
  const left = Math.max(0, Math.round((new Date(at).getTime() - now) / 1000))
  const text = left === 0 ? 'trying now…' : `next try in ${Math.floor(left / 60)}:${String(left % 60).padStart(2, '0')}`
  return <span className="text-gray-400"> — {text} (attempt {attempt} of {max})</span>
}

const DELIVERY_STYLE: Record<string, string> = {
  succeeded: 'text-emerald-300', failed: 'text-red-400', queued: 'text-amber-300', running: 'text-amber-300',
}

export default function AdminExportEditorPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const isNew = !id
  const [def, setDef] = useState<ExportDefinition | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [spec, setSpec] = useState<ExportSpec>(defaultSpec(''))
  const [dirty, setDirty] = useState(false)
  const [clients, setClients] = useState<Client[]>([])
  const [campaigns, setCampaigns] = useState<Campaign[]>([])
  const [agencies, setAgencies] = useState<MediaAgency[]>([])
  const [runs, setRuns] = useState<ExportRunRow[]>([])
  const [versions, setVersions] = useState<VersionRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [from, setFrom] = useState(isoDay(-1))
  const [to, setTo] = useState(isoDay(-1))
  const [source, setSource] = useState<DataSource>('production')
  const [preview, setPreview] = useState<PreviewResult | null>(null)
  const [previewing, setPreviewing] = useState(false)
  const [approving, setApproving] = useState(false)
  const [sending, setSending] = useState<ExportRunRow | null>(null)
  const [activity, setActivity] = useState<ActivityRow[]>([])
  const [templates, setTemplates] = useState<StarterTemplate[]>([])
  const [pendingSchedule, setPendingSchedule] = useState<ExportSchedule | null>(null)

  const setS = (s: ExportSpec) => { setSpec(s); setDirty(true) }

  const load = (d: ExportDefinition) => {
    setDef(d); setName(d.name); setDescription(d.description ?? ''); setSpec(d.spec); setDirty(false)
  }
  const refreshRuns = () => id ? exportsApi.runs(id).then(setRuns).catch(() => { }) : Promise.resolve()
  const refreshVersions = () => id ? exportsApi.versions(id).then(setVersions).catch(() => { }) : Promise.resolve()
  const refreshActivity = () => id ? exportsApi.activity(id).then(setActivity).catch(() => { }) : Promise.resolve()

  useEffect(() => {
    listClients().then(setClients).catch(() => { })
    listCampaigns().then(setCampaigns).catch(() => { })
    mediaApi.agencies().then(setAgencies).catch(() => { })
    exportsApi.templates().then(setTemplates).catch(() => { })
  }, [])

  useEffect(() => {
    if (!id) return
    exportsApi.get(id).then(load).catch((e: Error) => setError(e.message))
    refreshRuns(); refreshVersions(); refreshActivity()
  }, [id]) // eslint-disable-line react-hooks/exhaustive-deps

  // Follow files being generated / sent until they finish: quickly while something is happening or due within a minute,
  // slowly while a delivery waits out a retry backoff, not at all once nothing is pending.
  const soon = runs.some((r) => r.status === 'queued' || r.status === 'running'
    || r.deliveries.some((d) => d.status === 'running' || (d.status === 'queued' && new Date(d.nextAttemptAt).getTime() < Date.now() + 60_000)))
  const waiting = runs.some((r) => r.deliveries.some((d) => d.status === 'queued'))
  const pollEvery = soon ? 2500 : waiting ? 20_000 : 0
  useEffect(() => {
    if (!pollEvery) { refreshActivity(); return }
    const t = setInterval(refreshRuns, pollEvery)
    return () => clearInterval(t)
  }, [pollEvery]) // eslint-disable-line react-hooks/exhaustive-deps

  const campaignOptions = useMemo(
    () => campaigns.filter((c) => !spec.clientId || c.clientId === spec.clientId).sort((a, b) => a.name.localeCompare(b.name)),
    [campaigns, spec.clientId])

  async function save() {
    setSaving(true); setError(null); setNotice(null)
    try {
      if (isNew) {
        const d = await exportsApi.create(name, description || null, spec)
        if (pendingSchedule) await exportsApi.saveSchedule(d.id, pendingSchedule)
        navigate(`/admin/exports/${d.id}`, { replace: true })
      } else {
        load(await exportsApi.update(id!, name, description || null, spec))
        setNotice('Saved.'); refreshVersions()
      }
    } catch (e) { setError(e instanceof Error ? e.message : 'Save failed.') }
    finally { setSaving(false) }
  }

  async function runPreview() {
    setPreviewing(true); setError(null)
    try { setPreview(await exportsApi.preview({ spec, name: name || 'Preview', from, to, dataSource: source, isTest: source === 'practice', maxCalls: 25 })) }
    catch (e) { setError(e instanceof Error ? e.message : 'Preview failed.') }
    finally { setPreviewing(false) }
  }

  async function queue(isTest: boolean, deliver = false) {
    if (!id) return
    if (dirty) { setError('Save your changes first — files are generated from the saved export.'); return }
    setError(null); setNotice(null)
    try {
      await exportsApi.queueRun(id, { from, to, isTest, dataSource: isTest ? source : 'production', deliver })
      setNotice(isTest ? 'Test file queued.' : deliver ? 'File queued — it will be sent when it is ready.' : 'File queued.')
      refreshRuns()
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not queue the file.') }
  }

  async function lifecycle(action: LifecycleAction, extra?: { vendorContact?: string; note?: string; runId?: string }) {
    setError(null); setNotice(null)
    try { load(await exportsApi.lifecycle(id!, action, extra)); refreshVersions() }
    catch (e) { setError(e instanceof Error ? e.message : 'Failed.'); throw e }
  }

  async function remove() {
    if (!window.confirm('Delete this draft export and its files?')) return
    try { await exportsApi.remove(id!); navigate('/admin/exports') }
    catch (e) { setError(e instanceof Error ? e.message : 'Delete failed.') }
  }

  const status = def?.status ?? 'draft'
  const grid = preview?.grid ? preview.text.split('\n').filter((l) => l.length > 0).map((l) => l.split('\t')) : null

  return (
    <AdminShell>
      <div className="max-w-6xl mx-auto px-4 sm:px-6 py-6">
        <Link to="/admin/exports" className="text-sm text-indigo-400 hover:text-indigo-300">← Data Exports</Link>
        <div className="flex flex-wrap items-center justify-between gap-3 mt-2 mb-4">
          <div className="flex items-center gap-3">
            <h1 className="text-xl font-semibold text-white">{isNew ? 'New export' : def?.name ?? '…'}</h1>
            {def && <span className={`px-2 py-0.5 rounded text-xs ${STATUS_STYLE[status]}`}>{STATUS_LABEL[status]}</span>}
            {dirty && <span className="text-xs text-amber-300">Unsaved changes</span>}
          </div>
          <div className="flex flex-wrap gap-2">
            {def && status === 'draft' && <button className={`${btn} bg-amber-700 hover:bg-amber-600 text-white`} onClick={() => lifecycle('start_testing').catch(() => { })}>Start testing with vendor</button>}
            {def && status === 'testing' && <button className={`${btn} bg-sky-600 hover:bg-sky-500 text-white`} onClick={() => setApproving(true)}>Record vendor approval…</button>}
            {def && (status === 'approved' || status === 'paused') && <button className={`${btn} bg-emerald-600 hover:bg-emerald-500 text-white`} onClick={() => lifecycle('go_live').catch(() => { })}>Go live</button>}
            {def && status === 'live' && <button className={`${btn} bg-gray-700 hover:bg-gray-600 text-amber-300`} onClick={() => lifecycle('pause').catch(() => { })}>Pause</button>}
            {def && status !== 'testing' && status !== 'draft' && <button className={`${btn} bg-gray-700 hover:bg-gray-600 text-white`} onClick={() => lifecycle('start_testing').catch(() => { })}>Re-test</button>}
            {def && ['testing', 'approved', 'paused'].includes(status) && <button className={`${btn} bg-gray-700 hover:bg-gray-600 text-white`} onClick={() => lifecycle('back_to_draft').catch(() => { })}>Back to draft</button>}
            {def && status === 'draft' && <button className={`${btn} bg-gray-700 hover:bg-red-700 text-red-300 hover:text-white`} onClick={remove}>Delete</button>}
            <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white`} disabled={saving || !name.trim()} onClick={save}>{isNew ? 'Create' : 'Save'}</button>
          </div>
        </div>

        {def?.scheduleDescription && (
          <p className="text-sm text-gray-400 mb-3">
            {def.scheduleDescription}{' '}
            {def.status === 'live' && def.nextRuns[0]
              ? <span className="text-emerald-300">Next run {when(def.nextRuns[0].runAt)}.</span>
              : <span className="text-amber-300">Not running — the export isn't live.</span>}
          </p>
        )}
        {error && <p className="text-red-400 text-sm mb-3 whitespace-pre-wrap">{error}</p>}
        {notice && <p className="text-emerald-400 text-sm mb-3">{notice}</p>}

        {def?.approval && (
          <div className={`${card} ${def.changedSinceApproval ? 'border-amber-600' : ''}`}>
            <p className="text-sm text-gray-200">
              Vendor approved by <b>{def.approval.vendorContact}</b> on {when(def.approval.at)} (recorded by {def.approval.recordedBy})
              {def.approval.note ? ` — ${def.approval.note}` : ''}
            </p>
            {def.changedSinceApproval && (
              <p className="text-sm text-amber-300 mt-1">
                The layout or data has changed since then (approved revision {def.approval.specRevision}, now {def.specRevision}).
                Consider sending the vendor a new test file.
              </p>
            )}
          </div>
        )}

        {isNew && templates.length > 0 && (
          <div className={card}>
            <label className={label}>Start from</label>
            <select className={input} defaultValue="" onChange={(e) => {
              const t = templates.find((x) => x.key === e.target.value)
              if (!t) { setSpec(defaultSpec('')); setPendingSchedule(null); return }
              setSpec(t.spec); setPendingSchedule(t.schedule); setDirty(true)
              if (!name.trim()) setName(t.name.replace(/ \(.*\)$/, ''))
              setNotice(t.notes)
            }}>
              <option value="">Blank export</option>
              {templates.map((t) => <option key={t.key} value={t.key}>{t.name} — {t.description}</option>)}
            </select>
            {pendingSchedule && <p className="text-xs text-gray-400 mt-2">Comes with a schedule (nightly) — it only runs once the export is live.</p>}
          </div>
        )}

        <div className={card}>
          <div className="grid sm:grid-cols-2 gap-3">
            <div>
              <label className={label}>Name</label>
              <input className={input} value={name} onChange={(e) => { setName(e.target.value); setDirty(true) }} placeholder="e.g. Cannella SF nightly" />
            </div>
            <div>
              <label className={label}>Description</label>
              <input className={input} value={description} onChange={(e) => { setDescription(e.target.value); setDirty(true) }} />
            </div>
          </div>
        </div>

        <div className={card}>
          <h2 className="text-sm font-semibold text-gray-100 mb-3">Which calls</h2>
          <p className="text-xs text-gray-400 mb-3">Production calls only — training and sandbox calls can go into a test file, never a real one.</p>
          <div className="grid sm:grid-cols-3 gap-3 mb-3">
            <div>
              <label className={label}>Client</label>
              <select className={input} value={spec.clientId ?? ''} onChange={(e) => setS({ ...spec, clientId: e.target.value || null, campaignIds: [] })}>
                <option value="">Any client</option>
                {clients.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            </div>
            <div>
              <label className={label}>Media agency</label>
              <select className={input} value={spec.mediaAgency ?? ''} onChange={(e) => setS({ ...spec, mediaAgency: e.target.value || null })}>
                <option value="">Any (or none)</option>
                {agencies.map((a) => <option key={a.id} value={a.name}>{a.name}</option>)}
              </select>
            </div>
            <div>
              <label className={label}>One row per</label>
              <select className={input} value={spec.rowGrain} onChange={(e) => setS({ ...spec, rowGrain: e.target.value as ExportSpec['rowGrain'] })}>
                <option value="call">Call</option>
                <option value="interaction">Interaction (each campaign on a call)</option>
                <option value="cart_line">Cart line</option>
              </select>
            </div>
          </div>
          <label className={label}>Campaigns {spec.campaignIds.length === 0 && <span className="text-gray-500">(none checked = all)</span>}</label>
          <div className="flex flex-wrap gap-x-4 gap-y-1 mb-3 max-h-32 overflow-y-auto">
            {campaignOptions.map((c) => (
              <label key={c.id} className="flex items-center gap-1.5 text-sm text-gray-300">
                <input type="checkbox" checked={spec.campaignIds.includes(c.id)}
                  onChange={(e) => setS({ ...spec, campaignIds: e.target.checked ? [...spec.campaignIds, c.id] : spec.campaignIds.filter((x) => x !== c.id) })} />
                {c.name}
              </label>
            ))}
          </div>
          <label className={label}>Only include rows where (Liquid condition, optional)</label>
          <input className={`${input} font-mono`} value={spec.condition ?? ''} placeholder={'e.g. call.disposition != "Junk"'}
            onChange={(e) => setS({ ...spec, condition: e.target.value || null })} />
        </div>

        <div className={card}>
          <div className="flex flex-wrap items-center justify-between gap-3 mb-3">
            <h2 className="text-sm font-semibold text-gray-100">Layout</h2>
            <div className="flex rounded overflow-hidden border border-gray-700 text-sm">
              {(['columns', 'document'] as const).map((m) => (
                <button key={m} className={`px-3 py-1 ${spec.layoutMode === m ? 'bg-indigo-600 text-white' : 'bg-gray-900 text-gray-300 hover:bg-gray-800'}`}
                  onClick={() => setS({ ...spec, layoutMode: m, documentTemplate: m === 'document' && !spec.documentTemplate ? '{% for call in calls %}{{ call.started_at | format_time: \'MM/dd/yyyy HH:mm\' }},{{ call.ani }}\n{% endfor %}' : spec.documentTemplate })}>
                  {m === 'columns' ? 'Columns' : 'Document'}
                </button>
              ))}
            </div>
          </div>
          {spec.layoutMode === 'columns' ? <ColumnsEditor spec={spec} set={setS} /> : (
            <div>
              <p className="text-xs text-gray-400 mb-2">One Liquid template over every call (<code>{'{% for call in calls %}'}</code>) — for rigid vendor
                formats with header / trailer records, several lines per call, or counts and totals.</p>
              <textarea className={`${input} font-mono`} rows={14} value={spec.documentTemplate ?? ''} onChange={(e) => setS({ ...spec, documentTemplate: e.target.value })} />
              <div className="flex flex-wrap gap-4 mt-2">
                <label className="flex items-center gap-2 text-sm text-gray-300">
                  <input type="checkbox" checked={spec.skipBlankLines} onChange={(e) => setS({ ...spec, skipBlankLines: e.target.checked })} />
                  Drop blank lines
                </label>
                <label className="flex items-center gap-2 text-sm text-gray-300">
                  Line ending
                  <select className="bg-gray-900 border border-gray-700 rounded px-2 py-1 text-sm text-gray-100" value={spec.lineEnding}
                    onChange={(e) => setS({ ...spec, lineEnding: e.target.value as 'crlf' | 'lf' })}>
                    <option value="crlf">Windows (CRLF)</option><option value="lf">Unix (LF)</option>
                  </select>
                </label>
              </div>
            </div>
          )}
        </div>

        <div className={card}>
          <h2 className="text-sm font-semibold text-gray-100 mb-3">File</h2>
          <div className="grid sm:grid-cols-3 gap-3">
            <div className="sm:col-span-2">
              <label className={label}>File name (Liquid)</label>
              <input className={`${input} font-mono`} value={spec.fileNameTemplate} onChange={(e) => setS({ ...spec, fileNameTemplate: e.target.value })} />
            </div>
            <div>
              <label className={label}>Test file suffix</label>
              <input className={input} value={spec.testFileSuffix} onChange={(e) => setS({ ...spec, testFileSuffix: e.target.value })} placeholder="blank = same name" />
            </div>
            <div>
              <label className={label}>Time zone (dates in the file and the days you pick)</label>
              <select className={input} value={spec.timeZone} onChange={(e) => setS({ ...spec, timeZone: e.target.value })}>
                {spec.timeZone === '' && <option value="">Workspace time zone</option>}
                {!ZONES.includes(spec.timeZone) && spec.timeZone !== '' && <option value={spec.timeZone}>{spec.timeZone}</option>}
                {ZONES.map((z) => <option key={z} value={z}>{z}</option>)}
              </select>
            </div>
          </div>
        </div>

        <ExportCardDataCard spec={spec} onChange={setS} />

        {def && <ExportScheduleCard def={def} onSaved={(d) => { setDef(d); refreshVersions() }} />}
        {def && <ExportDeliveryCard def={def} onSaved={(d) => { setDef(d); refreshVersions() }} />}

        <FieldReference />

        <div className={card}>
          <h2 className="text-sm font-semibold text-gray-100 mb-3">Preview &amp; files</h2>
          <div className="flex flex-wrap items-end gap-3 mb-3">
            <div>
              <label className={label}>From</label>
              <input type="date" className={input} value={from} onChange={(e) => setFrom(e.target.value)} />
            </div>
            <div>
              <label className={label}>To</label>
              <input type="date" className={input} value={to} onChange={(e) => setTo(e.target.value)} />
            </div>
            <div>
              <label className={label}>Calls (for preview / test file)</label>
              <select className={input} value={source} onChange={(e) => setSource(e.target.value as DataSource)}>
                <option value="production">Production calls</option>
                <option value="practice">Practice calls (training / sandbox)</option>
              </select>
            </div>
            <button className={`${btn} bg-gray-700 hover:bg-gray-600 text-white`} disabled={previewing} onClick={runPreview}>{previewing ? 'Rendering…' : 'Preview'}</button>
            {!isNew && <button className={`${btn} bg-amber-700 hover:bg-amber-600 text-white`} onClick={() => queue(true)}>Generate test file</button>}
            {!isNew && <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white`} disabled={source === 'practice'} title={source === 'practice' ? 'Practice calls only go into test files' : ''} onClick={() => queue(false)}>Run now</button>}
            {!isNew && def && ['approved', 'live', 'paused'].includes(def.status) && def.deliveryTargets.some((t) => t.enabled) && (
              <button className={`${btn} bg-emerald-700 hover:bg-emerald-600 text-white`} disabled={source === 'practice'} onClick={() => queue(false, true)}>Run now &amp; send</button>
            )}
          </div>
          <p className="text-xs text-gray-500 mb-3">Dates are whole days in the export's time zone. A test file is marked as a test (file name suffix,
            <code> export.is_test</code>) and never counts as a real run.</p>

          {preview && (
            <div className="mb-4">
              {preview.success ? (
                <p className="text-xs text-gray-400 mb-1">
                  {preview.fileName} — {preview.rowCount} {preview.rowCount === 1 ? 'line' : 'lines'} from {preview.callCount} {preview.callCount === 1 ? 'call' : 'calls'}
                  {preview.truncated && ' (first calls only)'}
                </p>
              ) : <p className="text-red-400 text-sm mb-1 whitespace-pre-wrap">{preview.error}</p>}
              {grid ? (
                <div className="overflow-x-auto max-h-96 border border-gray-700 rounded">
                  <table className="text-xs">
                    <tbody>
                      {grid.map((row, i) => (
                        <tr key={i} className={i === 0 && spec.includeHeader ? 'bg-gray-800 font-medium text-gray-100' : 'text-gray-300 border-t border-gray-800'}>
                          {row.map((c, j) => <td key={j} className="px-2 py-1 whitespace-nowrap">{c}</td>)}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : preview.text && (
                <pre className="bg-gray-950 border border-gray-700 rounded p-3 text-xs text-gray-200 overflow-x-auto max-h-96 whitespace-pre">{preview.text}</pre>
              )}
            </div>
          )}

          {!isNew && (
            runs.length === 0 ? <p className="text-gray-500 italic text-sm">No files generated yet.</p> : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-left text-xs text-gray-500 border-b border-gray-700">
                      <th className="py-1.5 pr-3 font-medium">File</th>
                      <th className="py-1.5 pr-3 font-medium">Covers</th>
                      <th className="py-1.5 pr-3 font-medium">Rows</th>
                      <th className="py-1.5 pr-3 font-medium">Requested</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {runs.map((r) => (
                      <tr key={r.id} className="border-b border-gray-800 align-top">
                        <td className="py-1.5 pr-3">
                          {r.status === 'succeeded'
                            ? <span className="text-gray-100">{r.fileName}</span>
                            : <span className={r.status === 'failed' ? 'text-red-400' : 'text-amber-300'}>{r.status === 'failed' ? 'Failed' : r.status === 'running' ? 'Generating…' : 'Queued…'}</span>}
                          {r.isTest && <span className="ml-2 px-1.5 rounded bg-amber-900/60 text-amber-300 text-xs">test{r.dataSource === 'practice' ? ' · practice calls' : ''}</span>}
                          {r.kind === 'rerun' && <span className="ml-2 text-xs text-gray-400">re-run</span>}
                          {r.kind === 'scheduled' && <span className="ml-2 text-xs text-gray-400">scheduled</span>}
                          {r.fileDeletedAt && <span className="ml-2 text-xs text-gray-500">file deleted (retention)</span>}
                          {def?.approval?.runId === r.id && <span className="ml-2 px-1.5 rounded bg-sky-900/60 text-sky-300 text-xs">vendor approved</span>}
                          {r.error && <span className="block text-xs text-red-400 whitespace-pre-wrap">{r.error}</span>}
                          {r.status === 'succeeded' && <span className="block text-xs text-gray-500">{size(r.fileSize)} · revision {r.specRevision}</span>}
                          {r.deliveries.map((d) => (
                            <span key={d.id} className="block text-xs mt-0.5">
                              <span className={DELIVERY_STYLE[d.status]}>
                                {d.status === 'succeeded' ? '✓ Sent' : d.status === 'failed' ? '✕ Not sent' : d.attempts > 0 ? '↻ Retrying' : '… Sending'}
                              </span>
                              <span className="text-gray-300"> → {d.targetName}</span>
                              {d.deliveredAt && <span className="text-gray-500"> {when(d.deliveredAt)}</span>}
                              {d.status === 'queued' && d.attempts > 0 && (
                                <RetryCountdown at={d.nextAttemptAt} attempt={d.attempts + 1} max={d.maxAttempts} />
                              )}
                              {d.error && <span className="block text-red-400 whitespace-pre-wrap">{d.error}</span>}
                              {(d.status === 'failed' || (d.status === 'queued' && d.attempts > 0)) && (
                                <button className="text-indigo-400 hover:text-indigo-300 ml-1"
                                  onClick={() => exportsApi.retryDelivery(d.id).then(refreshRuns).catch((e: Error) => setError(e.message))}>
                                  {d.status === 'failed' ? 'Retry' : 'Retry now'}
                                </button>
                              )}
                            </span>
                          ))}
                        </td>
                        <td className="py-1.5 pr-3 text-gray-300 whitespace-nowrap text-xs">{when(r.windowStart)}<br />→ {when(r.windowEnd)}</td>
                        <td className="py-1.5 pr-3 text-gray-300">{r.rowCount ?? '—'}{r.callCount != null && <span className="block text-xs text-gray-500">{r.callCount} calls</span>}</td>
                        <td className="py-1.5 pr-3 text-gray-400 text-xs">{when(r.queuedAt)}<br />{r.requestedByName}</td>
                        <td className="py-1.5 whitespace-nowrap text-right">
                          {r.holdsCardData && r.status === 'succeeded' && (
                            <span className="text-xs text-amber-300 mr-3" title="Stored encrypted; goes only to FTPS targets, PGP-encrypted">
                              Card data · {r.cardDataWipedAt ? 'wiped after delivery' : `held for ${r.cardCallCount ?? 0} call(s)`}
                            </span>
                          )}
                          {r.status === 'succeeded' && !r.fileDeletedAt && !r.holdsCardData && <button className="text-indigo-400 hover:text-indigo-300 text-sm mr-3" onClick={() => exportsApi.download(r).then(refreshActivity).catch((e: Error) => setError(e.message))}>Download</button>}
                          {r.status === 'succeeded' && !r.fileDeletedAt && def && (r.isTest || ['approved', 'live', 'paused'].includes(def.status)) && (
                            <button className="text-indigo-400 hover:text-indigo-300 text-sm mr-3" onClick={() => setSending(r)}>Send…</button>
                          )}
                          {(r.status === 'succeeded' || r.status === 'failed') && (
                            <button className="text-gray-400 hover:text-white text-sm" title="Generate this window again with today's data and the current layout"
                              onClick={() => exportsApi.rerun(r.id).then(refreshRuns).catch((e: Error) => setError(e.message))}>Re-run</button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )
          )}
        </div>

        {activity.length > 0 && (
          <details className={card}>
            <summary className="cursor-pointer text-sm text-gray-200 font-medium">Activity — downloads &amp; sends ({activity.length})</summary>
            <ul className="mt-3 space-y-1 text-sm">
              {activity.map((a) => (
                <li key={a.id} className="text-gray-300">
                  <span className="text-gray-500 text-xs mr-2">{when(a.at)}</span>
                  <span className={a.action === 'delivery_failed' ? 'text-red-400' : a.action === 'delivered' ? 'text-emerald-300' : 'text-gray-200'}>
                    {{ downloaded: 'Downloaded', delivered: 'Sent', delivery_failed: 'Send failed', send_requested: 'Send requested', file_expired: 'File deleted', host_key_pinned: 'Host key pinned' }[a.action] ?? a.action}
                  </span>
                  {a.detail && <span> — {a.detail}</span>}
                  {a.actorName && <span className="text-gray-500"> — {a.actorName}</span>}
                </li>
              ))}
            </ul>
          </details>
        )}

        {versions.length > 0 && (
          <details className={card}>
            <summary className="cursor-pointer text-sm text-gray-200 font-medium">History ({versions.length})</summary>
            <ul className="mt-3 space-y-1 text-sm">
              {versions.map((v) => (
                <li key={v.versionNumber} className="text-gray-300">
                  <span className="text-gray-500 text-xs mr-2">{when(v.createdAt)}</span>
                  {v.changeSummary ?? 'Updated'} <span className="text-gray-500">— {v.createdByName}</span>
                </li>
              ))}
            </ul>
          </details>
        )}
      </div>

      {sending && def && (
        <SendDialog run={sending} def={def} onClose={() => setSending(null)}
          onSent={() => { setSending(null); setNotice('Queued to send.'); refreshRuns(); refreshActivity() }} />
      )}

      {approving && (
        <ApproveDialog runs={runs} onClose={() => setApproving(false)}
          onApprove={async (vendorContact, note, runId) => {
            await lifecycle('approve', { vendorContact, note, runId })
            setApproving(false)
          }} />
      )}
    </AdminShell>
  )
}
