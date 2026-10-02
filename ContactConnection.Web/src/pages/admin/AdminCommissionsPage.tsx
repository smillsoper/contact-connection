import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import {
  commissionsApi, KIND_LABELS, money,
  type CommissionKind, type CommissionRule, type CommissionSettings, type RecalcBatch, type RecalcInput,
  type RecalcPreview, type RuleInput,
} from '../../api/commissions'
import { listClients, listCampaigns, type Client, type Campaign } from '../../api/telephony'
import { productsApi, type ProductSearchResult } from '../../api/products'
import { customFieldsApi, type CustomFieldDefinition } from '../../api/customFields'

// Commissions (S171): rules per campaign — or per client as the default for campaigns with none of
// their own — and the tenant's pay period. The report lives at /commissions (supervisors too).

const inputCls = 'w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm'

function describe(r: CommissionRule) {
  const amount = r.kind === 'percent_of_order' ? `${r.amount}%` : `$${r.amount.toFixed(2)}`
  const what = r.kind === 'percent_of_order' ? 'of the order (less shipping, tax and fees)'
    : r.kind === 'flat_per_order' ? 'per order'
    : r.kind === 'flat_per_product' ? `per unit of ${r.productLabel ?? 'the product'}`
    : `when ${r.fieldName} = "${r.fieldValue}"`
  return `${amount} ${what}${r.tierLabel ? ` · ${r.tierLabel} calls only` : ''}`
}

const when = (local: string) => new Date(local).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })

function effective(r: CommissionRule) {
  if (!r.effectiveFrom && !r.effectiveUntil) return null
  if (r.effectiveFrom && r.effectiveUntil) return `Calls from ${when(r.effectiveFrom)} until ${when(r.effectiveUntil)}`
  return r.effectiveFrom ? `Calls from ${when(r.effectiveFrom)}` : `Calls before ${when(r.effectiveUntil!)}`
}

