import { useEffect, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import { api } from '../../api/client'
import { useAuthStore } from '../../stores/authStore'
import { getSubdomainFromHostname } from '../../utils/subdomain'
import { CloseIcon } from '../../components/icons/Icons'

/**
 * Number Porting (S184, permission numbers.port): move numbers from another phone company to ContactConnection. Paste the
 * numbers, we check them and split them into orders, and the owner of the current phone account signs SignalWire's letter
 * of authorization through an emailed link. ContactConnection's porting desk submits it and keeps the status here.
 */

interface Order {
  id: string; reference: string; kind: 'local' | 'toll_free'; numberCount: number; numbers: string[]; status: string; signerEmail: string
  requestedByName: string; createdAt: string; updatedAt: string; signedAt: string | null; signatureDaysLeft: number | null
  signalWireOrderNumber: string | null; focDate: string | null; completedAt: string | null; endUserName: string
  label: string; preAssignCampaignId: string | null; preAssignFlowId: string | null; preAssignTelephonyFlowId: string | null
  numbersLoadedAt: string | null
}
interface FlowOpt { id: string; name: string; type: 'crm' | 'telephony'; campaignId: string | null }
interface OrderDetail {
  order: Order; events: { at: string; by: string; text: string }[]; accountType: string; currentProviderHint: string | null
  correctionMessage: string | null; hasLoa: boolean; hasCertificate: boolean
}
interface Scrub { local: string[]; tollFree: string[]; alreadyYours: string[]; unavailable: string[]; inProgress: string[]; invalid: string[] }
interface Group { numbers: string; currentProvider: string; accountType: string; endUserName: string; signerEmail: string; preAssignCampaignId: string; scrub: Scrub | null }

export const PORT_STATUS: Record<string, { label: string; cls: string }> = {
  awaiting_signature: { label: 'Awaiting signature', cls: 'bg-amber-900/40 text-amber-300 border-amber-800' },
  needs_correction: { label: 'Needs correction', cls: 'bg-amber-900/40 text-amber-300 border-amber-800' },
  ready_to_submit: { label: 'Signed — being submitted', cls: 'bg-sky-900/40 text-sky-300 border-sky-800' },
  submitted: { label: 'With the carrier', cls: 'bg-indigo-900/40 text-indigo-300 border-indigo-800' },
  foc_confirmed: { label: 'Port date confirmed', cls: 'bg-emerald-900/40 text-emerald-300 border-emerald-800' },
  completed: { label: 'Completed', cls: 'bg-gray-800 text-gray-300 border-gray-700' },
  cancelled: { label: 'Cancelled', cls: 'bg-gray-800 text-gray-500 border-gray-700' },
}

export const fmtNumber = (e: string) => (e.length === 12 && e.startsWith('+1') ? `(${e.slice(2, 5)}) ${e.slice(5, 8)}-${e.slice(8)}` : e)
const fmtDate = (s: string) => new Date(s).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
const input = 'w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-indigo-500'

/** Downloads with the signed-in user's token. */
async function download(path: string, name: string) {
  const { token, tenantSubdomain } = useAuthStore.getState()
  const r = await fetch(path, { headers: { Authorization: `Bearer ${token}`, 'X-Tenant-Subdomain': getSubdomainFromHostname() ?? tenantSubdomain ?? '' } })
  if (!r.ok) return
  const url = URL.createObjectURL(await r.blob())
  const a = document.createElement('a'); a.href = url; a.download = name; document.body.appendChild(a); a.click(); a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

export default function AdminPortingPage() {
  const [orders, setOrders] = useState<Order[] | null>(null)
  const [creating, setCreating] = useState(false)
  const [open, setOpen] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const load = () => api.get<Order[]>('/api/v1/porting/orders').then(setOrders).catch((e: Error) => setError(e.message))
  useEffect(() => { load() }, [])

  return (
    <AdminShell>
      <div className="p-6 max-w-6xl">
        <div className="flex items-start justify-between gap-4 mb-6">
          <div>
            <h1 className="text-white text-xl font-semibold">Number Porting</h1>
            <p className="text-gray-500 text-sm mt-0.5">
              Move numbers from another phone company. We check them, the owner of the current phone account signs the carrier's
              authorization online, and ContactConnection handles the rest — you'll see each step here.
            </p>
          </div>
          {!creating && (
            <button onClick={() => setCreating(true)} className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium shrink-0">
              New port request
            </button>
          )}
        </div>
        {error && <p className="mb-4 text-sm text-red-400">{error}</p>}
        {creating && <NewRequest onDone={() => { setCreating(false); load() }} onCancel={() => setCreating(false)} />}

        {!orders && <p className="text-gray-400 text-sm">Loading…</p>}
        {orders?.length === 0 && !creating && <p className="text-gray-500 text-sm">No port requests yet.</p>}
        {orders && orders.length > 0 && (
          <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 text-gray-400 text-left">
                  <th className="px-4 py-3 font-medium">Port</th>
                  <th className="px-4 py-3 font-medium">Numbers</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="px-4 py-3 font-medium">Signer</th>
                  <th className="px-4 py-3 font-medium">Updated</th>
                </tr>
              </thead>
              <tbody>
                {orders.map((o) => (
                  <tr key={o.id} onClick={() => setOpen(o.id)} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/40 cursor-pointer">
                    <td className="px-4 py-3 text-white font-medium">{o.reference}</td>
                    <td className="px-4 py-3 text-gray-300">{o.numberCount} {o.kind === 'toll_free' ? 'toll-free' : 'local'}
                      <span className="text-gray-500"> · {o.numbers.slice(0, 2).map(fmtNumber).join(', ')}{o.numberCount > 2 ? '…' : ''}</span></td>
                    <td className="px-4 py-3">
                      <span className={`text-xs border rounded px-2 py-0.5 ${PORT_STATUS[o.status]?.cls}`}>{PORT_STATUS[o.status]?.label ?? o.status}</span>
                      {o.focDate && o.status === 'foc_confirmed' && <span className="text-xs text-emerald-300 ml-2">{o.focDate}</span>}
                    </td>
                    <td className="px-4 py-3 text-gray-400">{o.signerEmail}</td>
                    <td className="px-4 py-3 text-gray-500">{fmtDate(o.updatedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {open && <OrderDrawer id={open} onClose={() => setOpen(null)} onChanged={load} />}
    </AdminShell>
  )
}

// ── New request ──────────────────────────────────────────────────────────────

function NewRequest({ onDone, onCancel }: { onDone: () => void; onCancel: () => void }) {
  const blank = (): Group => ({ numbers: '', currentProvider: '', accountType: 'Business', endUserName: '', signerEmail: '', preAssignCampaignId: '', scrub: null })
  const [groups, setGroups] = useState<Group[]>([blank()])
  const [campaigns, setCampaigns] = useState<{ id: string; name: string }[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => { api.get<{ id: string; name: string }[]>('/api/v1/porting/campaigns').then(setCampaigns).catch(() => {}) }, [])

  const update = (i: number, g: Partial<Group>) => setGroups((cur) => cur.map((x, j) => (j === i ? { ...x, ...g } : x)))

  async function check(i: number) {
    try { update(i, { scrub: await api.post<Scrub>('/api/v1/porting/scrub', { text: groups[i].numbers }) }) }
    catch (e) { setError(e instanceof Error ? e.message : 'Check failed.') }
  }

  /** Keeps only the portable numbers in the box. */
  function keepPortable(i: number) {
    const s = groups[i].scrub
    if (!s) return
    update(i, { numbers: [...s.local, ...s.tollFree].map(fmtNumber).join('\n'), scrub: { ...s, alreadyYours: [], unavailable: [], inProgress: [], invalid: [] } })
  }

  async function submit() {
    setBusy(true); setError(null)
    try {
      await api.post('/api/v1/porting/orders', {
        groups: groups.map((g) => ({ ...g, preAssignCampaignId: g.preAssignCampaignId || null, endUserName: g.endUserName || null, scrub: undefined })),
      })
      onDone()
    } catch (e) { setError(e instanceof Error ? e.message : 'Request failed.') }
    finally { setBusy(false) }
  }

  const ready = groups.every((g) => g.scrub && g.scrub.local.length + g.scrub.tollFree.length > 0
    && g.scrub.alreadyYours.length + g.scrub.unavailable.length + g.scrub.inProgress.length + g.scrub.invalid.length === 0 && g.signerEmail.includes('@'))

  return (
    <div className="bg-gray-900 rounded-xl border border-indigo-900/60 p-5 mb-6 space-y-5">
      <div className="flex items-center justify-between">
        <h2 className="text-white font-semibold">New port request</h2>
        <button onClick={onCancel} className="text-gray-400 hover:text-white" title="Cancel"><CloseIcon size={18} /></button>
      </div>
      <p className="text-gray-400 text-xs">
        One group per phone account and service address (the carrier needs a separate authorization for each). Toll-free and local numbers
        in the same group are split into separate orders automatically.
      </p>
      {groups.map((g, i) => (
        <div key={i} className="border border-gray-800 rounded-lg p-4 space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-sm text-gray-300 font-medium">Account {i + 1}</span>
            {groups.length > 1 && <button onClick={() => setGroups((c) => c.filter((_, j) => j !== i))} className="text-xs text-gray-500 hover:text-red-400">Remove</button>}
          </div>
          <label className="block text-xs text-gray-400">Numbers — paste them, one per line or separated by commas
            <textarea value={g.numbers} onChange={(e) => update(i, { numbers: e.target.value, scrub: null })} rows={4}
              className={`${input} mt-1 font-mono`} placeholder={'(503) 555-0100\n800-555-0199'} />
          </label>
          <div className="flex items-center gap-3">
            <button onClick={() => void check(i)} disabled={!g.numbers.trim()} className="bg-gray-800 hover:bg-gray-700 disabled:opacity-40 text-gray-200 text-xs rounded-lg px-3 py-1.5">Check numbers</button>
            {g.scrub && <ScrubSummary s={g.scrub} onKeepPortable={() => keepPortable(i)} />}
          </div>
          <div className="grid sm:grid-cols-2 gap-3">
            <label className="text-xs text-gray-400">Current phone company (if you know it)
              <input value={g.currentProvider} onChange={(e) => update(i, { currentProvider: e.target.value })} className={`${input} mt-1`} />
            </label>
            <label className="text-xs text-gray-400">Account type
              <select value={g.accountType} onChange={(e) => update(i, { accountType: e.target.value })} className={`${input} mt-1`}>
                <option>Business</option><option>Residential</option>
              </select>
            </label>
            <label className="text-xs text-gray-400">Business name on the numbers
              <input value={g.endUserName} onChange={(e) => update(i, { endUserName: e.target.value })} className={`${input} mt-1`} placeholder="Defaults to your account's name" />
            </label>
            <label className="text-xs text-gray-400">Who signs — email of the current phone account's owner
              <input value={g.signerEmail} onChange={(e) => update(i, { signerEmail: e.target.value })} className={`${input} mt-1`} placeholder="owner@company.com" type="email" />
            </label>
            <label className="text-xs text-gray-400 sm:col-span-2">Once ported, put them on (optional — otherwise they wait in your Reserve)
              <select value={g.preAssignCampaignId} onChange={(e) => update(i, { preAssignCampaignId: e.target.value })} className={`${input} mt-1`}>
                <option value="">Reserve — I'll assign them myself</option>
                {campaigns.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            </label>
          </div>
        </div>
      ))}
      <button onClick={() => setGroups((c) => [...c, blank()])} className="text-sm text-indigo-300 hover:text-indigo-200">+ Numbers on another account</button>
      {error && <p className="text-sm text-red-400">{error}</p>}
      <div className="flex items-center gap-3">
        <button onClick={() => void submit()} disabled={!ready || busy}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded-lg px-4 py-2 text-sm font-medium">
          {busy ? 'Sending…' : 'Send for signature'}
        </button>
        {!ready && <span className="text-xs text-gray-500">Check the numbers and enter the signer's email for each account.</span>}
      </div>
    </div>
  )
}

function ScrubSummary({ s, onKeepPortable }: { s: Scrub; onKeepPortable: () => void }) {
  const bad = s.alreadyYours.length + s.unavailable.length + s.inProgress.length + s.invalid.length
  return (
    <div className="text-xs space-y-0.5">
      <div className="text-emerald-300">
        {s.local.length} local, {s.tollFree.length} toll-free ready to port
      </div>
      {s.alreadyYours.length > 0 && <div className="text-amber-300">Already yours: {s.alreadyYours.map(fmtNumber).join(', ')}</div>}
      {s.unavailable.length > 0 && <div className="text-amber-300">Not available to port: {s.unavailable.map(fmtNumber).join(', ')}</div>}
      {s.inProgress.length > 0 && <div className="text-amber-300">Already being ported: {s.inProgress.map(fmtNumber).join(', ')}</div>}
      {s.invalid.length > 0 && <div className="text-red-400">Not phone numbers: {s.invalid.join(', ')}</div>}
      {bad > 0 && <button onClick={onKeepPortable} className="text-indigo-300 hover:text-indigo-200 underline">Keep only the ones ready to port</button>}
    </div>
  )
}

// ── Where the numbers go ─────────────────────────────────────────────────────

/** Until the port date is confirmed: Reserve, or a campaign (with optional script / call-flow overrides) so the numbers
 * take calls the moment the carrier switches. */
function PreAssign({ order, onSaved }: { order: Order; onSaved: () => void }) {
  const [campaigns, setCampaigns] = useState<{ id: string; name: string }[]>([])
  const [flows, setFlows] = useState<FlowOpt[]>([])
  const [campaignId, setCampaignId] = useState(order.preAssignCampaignId ?? '')
  const [flowId, setFlowId] = useState(order.preAssignFlowId ?? '')
  const [telFlowId, setTelFlowId] = useState(order.preAssignTelephonyFlowId ?? '')
  const [msg, setMsg] = useState<string | null>(null)
  useEffect(() => {
    api.get<{ id: string; name: string }[]>('/api/v1/porting/campaigns').then(setCampaigns).catch(() => {})
    api.get<FlowOpt[]>('/api/v1/porting/flows').then(setFlows).catch(() => {})
  }, [])
  const changed = campaignId !== (order.preAssignCampaignId ?? '') || flowId !== (order.preAssignFlowId ?? '') || telFlowId !== (order.preAssignTelephonyFlowId ?? '')

  async function save() {
    try {
      await api.put(`/api/v1/porting/orders/${order.id}/pre-assign`, {
        campaignId: campaignId || null, flowId: campaignId ? flowId || null : null, telephonyFlowId: campaignId ? telFlowId || null : null,
      })
      setMsg('Saved.'); onSaved()
    } catch (e) { setMsg(e instanceof Error ? e.message : 'Save failed.') }
  }

  const sel = 'w-full bg-gray-800 border border-gray-700 rounded-lg px-2 py-1.5 text-xs text-white'
  return (
    <div className="border border-gray-800 rounded-lg p-3 space-y-2">
      <p className="text-gray-300 text-xs">When the port date is confirmed, the numbers are added to your account labelled <b>{order.label}</b>. Put them:</p>
      <select value={campaignId} onChange={(e) => { setCampaignId(e.target.value); setFlowId(''); setTelFlowId('') }} className={sel}>
        <option value="">In the Reserve — I'll assign them myself</option>
        {campaigns.map((c) => <option key={c.id} value={c.id}>On campaign: {c.name}</option>)}
      </select>
      {campaignId && (
        <div className="grid grid-cols-2 gap-2">
          <label className="text-[11px] text-gray-400">Script override
            <select value={flowId} onChange={(e) => setFlowId(e.target.value)} className={`${sel} mt-0.5`}>
              <option value="">Campaign's script</option>
              {flows.filter((f) => f.type === 'crm').map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
            </select>
          </label>
          <label className="text-[11px] text-gray-400">Call flow override
            <select value={telFlowId} onChange={(e) => setTelFlowId(e.target.value)} className={`${sel} mt-0.5`}>
              <option value="">Campaign's call flow</option>
              {flows.filter((f) => f.type === 'telephony').map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
            </select>
          </label>
        </div>
      )}
      {changed && <button onClick={() => void save()} className="bg-indigo-600 hover:bg-indigo-500 text-white text-xs rounded-lg px-3 py-1.5">Save</button>}
      {msg && <span className="text-xs text-gray-400 ml-2">{msg}</span>}
    </div>
  )
}

// ── One order ────────────────────────────────────────────────────────────────

function OrderDrawer({ id, onClose, onChanged }: { id: string; onClose: () => void; onChanged: () => void }) {
  const [d, setD] = useState<OrderDetail | null>(null)
  const [email, setEmail] = useState('')
  const [msg, setMsg] = useState<string | null>(null)
  const [confirmCancel, setConfirmCancel] = useState(false)
  const load = () => api.get<OrderDetail>(`/api/v1/porting/orders/${id}`).then((x) => { setD(x); setEmail(x.order.signerEmail) }).catch((e: Error) => setMsg(e.message))
  useEffect(() => { load() }, [id]) // eslint-disable-line react-hooks/exhaustive-deps

  async function act(p: Promise<unknown>, done: string) {
    try { await p; setMsg(done); load(); onChanged() } catch (e) { setMsg(e instanceof Error ? e.message : 'Failed.') }
  }

  const o = d?.order
  const canResend = o && (o.status === 'awaiting_signature' || o.status === 'needs_correction')
  const canCancel = o && ['awaiting_signature', 'needs_correction', 'ready_to_submit'].includes(o.status)
  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex justify-end" onClick={onClose}>
      <div className="w-full max-w-xl h-full bg-gray-900 border-l border-gray-800 overflow-y-auto p-6" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-white text-lg font-semibold">{o?.reference ?? 'Port'}</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-white"><CloseIcon size={18} /></button>
        </div>
        {!d && <p className="text-gray-400 text-sm">{msg ?? 'Loading…'}</p>}
        {d && o && (
          <div className="space-y-5 text-sm">
            <div className="flex flex-wrap items-center gap-2">
              <span className={`text-xs border rounded px-2 py-0.5 ${PORT_STATUS[o.status]?.cls}`}>{PORT_STATUS[o.status]?.label}</span>
              {o.focDate && <span className="text-emerald-300 text-xs">Moves on {o.focDate}</span>}
              {o.signatureDaysLeft != null && o.status !== 'completed' && o.status !== 'cancelled' && o.signatureDaysLeft <= 5 &&
                <span className="text-amber-300 text-xs">Signature valid {o.signatureDaysLeft} more day(s)</span>}
            </div>
            {d.correctionMessage && <p className="bg-amber-950/40 border border-amber-800 text-amber-200 rounded-lg px-3 py-2">Correction requested: {d.correctionMessage}</p>}
            <div>
              <p className="text-gray-400 text-xs mb-1">{o.numberCount} {o.kind === 'toll_free' ? 'toll-free' : 'local'} number(s) · {o.endUserName}</p>
              <p className="font-mono text-gray-200 text-xs leading-relaxed">{o.numbers.map(fmtNumber).join(', ')}</p>
            </div>
            {o.numbersLoadedAt ? (
              <p className="bg-emerald-950/40 border border-emerald-800 text-emerald-200 rounded-lg px-3 py-2 text-xs">
                The numbers are in your account, labelled <b>{o.label}</b> — find them on Clients / Telephony → Numbers by searching that label.
                {o.preAssignCampaignId ? ' They\'re already on the campaign you chose.' : ' They\'re in your Reserve, ready to assign.'}
              </p>
            ) : !['completed', 'cancelled'].includes(o.status) && (
              <PreAssign order={o} onSaved={() => { load(); onChanged() }} />
            )}
            {(d.hasLoa || d.hasCertificate) && (
              <div className="flex gap-2">
                {d.hasLoa && <button onClick={() => void download(`/api/v1/porting/orders/${id}/files/loa`, `${o.reference} LOA.pdf`)} className="bg-gray-800 hover:bg-gray-700 text-gray-200 text-xs rounded-lg px-3 py-1.5">Signed LOA</button>}
                {d.hasCertificate && <button onClick={() => void download(`/api/v1/porting/orders/${id}/files/certificate`, `${o.reference} signature record.pdf`)} className="bg-gray-800 hover:bg-gray-700 text-gray-200 text-xs rounded-lg px-3 py-1.5">Signature record</button>}
              </div>
            )}
            {canResend && (
              <div className="border border-gray-800 rounded-lg p-3 space-y-2">
                <p className="text-gray-300 text-xs">Waiting for {o.signerEmail} to sign. Send the link again — or to someone else:</p>
                <div className="flex gap-2">
                  <input value={email} onChange={(e) => setEmail(e.target.value)} className={input} />
                  <button onClick={() => void act(api.post(`/api/v1/porting/orders/${id}/resend`, { signerEmail: email }), 'Link sent.')}
                    className="bg-indigo-600 hover:bg-indigo-500 text-white text-xs rounded-lg px-3 shrink-0">Resend link</button>
                </div>
              </div>
            )}
            <div>
              <p className="text-gray-400 text-xs uppercase tracking-wide mb-2">Timeline</p>
              <ol className="space-y-2">
                {d.events.slice().reverse().map((e, i) => (
                  <li key={i} className="flex gap-3">
                    <span className="text-gray-500 text-xs w-28 shrink-0">{fmtDate(e.at)}</span>
                    <span className="text-gray-200">{e.text}<span className="text-gray-500"> — {e.by}</span></span>
                  </li>
                ))}
              </ol>
            </div>
            {canCancel && (confirmCancel ? (
              <div className="flex items-center gap-3 text-xs">
                <span className="text-gray-300">Cancel this port request?</span>
                <button onClick={() => void act(api.post(`/api/v1/porting/orders/${id}/cancel`, {}), 'Cancelled.')} className="text-red-400 hover:text-red-300 font-medium">Cancel it</button>
                <button onClick={() => setConfirmCancel(false)} className="text-gray-400">Keep it</button>
              </div>
            ) : <button onClick={() => setConfirmCancel(true)} className="text-xs text-gray-500 hover:text-red-400">Cancel this request</button>)}
            {msg && <p className="text-xs text-gray-300">{msg}</p>}
          </div>
        )}
      </div>
    </div>
  )
}
