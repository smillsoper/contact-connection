import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import PortalShell from '../../components/portal/PortalShell'
import { usePortalAuthStore } from '../../stores/portalAuthStore'
import { PORT_STATUS, fmtNumber } from '../admin/AdminPortingPage'
import { CopyIcon, CheckIcon } from '../../components/icons/Icons'

/**
 * The porting desk (S184, Owner + Support). Signed orders arrive here (and in the platform-porting mailbox) with SignalWire's
 * port-in form laid out field by field — copy each into SignalWire's form, upload the LOA + bill, then record the order
 * number, the confirmed port date, and completion. Anything wrong goes back to the signer with a message.
 */

interface Row {
  id: string; reference: string; tenantName: string | null; kind: string; numberCount: number; numbers: string[]; status: string
  signerEmail: string; updatedAt: string; signatureDaysLeft: number | null; signalWireOrderNumber: string | null; focDate: string | null
}
interface Detail {
  order: Row & { endUserName: string; requestedByName: string }
  events: { at: string; by: string; text: string }[]
  correctionMessage: string | null
  files: { loa: boolean; certificate: boolean; bill: boolean; billName: string | null }
  form: { label: string; value: string }[] | null
  sensitivePurgedAt: string | null
}

async function portal<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = usePortalAuthStore.getState().token
  const r = await fetch(path, { ...init, headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}`, ...(init.headers ?? {}) } })
  if (!r.ok) {
    const b = await r.text()
    let m = b || r.statusText
    try { m = JSON.parse(b).error ?? m } catch { /* text */ }
    throw new Error(m)
  }
  return (r.status === 204 ? undefined : r.headers.get('content-type')?.includes('json') ? r.json() : r.blob()) as Promise<T>
}

const fmtDate = (s: string) => new Date(s).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })

export default function PortalPortingPage() {
  const { id } = useParams<{ id: string }>()
  return <PortalShell>{id ? <PortDetail id={id} /> : <Queue />}</PortalShell>
}

function Queue() {
  const navigate = useNavigate()
  const [rows, setRows] = useState<Row[] | null>(null)
  const [filter, setFilter] = useState('open')
  const [error, setError] = useState<string | null>(null)
  useEffect(() => { portal<Row[]>(`/api/v1/portal/porting?status=${filter === 'all' ? '' : filter}`).then(setRows).catch((e: Error) => setError(e.message)) }, [filter])

  return (
    <div className="p-6 max-w-6xl">
      <div className="flex items-center justify-between mb-4">
        <div>
          <h1 className="text-white text-xl font-semibold">Porting</h1>
          <p className="text-gray-500 text-sm">Port-in orders from every tenant. Signed orders are ready to enter in SignalWire.</p>
        </div>
        <select value={filter} onChange={(e) => setFilter(e.target.value)} className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-1.5 text-sm text-white">
          <option value="open">Open</option>
          <option value="ready_to_submit">Ready to submit</option>
          <option value="all">All</option>
        </select>
      </div>
      {error && <p className="text-red-400 text-sm mb-3">{error}</p>}
      {!rows && <p className="text-gray-400 text-sm">Loading…</p>}
      {rows?.length === 0 && <p className="text-gray-500 text-sm">Nothing here.</p>}
      {rows && rows.length > 0 && (
        <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-800 text-gray-400 text-left">
                <th className="px-4 py-3 font-medium">Port</th><th className="px-4 py-3 font-medium">Tenant</th>
                <th className="px-4 py-3 font-medium">Numbers</th><th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3 font-medium">Updated</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id} onClick={() => navigate(`/portal/porting/${r.id}`)} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/40 cursor-pointer">
                  <td className="px-4 py-3 text-white font-medium">{r.reference}</td>
                  <td className="px-4 py-3 text-gray-300">{r.tenantName}</td>
                  <td className="px-4 py-3 text-gray-300">{r.numberCount} {r.kind === 'toll_free' ? 'toll-free' : 'local'}</td>
                  <td className="px-4 py-3">
                    <span className={`text-xs border rounded px-2 py-0.5 ${PORT_STATUS[r.status]?.cls}`}>{PORT_STATUS[r.status]?.label ?? r.status}</span>
                    {r.status === 'ready_to_submit' && r.signatureDaysLeft != null && r.signatureDaysLeft <= 5 &&
                      <span className="text-xs text-amber-300 ml-2">signature expires in {r.signatureDaysLeft} d</span>}
                  </td>
                  <td className="px-4 py-3 text-gray-500">{fmtDate(r.updatedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function Copy({ value }: { value: string }) {
  const [done, setDone] = useState(false)
  return (
    <button title="Copy" onClick={() => { void navigator.clipboard.writeText(value); setDone(true); setTimeout(() => setDone(false), 1200) }}
      className="text-gray-500 hover:text-white shrink-0">{done ? <CheckIcon size={14} className="text-emerald-400" /> : <CopyIcon size={14} />}</button>
  )
}

function PortDetail({ id }: { id: string }) {
  const navigate = useNavigate()
  const [d, setD] = useState<Detail | null>(null)
  const [msg, setMsg] = useState<string | null>(null)
  const [orderNo, setOrderNo] = useState('')
  const [foc, setFoc] = useState('')
  const [correction, setCorrection] = useState('')
  const [note, setNote] = useState('')
  const [cancelReason, setCancelReason] = useState<string | null>(null)
  const load = () => portal<Detail>(`/api/v1/portal/porting/${id}`).then((x) => { setD(x); setOrderNo(x.order.signalWireOrderNumber ?? ''); setFoc(x.order.focDate ?? '') }).catch((e: Error) => setMsg(e.message))
  useEffect(() => { load() }, [id]) // eslint-disable-line react-hooks/exhaustive-deps

  async function act(path: string, body: unknown, done: string) {
    try { await portal(`/api/v1/portal/porting/${id}/${path}`, { method: 'POST', body: JSON.stringify(body) }); setMsg(done); load() }
    catch (e) { setMsg(e instanceof Error ? e.message : 'Failed.') }
  }
  async function file(kind: string, name: string) {
    try {
      const blob = await portal<Blob>(`/api/v1/portal/porting/${id}/files/${kind}`)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a'); a.href = url; a.download = name; document.body.appendChild(a); a.click(); a.remove()
      setTimeout(() => URL.revokeObjectURL(url), 10_000)
    } catch (e) { setMsg(e instanceof Error ? e.message : 'Download failed.') }
  }

  if (!d) return <div className="p-6 text-gray-400 text-sm">{msg ?? 'Loading…'}</div>
  const o = d.order
  const s = o.status
  const btn = 'bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded-lg px-3 py-1.5 text-xs font-medium shrink-0'
  const field = 'bg-gray-800 border border-gray-700 rounded-lg px-3 py-1.5 text-sm text-white focus:outline-none focus:border-indigo-500'
  return (
    <div className="p-6 max-w-5xl">
      <button onClick={() => navigate('/portal/porting')} className="text-gray-400 hover:text-white text-sm mb-3">← Porting</button>
      <div className="flex flex-wrap items-center gap-3 mb-1">
        <h1 className="text-white text-xl font-semibold">{o.reference}</h1>
        <span className={`text-xs border rounded px-2 py-0.5 ${PORT_STATUS[s]?.cls}`}>{PORT_STATUS[s]?.label ?? s}</span>
        {o.signatureDaysLeft != null && ['ready_to_submit', 'submitted'].includes(s) &&
          <span className={`text-xs ${o.signatureDaysLeft <= 5 ? 'text-amber-300' : 'text-gray-500'}`}>signature valid {o.signatureDaysLeft} more day(s)</span>}
      </div>
      <p className="text-gray-500 text-sm mb-5">{o.tenantName} · {o.numberCount} {o.kind === 'toll_free' ? 'toll-free' : 'local'} number(s) · requested by {o.requestedByName}</p>
      {msg && <p className="mb-4 text-sm text-gray-200 bg-gray-800 rounded-lg px-3 py-2">{msg}</p>}

      <div className="grid lg:grid-cols-5 gap-5">
        <div className="lg:col-span-3 space-y-5">
          <section className="bg-gray-900 rounded-xl border border-gray-800 p-4">
            <h2 className="text-white text-sm font-semibold mb-1">SignalWire port-in form</h2>
            <p className="text-gray-500 text-xs mb-3">In the order SignalWire's form asks for them. Then upload the {o.kind === 'toll_free' ? 'toll-free' : 'local'} LOA and the bill below.</p>
            {!d.form && <p className="text-gray-500 text-sm">Not signed yet — the form fills in once the account owner signs.</p>}
            {d.form && (
              <div className="divide-y divide-gray-800">
                {d.form.map((f) => (
                  <div key={f.label} className="flex items-start gap-3 py-2">
                    <span className="text-gray-400 text-xs w-48 shrink-0 pt-0.5">{f.label}</span>
                    <span className={`text-sm flex-1 whitespace-pre-wrap break-all ${f.value ? 'text-white' : 'text-red-400'}`}>
                      {f.value || (f.label === 'Project ID' ? 'Not set — add it on Manage Tenant' : '—')}
                    </span>
                    {f.value && <Copy value={f.value} />}
                  </div>
                ))}
              </div>
            )}
            <div className="flex flex-wrap gap-2 mt-3">
              {d.files.loa && <button onClick={() => void file('loa', `${o.reference} LOA.pdf`)} className="bg-gray-800 hover:bg-gray-700 text-gray-200 text-xs rounded-lg px-3 py-1.5">Signed LOA</button>}
              {d.files.bill && <button onClick={() => void file('bill', `${o.reference} bill - ${d.files.billName}`)} className="bg-gray-800 hover:bg-gray-700 text-gray-200 text-xs rounded-lg px-3 py-1.5">Bill copy</button>}
              {d.files.certificate && <button onClick={() => void file('certificate', `${o.reference} signature record.pdf`)} className="bg-gray-800 hover:bg-gray-700 text-gray-200 text-xs rounded-lg px-3 py-1.5">Signature record</button>}
              <button onClick={() => void file('numbers', `${o.reference} numbers.csv`)} className="bg-gray-800 hover:bg-gray-700 text-gray-200 text-xs rounded-lg px-3 py-1.5">Numbers (CSV)</button>
            </div>
            {d.sensitivePurgedAt && <p className="text-gray-500 text-xs mt-2">PIN and bill copy deleted {fmtDate(d.sensitivePurgedAt)}.</p>}
          </section>

          <section className="bg-gray-900 rounded-xl border border-gray-800 p-4">
            <h2 className="text-white text-sm font-semibold mb-2">Timeline</h2>
            <ol className="space-y-2 text-sm">
              {d.events.slice().reverse().map((e, i) => (
                <li key={i} className="flex gap-3"><span className="text-gray-500 text-xs w-28 shrink-0">{fmtDate(e.at)}</span>
                  <span className="text-gray-200">{e.text}<span className="text-gray-500"> — {e.by}</span></span></li>
              ))}
            </ol>
          </section>
        </div>

        <div className="lg:col-span-2 space-y-4">
          {['ready_to_submit', 'submitted'].includes(s) && (
            <section className="bg-gray-900 rounded-xl border border-gray-800 p-4 space-y-2">
              <h3 className="text-white text-sm font-semibold">1. Submitted to SignalWire</h3>
              <div className="flex gap-2">
                <input value={orderNo} onChange={(e) => setOrderNo(e.target.value)} placeholder="SignalWire order #" className={`${field} flex-1`} />
                <button disabled={!orderNo.trim()} onClick={() => void act('submitted', { orderNumber: orderNo }, 'Recorded as submitted.')} className={btn}>Save</button>
              </div>
            </section>
          )}
          {['submitted', 'foc_confirmed'].includes(s) && (
            <section className="bg-gray-900 rounded-xl border border-gray-800 p-4 space-y-2">
              <h3 className="text-white text-sm font-semibold">2. Port date confirmed (FOC)</h3>
              <div className="flex gap-2">
                <input type="date" value={foc} onChange={(e) => setFoc(e.target.value)} className={`${field} flex-1`} />
                <button disabled={!foc} onClick={() => void act('foc', { date: foc }, 'Port date recorded — the tenant has been told.')} className={btn}>Save</button>
              </div>
            </section>
          )}
          {s === 'foc_confirmed' && (
            <section className="bg-gray-900 rounded-xl border border-gray-800 p-4 space-y-2">
              <h3 className="text-white text-sm font-semibold">3. Port complete</h3>
              <p className="text-gray-500 text-xs">When SignalWire shows the numbers active in the tenant's project.</p>
              <button onClick={() => void act('complete', {}, 'Completed.')} className={btn}>Mark completed</button>
            </section>
          )}
          {['ready_to_submit', 'submitted', 'foc_confirmed', 'needs_correction'].includes(s) && (
            <section className="bg-gray-900 rounded-xl border border-gray-800 p-4 space-y-2">
              <h3 className="text-white text-sm font-semibold">Send back for correction</h3>
              <p className="text-gray-500 text-xs">The signer gets a new link with your message; their answers stay filled in.</p>
              <textarea value={correction} onChange={(e) => setCorrection(e.target.value)} rows={3} placeholder="e.g. The account number doesn't match the bill." className={`${field} w-full`} />
              <button disabled={!correction.trim()} onClick={() => void act('correction', { message: correction }, 'Sent back to the signer.').then(() => setCorrection(''))} className={btn}>Send back</button>
            </section>
          )}
          <section className="bg-gray-900 rounded-xl border border-gray-800 p-4 space-y-2">
            <h3 className="text-white text-sm font-semibold">Note</h3>
            <textarea value={note} onChange={(e) => setNote(e.target.value)} rows={2} className={`${field} w-full`} placeholder="Shown on the timeline (the tenant sees it too)." />
            <button disabled={!note.trim()} onClick={() => void act('note', { message: note }, 'Note added.').then(() => setNote(''))} className={btn}>Add note</button>
          </section>
          {!['completed', 'cancelled'].includes(s) && (cancelReason === null
            ? <button onClick={() => setCancelReason('')} className="text-xs text-gray-500 hover:text-red-400">Cancel this port</button>
            : (
              <section className="bg-gray-900 rounded-xl border border-red-900/50 p-4 space-y-2">
                <input value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} placeholder="Reason (the tenant sees it)" className={`${field} w-full`} />
                <div className="flex gap-3 text-xs">
                  <button onClick={() => void act('cancel', { reason: cancelReason }, 'Cancelled.')} className="text-red-400 hover:text-red-300 font-medium">Cancel the port</button>
                  <button onClick={() => setCancelReason(null)} className="text-gray-400">Keep it</button>
                </div>
              </section>
            ))}
          <p className="text-gray-600 text-xs">Numbers: {o.numbers.map(fmtNumber).join(', ')}</p>
        </div>
      </div>
    </div>
  )
}