function RuleEditor({ rule, scope, products, fields, onSave, onClose }: {
  rule: CommissionRule | null
  scope: { clientId?: string; campaignId?: string }
  products: ProductSearchResult[]
  fields: CustomFieldDefinition[]
  onSave: (input: RuleInput) => Promise<void>
  onClose: () => void
}) {
  const [f, setF] = useState<RuleInput>(rule ? {
    name: rule.name, kind: rule.kind, amount: rule.amount, productId: rule.productId, fieldName: rule.fieldName,
    fieldValue: rule.fieldValue, tierLabel: rule.tierLabel, isActive: rule.isActive,
    effectiveFrom: rule.effectiveFrom ?? '', effectiveUntil: rule.effectiveUntil ?? '',
  } : { ...scope, name: '', kind: 'percent_of_order', amount: 0, tierLabel: '', isActive: true, effectiveFrom: '', effectiveUntil: '' })
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const set = (p: Partial<RuleInput>) => setF((s) => ({ ...s, ...p }))

  async function save() {
    setSaving(true); setError(null)
    try {
      await onSave({ ...f, ...scope, tierLabel: f.tierLabel?.trim() || null, effectiveFrom: f.effectiveFrom || null, effectiveUntil: f.effectiveUntil || null })
      onClose()
    }
    catch (e) { setError(e instanceof Error ? e.message : 'Save failed.'); setSaving(false) }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
      <div className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-lg max-h-[90vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-700">
          <h2 className="text-lg font-semibold text-white">{rule ? 'Edit Rule' : 'New Rule'}</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-white text-xl leading-none">&times;</button>
        </div>
        <div className="overflow-y-auto flex-1 px-6 py-4 space-y-4">
          <label className="block">
            <span className="block text-sm font-medium text-gray-300 mb-1">Name</span>
            <input value={f.name} onChange={(e) => set({ name: e.target.value })} className={inputCls} placeholder="e.g. Alpha sales commission" />
          </label>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <label className="block">
              <span className="block text-sm font-medium text-gray-300 mb-1">Pays</span>
              <select value={f.kind} onChange={(e) => set({ kind: e.target.value as CommissionKind })} className={inputCls}>
                {(Object.keys(KIND_LABELS) as CommissionKind[]).map((k) => <option key={k} value={k}>{KIND_LABELS[k]}</option>)}
              </select>
            </label>
            <label className="block">
              <span className="block text-sm font-medium text-gray-300 mb-1">{f.kind === 'percent_of_order' ? 'Percent' : 'Amount ($)'}</span>
              <input type="number" min={0} step="0.01" value={f.amount} onChange={(e) => set({ amount: Number(e.target.value) })} className={inputCls} />
            </label>
          </div>
          {f.kind === 'percent_of_order' && (
            <p className="text-xs text-gray-500">Of the order total less shipping, tax and fees.</p>
          )}
          {f.kind === 'flat_per_product' && (
            <label className="block">
              <span className="block text-sm font-medium text-gray-300 mb-1">Product</span>
              <select value={f.productId ?? ''} onChange={(e) => set({ productId: e.target.value || null })} className={inputCls}>
                <option value="">Choose…</option>
                {products.map((p) => <option key={p.id} value={p.id}>{p.sku} — {p.description}</option>)}
              </select>
            </label>
          )}
          {f.kind === 'flat_per_field' && (
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <label className="block">
                <span className="block text-sm font-medium text-gray-300 mb-1">Custom field</span>
                <select value={f.fieldName ?? ''} onChange={(e) => set({ fieldName: e.target.value || null })} className={inputCls}>
                  <option value="">Choose…</option>
                  {fields.map((d) => <option key={d.id} value={d.fieldName}>{d.displayLabel} ({d.fieldName})</option>)}
                </select>
              </label>
              <label className="block">
                <span className="block text-sm font-medium text-gray-300 mb-1">Equals</span>
                <input value={f.fieldValue ?? ''} onChange={(e) => set({ fieldValue: e.target.value })} className={inputCls} placeholder="e.g. Discount" />
              </label>
            </div>
          )}
          {f.kind === 'flat_per_field' && (
            <p className="text-xs text-gray-500">Earned whether or not the call placed an order — for script flags like a retention call's save method.</p>
          )}
          <label className="block">
            <span className="block text-sm font-medium text-gray-300 mb-1">Only for calls routed to tier (optional)</span>
            <input list="cc-tier-labels" value={f.tierLabel ?? ''} onChange={(e) => set({ tierLabel: e.target.value })} className={inputCls} placeholder="Any tier" />
            <datalist id="cc-tier-labels"><option value="Alpha" /><option value="Elite" /></datalist>
            <span className="block text-xs text-gray-500 mt-1">
              A tier rule replaces the general rule of the same kind for that tier's calls — e.g. "10% · Alpha" plus "1%" pays Alpha calls 10% and all others 1%.
            </span>
          </label>
          <div>
            <p className="text-sm font-medium text-gray-300 mb-1">Applies to calls that started (optional)</p>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <label className="block">
                <span className="block text-xs text-gray-400 mb-1">From</span>
                <input type="datetime-local" value={f.effectiveFrom ?? ''} onChange={(e) => set({ effectiveFrom: e.target.value })} className={inputCls} />
              </label>
              <label className="block">
                <span className="block text-xs text-gray-400 mb-1">Until (not including)</span>
                <input type="datetime-local" value={f.effectiveUntil ?? ''} onChange={(e) => set({ effectiveUntil: e.target.value })} className={inputCls} />
              </label>
            </div>
            <span className="block text-xs text-gray-500 mt-1">
              In your tenant's time zone. To change a rate from a date: end the old rule there and add the new one starting there,
              then use "Recalculate past calls" for calls already taken.
            </span>
          </div>
          <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
            <input type="checkbox" checked={f.isActive} onChange={(e) => set({ isActive: e.target.checked })} className="accent-indigo-600" />
            Active
          </label>
          {error && <p className="text-red-400 text-sm">{error}</p>}
        </div>
        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-700">
          <button onClick={onClose} className="px-4 py-2 text-sm text-gray-300 hover:text-white">Cancel</button>
          <button onClick={save} disabled={saving} className="px-5 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg disabled:opacity-50">
            {saving ? 'Saving…' : 'Save Rule'}
          </button>
        </div>
      </div>
    </div>
  )
}

function PayPeriodCard() {
  const [s, setS] = useState<CommissionSettings | null>(null)
  const [freq, setFreq] = useState('biweekly')
  const [start, setStart] = useState('')
  const [msg, setMsg] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    commissionsApi.settings().then((r) => { setS(r); setFreq(r.frequency); setStart(r.start) }).catch((e: Error) => setError(e.message))
  }, [])

  async function save() {
    setMsg(null); setError(null)
    try { const r = await commissionsApi.saveSettings(freq, start); setS(r); setMsg('Saved.') }
    catch (e) { setError(e instanceof Error ? e.message : 'Save failed.') }
  }

  return (
    <div className="bg-gray-800 border border-gray-700 rounded-xl p-4">
      <h2 className="text-white font-medium mb-1">Pay period</h2>
      <p className="text-xs text-gray-400 mb-3">
        How commission reports group earnings, in your tenant's time zone{s ? ` (${s.timezone})` : ''}.
        {s && <> Current period: <span className="text-gray-200">{s.current.label}</span>.</>}
      </p>
      <div className="flex flex-wrap items-end gap-3">
        <label className="block">
          <span className="block text-xs text-gray-400 mb-1">Frequency</span>
          <select value={freq} onChange={(e) => setFreq(e.target.value)} className={inputCls}>
            <option value="weekly">Weekly</option>
            <option value="biweekly">Every two weeks</option>
            <option value="semimonthly">Twice a month (1st–15th, 16th–end)</option>
            <option value="monthly">Monthly</option>
          </select>
        </label>
        {(freq === 'weekly' || freq === 'biweekly') && (
          <label className="block">
            <span className="block text-xs text-gray-400 mb-1">A period starts on</span>
            <input type="date" value={start} onChange={(e) => setStart(e.target.value)} className={inputCls} />
          </label>
        )}
        <button onClick={save} className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg">Save</button>
      </div>
      {msg && <p className="text-emerald-400 text-xs mt-2">{msg}</p>}
      {error && <p className="text-red-400 text-xs mt-2">{error}</p>}
    </div>
  )
}

/**
 * Recalculate past calls (S171): re-run commission for calls in a window under the rules in effect when
 * each started — for commission set up late or changed retroactively. Preview first; the Worker applies
 * it as a batch. Corrections post now (periods already paid) or on each call's date (not yet paid).
 */
function RecalcCard({ clientId, campaignId }: { clientId: string; campaignId: string }) {
  const [from, setFrom] = useState('')
  const [to, setTo] = useState(() => new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 16))
  const [postTo, setPostTo] = useState<'current' | 'call_date'>('current')
  const [reason, setReason] = useState('')
  const [preview, setPreview] = useState<RecalcPreview | null>(null)
  const [batches, setBatches] = useState<RecalcBatch[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const input = (): RecalcInput => ({ ...(campaignId ? { campaignId } : { clientId }), from, to, postTo, reason })
  const loadBatches = () => commissionsApi.recalcs().then(setBatches).catch(() => {})
  useEffect(() => { loadBatches() }, [])
  useEffect(() => { setPreview(null) }, [clientId, campaignId, from, to])

  // While a batch is running, follow its progress.
  const active = batches.some((b) => b.status === 'pending' || b.status === 'running')
  useEffect(() => {
    if (!active) return
    const t = setInterval(loadBatches, 2000)
    return () => clearInterval(t)
  }, [active])

  async function run(fn: () => Promise<void>) {
    setBusy(true); setError(null)
    try { await fn() } catch (e) { setError(e instanceof Error ? e.message : 'Failed.') } finally { setBusy(false) }
  }

  return (
    <div className="bg-gray-800 border border-gray-700 rounded-xl p-4 space-y-3">
      <div>
        <h2 className="text-white font-medium">Recalculate past calls</h2>
        <p className="text-xs text-gray-400 mt-1">
          For the client / campaign selected above: recalculates every call that started in the window under the rules in effect when it
          started. Use it after setting up commission late or changing it retroactively. Preview shows what would change before anything does.
        </p>
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <label className="block">
          <span className="block text-xs text-gray-400 mb-1">Calls that started from</span>
          <input type="datetime-local" value={from} onChange={(e) => setFrom(e.target.value)} className={inputCls} />
        </label>
        <label className="block">
          <span className="block text-xs text-gray-400 mb-1">Until</span>
          <input type="datetime-local" value={to} onChange={(e) => setTo(e.target.value)} className={inputCls} />
        </label>
      </div>
      <button disabled={busy || !from || !to} onClick={() => run(async () => setPreview(await commissionsApi.previewRecalc(input())))}
        className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded-lg disabled:opacity-50">
        {busy && !preview ? 'Checking…' : 'Preview'}
      </button>

      {preview && (
        <div className="border border-gray-700 rounded-lg p-3 space-y-3">
          <p className="text-sm text-gray-300">
            {preview.calls.toLocaleString()} call{preview.calls === 1 ? '' : 's'} in the window — <span className="text-white font-medium">{preview.changedCalls.toLocaleString()}</span> would change.
            Commission {money(preview.current)} → {money(preview.recalculated)}{' '}
            <span className={preview.difference < 0 ? 'text-red-400' : 'text-emerald-400'}>({preview.difference >= 0 ? '+' : ''}{money(preview.difference)})</span>
          </p>
          {preview.agents.length > 0 && (
            <table className="w-full text-xs">
              <thead>
                <tr className="text-left text-gray-500 border-b border-gray-700">
                  <th className="py-1 pr-3 font-medium">Agent</th>
                  <th className="py-1 pr-3 font-medium text-right">Calls changing</th>
                  <th className="py-1 pr-3 font-medium text-right">Now</th>
                  <th className="py-1 pr-3 font-medium text-right">Recalculated</th>
                  <th className="py-1 pr-3 font-medium text-right">Difference</th>
                </tr>
              </thead>
              <tbody>
                {preview.agents.map((a) => (
                  <tr key={a.agentId} className="border-b border-gray-800">
                    <td className="py-1 pr-3 text-gray-200">{a.agentName}</td>
                    <td className="py-1 pr-3 text-right text-gray-300">{a.changedCalls}</td>
                    <td className="py-1 pr-3 text-right text-gray-300">{money(a.current)}</td>
                    <td className="py-1 pr-3 text-right text-gray-300">{money(a.recalculated)}</td>
                    <td className={`py-1 pr-3 text-right ${a.difference < 0 ? 'text-red-400' : a.difference > 0 ? 'text-emerald-400' : 'text-gray-500'}`}>{money(a.difference)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {preview.changedCalls > 0 && (
            <div className="space-y-2">
              <p className="text-xs font-medium text-gray-300">Where corrections count</p>
              <label className="flex items-start gap-2 text-xs text-gray-300 cursor-pointer">
                <input type="radio" checked={postTo === 'current'} onChange={() => setPostTo('current')} className="mt-0.5 accent-indigo-600" />
                <span>In the current pay period <span className="block text-gray-500">The affected periods were already paid — payroll picks up the correction now.</span></span>
              </label>
              <label className="flex items-start gap-2 text-xs text-gray-300 cursor-pointer">
                <input type="radio" checked={postTo === 'call_date'} onChange={() => setPostTo('call_date')} className="mt-0.5 accent-indigo-600" />
                <span>On each call's date <span className="block text-gray-500">The affected periods haven't been paid yet — their totals change.</span></span>
              </label>
              <input value={reason} onChange={(e) => setReason(e.target.value)} className={inputCls}
                placeholder="Reason (noted on every correction) — e.g. NeuroQ commission set up retroactive to Sep 15" />
              <button disabled={busy || !reason.trim() || active}
                onClick={() => run(async () => { await commissionsApi.startRecalc(input()); setPreview(null); setReason(''); await loadBatches() })}
                className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg disabled:opacity-50">
                Apply to {preview.changedCalls.toLocaleString()} call{preview.changedCalls === 1 ? '' : 's'}
              </button>
              {active && <p className="text-xs text-amber-300">Another recalculation is still running.</p>}
            </div>
          )}
        </div>
      )}
      {error && <p className="text-red-400 text-sm">{error}</p>}

      {batches.length > 0 && (
        <div>
          <p className="text-xs font-medium text-gray-400 mb-1">Recent recalculations</p>
          <ul className="divide-y divide-gray-700 text-xs">
            {batches.map((b) => (
              <li key={b.id} className="py-1.5">
                <div className="flex justify-between gap-3">
                  <span className="text-gray-200">{b.scope} · {when(b.from)} – {when(b.to)}</span>
                  <span className={b.status === 'failed' ? 'text-red-400' : b.status === 'completed' ? 'text-emerald-400' : 'text-amber-300'}>
                    {b.status === 'running' ? `Running ${b.processedCalls}/${b.totalCalls}` : b.status}
                  </span>
                </div>
                <div className="text-gray-500">
                  {b.reason} · {b.postTo === 'call_date' ? "posted on each call's date" : 'posted to the current period'}
                  {b.status === 'completed' && <> · {b.changedCalls} changed · {b.difference >= 0 ? '+' : ''}{money(b.difference)}</>}
                  {b.requestedBy && <> · {b.requestedBy}</>} · {new Date(b.createdAt).toLocaleString()}
                  {b.error && <span className="text-red-400"> · {b.error}</span>}
                </div>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}

export default function AdminCommissionsPage() {
  const [clients, setClients] = useState<Client[]>([])
  const [campaigns, setCampaigns] = useState<Campaign[]>([])
  const [clientId, setClientId] = useState('')
  const [campaignId, setCampaignId] = useState('')   // '' = the client's default rules
  const [rules, setRules] = useState<CommissionRule[] | null>(null)
  const [clientRuleCount, setClientRuleCount] = useState(0)
  // Every rule on the tenant — to show which campaigns have rules of their own.
  const [allRules, setAllRules] = useState<CommissionRule[]>([])
  const [products, setProducts] = useState<ProductSearchResult[]>([])
  const [fields, setFields] = useState<CustomFieldDefinition[]>([])
  const [editing, setEditing] = useState<CommissionRule | 'new' | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    listClients().then((c) => { setClients(c); if (c.length > 0) setClientId(c[0].id) }).catch(() => setError('Failed to load clients.'))
    productsApi.search('', 1, 200, true).then(setProducts).catch(() => {})
    customFieldsApi.listDefinitions().then((d) => setFields(d.filter((x) => x.isActive))).catch(() => {})
  }, [])

  useEffect(() => {
    if (!clientId) return
    setCampaignId('')
    listCampaigns(clientId).then(setCampaigns).catch(() => setCampaigns([]))
  }, [clientId])

  const scope = campaignId ? { campaignId } : { clientId }
  const load = () => {
    if (!clientId) return
    setRules(null)
    commissionsApi.rules(scope).then(setRules).catch((e: Error) => setError(e.message))
    commissionsApi.rules({}).then(setAllRules).catch(() => {})
    if (campaignId) commissionsApi.rules({ clientId }).then((r) => setClientRuleCount(r.filter((x) => x.isActive).length)).catch(() => {})
  }
  useEffect(load, [clientId, campaignId]) // eslint-disable-line react-hooks/exhaustive-deps

  async function remove(r: CommissionRule) {
    setError(null)
    try { await commissionsApi.deleteRule(r.id); load() } catch (e) { setError(e instanceof Error ? e.message : 'Delete failed.') }
  }

  const usesClientRules = campaignId !== '' && rules !== null && rules.filter((r) => r.isActive).length === 0
  const ownRuleCount = (id: string) => allRules.filter((r) => r.campaignId === id && r.isActive).length
  const overriding = campaigns.filter((c) => ownRuleCount(c.id) > 0)

  return (
    <AdminShell>
      <div className="max-w-4xl mx-auto space-y-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold text-white">Commissions</h1>
            <p className="text-sm text-gray-400 mt-1">
              What agents earn per call. Rules are set per campaign; a client's rules are the default for its campaigns that have none of their own.
            </p>
          </div>
          <Link to="/commissions" className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded-lg whitespace-nowrap">Commission report →</Link>
        </div>

        <PayPeriodCard />

        <div className="bg-gray-800 border border-gray-700 rounded-xl p-4 space-y-4">
          <div className="flex flex-wrap items-end gap-3">
            <label className="block">
              <span className="block text-xs text-gray-400 mb-1">Client</span>
              <select value={clientId} onChange={(e) => setClientId(e.target.value)} className={inputCls}>
                {clients.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            </label>
            <label className="block">
              <span className="block text-xs text-gray-400 mb-1">Rules for</span>
              <select value={campaignId} onChange={(e) => setCampaignId(e.target.value)} className={inputCls}>
                <option value="">All campaigns (client default)</option>
                {campaigns.map((c) => {
                  const n = ownRuleCount(c.id)
                  return <option key={c.id} value={c.id}>{c.name}{n > 0 ? ` (${n} rule${n === 1 ? '' : 's'} of its own)` : ''}</option>
                })}
              </select>
            </label>
            <div className="flex-1" />
            <button onClick={() => setEditing('new')} disabled={!clientId}
              className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg disabled:opacity-50">+ New Rule</button>
          </div>

          {campaignId === '' && overriding.length > 0 && (
            <p className="text-xs text-amber-300 bg-amber-950/30 border border-amber-900/50 rounded-lg px-3 py-2">
              These rules apply to this client's campaigns that have none of their own. {overriding.map((c) => c.name).join(', ')}{' '}
              {overriding.length === 1 ? 'has its own rules, so it doesn\'t use these' : 'have their own rules, so they don\'t use these'} —
              pick {overriding.length === 1 ? 'it' : 'one'} under "Rules for" to see or change {overriding.length === 1 ? 'them' : 'its rules'}.
            </p>
          )}
          {usesClientRules && (
            <p className="text-xs text-amber-300 bg-amber-950/30 border border-amber-900/50 rounded-lg px-3 py-2">
              This campaign has no rules of its own, so it uses the client default ({clientRuleCount} active rule{clientRuleCount === 1 ? '' : 's'}).
              Adding a rule here replaces the client default for this campaign entirely.
            </p>
          )}
          {error && <p className="text-red-400 text-sm">{error}</p>}
          {!rules ? <p className="text-gray-400 text-sm">Loading…</p> : rules.length === 0 ? (
            <p className="text-gray-500 italic text-sm">No rules here yet.</p>
          ) : (
            <div className="space-y-2">
              {rules.map((r) => (
                <div key={r.id} className="bg-gray-900/60 border border-gray-700 rounded-lg p-3 flex items-center justify-between gap-4">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-white text-sm font-medium">{r.name}</span>
                      {!r.isActive && <span className="text-xs bg-gray-700 text-gray-400 px-2 py-0.5 rounded-full">Inactive</span>}
                    </div>
                    <p className="text-xs text-gray-400 mt-0.5">{describe(r)}</p>
                    {effective(r) && <p className="text-xs text-indigo-300 mt-0.5">{effective(r)}</p>}
                  </div>
                  <div className="flex gap-2 flex-shrink-0">
                    <button onClick={() => setEditing(r)} className="px-3 py-1.5 text-xs bg-gray-700 hover:bg-gray-600 text-white rounded-lg">Edit</button>
                    <button onClick={() => remove(r)} className="px-3 py-1.5 text-xs text-gray-400 hover:text-red-400">Delete</button>
                  </div>
                </div>
              ))}
            </div>
          )}
          <p className="text-[11px] text-gray-500 leading-snug">
            Commissions are recorded when an order is submitted (the flow's API node marked "Order submission") and when the script ends;
            changes later — a resubmitted order, an edited custom field — reverse the old entries and record new ones. Deleting or editing a
            rule doesn't change commissions already recorded.
          </p>
        </div>

        <RecalcCard clientId={clientId} campaignId={campaignId} />
      </div>
      {editing !== null && (
        <RuleEditor
          rule={editing === 'new' ? null : editing}
          scope={scope}
          products={products}
          fields={fields}
          onSave={async (input) => {
            if (editing === 'new') await commissionsApi.createRule(input)
            else await commissionsApi.updateRule(editing.id, input)
            load()
          }}
          onClose={() => setEditing(null)}
        />
      )}
    </AdminShell>
  )
}
