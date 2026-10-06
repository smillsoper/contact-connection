import { useEffect, useMemo, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import {
  dispositionsApi, type Disposition, type DispositionCategory, type DispositionInput, type UnmappedDisposition,
} from '../../api/dispositions'
import { listCampaigns, listClients, type Campaign, type Client } from '../../api/telephony'

// Dispositions (S181, docs/dispositions-kpi-plan.md). A disposition is the outcome of an interaction; its reporting
// category is what the KPIs read. Scoped like custom fields: everywhere (tenant), a client, or a campaign — the narrowest
// wins for the same name. KPIs follow the CURRENT mapping, so every change here re-links past calls too.

const input = 'w-full bg-gray-900 border border-gray-700 rounded px-2 py-1.5 text-sm text-gray-100 focus:outline-none focus:border-indigo-500'
const label = 'block text-xs text-gray-400 mb-1'
const card = 'bg-gray-800/60 border border-gray-700 rounded-lg p-4 mb-4'
const btn = 'px-3 py-1.5 rounded text-sm disabled:opacity-50'

type Tab = 'dispositions' | 'categories' | 'unmapped'

const emptyInput = (categoryId: string): DispositionInput =>
  ({ name: '', code: null, categoryId, clientId: null, campaignId: null, aliases: [] })

function DispositionForm({ value, onChange, categories, clients, campaigns, scopeLocked }: {
  value: DispositionInput; onChange: (v: DispositionInput) => void; categories: DispositionCategory[]
  clients: Client[]; campaigns: Campaign[]; scopeLocked?: boolean
}) {
  const set = (p: Partial<DispositionInput>) => onChange({ ...value, ...p })
  const scope = value.campaignId ? 'campaign' : value.clientId ? 'client' : 'tenant'
  return (
    <div className="grid sm:grid-cols-4 gap-3">
      <div className="sm:col-span-2">
        <label className={label}>Name (what the script records)</label>
        <input className={input} value={value.name} onChange={(e) => set({ name: e.target.value })} />
      </div>
      <div>
        <label className={label}>Reporting category</label>
        <select className={input} value={value.categoryId} onChange={(e) => set({ categoryId: e.target.value })}>
          {categories.filter((c) => c.isActive).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
      </div>
      <div>
        <label className={label}>Export code (optional)</label>
        <input className={input} value={value.code ?? ''} onChange={(e) => set({ code: e.target.value || null })} placeholder="e.g. ORD" />
      </div>
      <div>
        <label className={label}>Applies to</label>
        <select className={input} disabled={scopeLocked} value={scope}
          onChange={(e) => set(e.target.value === 'tenant' ? { clientId: null, campaignId: null }
            : e.target.value === 'client' ? { clientId: clients[0]?.id ?? null, campaignId: null }
            : { campaignId: campaigns[0]?.id ?? null, clientId: null })}>
          <option value="tenant">Every campaign</option>
          <option value="client">One client</option>
          <option value="campaign">One campaign</option>
        </select>
      </div>
      {scope === 'client' && (
        <div>
          <label className={label}>Client</label>
          <select className={input} disabled={scopeLocked} value={value.clientId ?? ''} onChange={(e) => set({ clientId: e.target.value })}>
            {clients.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </div>
      )}
      {scope === 'campaign' && (
        <div>
          <label className={label}>Campaign</label>
          <select className={input} disabled={scopeLocked} value={value.campaignId ?? ''} onChange={(e) => set({ campaignId: e.target.value })}>
            {campaigns.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </div>
      )}
      <div className="sm:col-span-4">
        <label className={label}>Also recorded as (aliases, comma-separated — older wordings that mean the same thing)</label>
        <input className={input} value={value.aliases.join(', ')}
          onChange={(e) => set({ aliases: e.target.value.split(',').map((x) => x.trim()).filter(Boolean) })} />
      </div>
    </div>
  )
}

export default function AdminDispositionsPage() {
  const [tab, setTab] = useState<Tab>('dispositions')
  const [categories, setCategories] = useState<DispositionCategory[]>([])
  const [dispositions, setDispositions] = useState<Disposition[]>([])
  const [unmapped, setUnmapped] = useState<UnmappedDisposition[]>([])
  const [clients, setClients] = useState<Client[]>([])
  const [campaigns, setCampaigns] = useState<Campaign[]>([])
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [editing, setEditing] = useState<{ id: string | null; value: DispositionInput } | null>(null)
  const [catEdit, setCatEdit] = useState<{ id: string | null; name: string; description: string; salesOpportunity: boolean; excludedFromKpis: boolean } | null>(null)
  const [resolving, setResolving] = useState<{ text: string; mode: 'existing' | 'new'; dispositionId: string; value: DispositionInput } | null>(null)
  const [busy, setBusy] = useState(false)

  const load = async () => {
    try {
      const [cats, ds, um] = await Promise.all([dispositionsApi.categories(), dispositionsApi.list(), dispositionsApi.unmapped()])
      setCategories(cats); setDispositions(ds); setUnmapped(um)
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not load dispositions.') }
  }
  useEffect(() => {
    load()
    listClients().then(setClients).catch(() => { })
    listCampaigns().then(setCampaigns).catch(() => { })
  }, [])

  const catName = useMemo(() => Object.fromEntries(categories.map((c) => [c.id, c])), [categories])
  const scopeName = (d: { clientId: string | null; campaignId: string | null }) =>
    d.campaignId ? campaigns.find((c) => c.id === d.campaignId)?.name ?? 'A campaign'
      : d.clientId ? clients.find((c) => c.id === d.clientId)?.name ?? 'A client' : 'Every campaign'
  const defaultCategory = categories.find((c) => c.key === 'other')?.id ?? categories[0]?.id ?? ''

  async function run(work: () => Promise<string | void>) {
    setBusy(true); setError(null); setNotice(null)
    try { const msg = await work(); if (msg) setNotice(msg); await load() }
    catch (e) { setError(e instanceof Error ? e.message : 'Failed.') }
    finally { setBusy(false) }
  }
  const relinkedMsg = (n: number) => n > 0 ? `Saved — ${n} past interaction${n === 1 ? '' : 's'} re-linked.` : 'Saved.'

  const unmappedTotal = unmapped.reduce((n, u) => n + u.productionCount, 0)

  return (
    <AdminShell>
      <div className="max-w-6xl mx-auto px-4 sm:px-6 py-6">
        <h1 className="text-xl font-semibold text-white mb-1">Dispositions</h1>
        <p className="text-sm text-gray-400 mb-4">
          The outcome of each interaction, and what it means for reporting. KPIs read the <b className="text-gray-300">category</b>,
          so changing a disposition's category corrects every past call too.
        </p>
        <div className="flex flex-wrap gap-1 mb-4 border-b border-gray-700">
          {(['dispositions', 'categories', 'unmapped'] as const).map((t) => (
            <button key={t} onClick={() => setTab(t)}
              className={`px-4 py-2 text-sm -mb-px border-b-2 ${tab === t ? 'border-indigo-500 text-white' : 'border-transparent text-gray-400 hover:text-gray-200'}`}>
              {t === 'dispositions' ? `Dispositions (${dispositions.length})` : t === 'categories' ? 'Reporting categories'
                : <>Unmapped {unmapped.length > 0 && <span className="ml-1 px-1.5 rounded bg-amber-700 text-white text-xs">{unmapped.length}</span>}</>}
            </button>
          ))}
        </div>
        {error && <p className="text-red-400 text-sm mb-3 whitespace-pre-wrap">{error}</p>}
        {notice && <p className="text-emerald-400 text-sm mb-3">{notice}</p>}

        {tab === 'dispositions' && (
          <>
            {editing ? (
              <div className={card}>
                <h2 className="text-sm font-semibold text-gray-100 mb-3">{editing.id ? 'Edit disposition' : 'New disposition'}</h2>
                <DispositionForm value={editing.value} onChange={(v) => setEditing({ ...editing, value: v })}
                  categories={categories} clients={clients} campaigns={campaigns} scopeLocked={!!editing.id} />
                <div className="flex gap-2 mt-3">
                  <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white`} disabled={busy || !editing.value.name.trim()}
                    onClick={() => run(async () => {
                      const r = editing.id ? await dispositionsApi.update(editing.id, editing.value) : await dispositionsApi.create(editing.value)
                      setEditing(null); return relinkedMsg(r.relinked)
                    })}>Save</button>
                  <button className={`${btn} bg-gray-700 hover:bg-gray-600 text-white`} onClick={() => setEditing(null)}>Cancel</button>
                </div>
              </div>
            ) : (
              <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white mb-4`} disabled={!defaultCategory}
                onClick={() => setEditing({ id: null, value: emptyInput(defaultCategory) })}>New disposition</button>
            )}
            {dispositions.length === 0 ? (
              <p className="text-gray-500 italic text-sm">No dispositions yet. Start from the <button className="text-indigo-400 underline" onClick={() => setTab('unmapped')}>Unmapped</button> tab — it lists what your scripts have actually recorded.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-left text-xs text-gray-500 border-b border-gray-700">
                      <th className="py-2 pr-3 font-medium">Disposition</th>
                      <th className="py-2 pr-3 font-medium">Category</th>
                      <th className="py-2 pr-3 font-medium">Applies to</th>
                      <th className="py-2 pr-3 font-medium">Calls</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {dispositions.map((d) => (
                      <tr key={d.id} className={`border-b border-gray-800 align-top ${d.isActive ? '' : 'opacity-50'}`}>
                        <td className="py-2 pr-3 text-gray-100">
                          {d.name}{d.code && <span className="ml-2 text-xs text-gray-500 font-mono">{d.code}</span>}
                          {!d.isActive && <span className="ml-2 text-xs text-amber-300">retired</span>}
                          {d.aliases.length > 0 && <span className="block text-xs text-gray-500">also: {d.aliases.join(', ')}</span>}
                        </td>
                        <td className="py-2 pr-3 text-gray-300">
                          <select className="bg-gray-900 border border-gray-700 rounded px-1.5 py-1 text-xs text-gray-100" value={d.categoryId}
                            onChange={(e) => run(async () => relinkedMsg((await dispositionsApi.update(d.id,
                              { name: d.name, code: d.code, categoryId: e.target.value, clientId: d.clientId, campaignId: d.campaignId, aliases: d.aliases, displayOrder: d.displayOrder })).relinked))}>
                            {categories.filter((c) => c.isActive || c.id === d.categoryId).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                          </select>
                        </td>
                        <td className="py-2 pr-3 text-gray-400 text-xs">{scopeName(d)}</td>
                        <td className="py-2 pr-3 text-gray-400">{d.interactions}</td>
                        <td className="py-2 whitespace-nowrap text-right text-sm">
                          <button className="text-indigo-400 hover:text-indigo-300 mr-3"
                            onClick={() => setEditing({ id: d.id, value: { name: d.name, code: d.code, categoryId: d.categoryId, clientId: d.clientId, campaignId: d.campaignId, aliases: d.aliases, displayOrder: d.displayOrder } })}>Edit</button>
                          {d.interactions === 0
                            ? <button className="text-red-400 hover:text-red-300" onClick={() => run(async () => { await dispositionsApi.remove(d.id); return 'Deleted.' })}>Delete</button>
                            : <button className="text-gray-400 hover:text-white" onClick={() => run(async () => { await dispositionsApi.setActive(d.id, !d.isActive); return d.isActive ? 'Retired — past calls keep it.' : 'Restored.' })}>{d.isActive ? 'Retire' : 'Restore'}</button>}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}

        {tab === 'categories' && (
          <>
            <p className="text-xs text-gray-400 mb-3">
              <b className="text-gray-300">Sales opportunity</b> = counts in the gross / net close-rate denominator.{' '}
              <b className="text-gray-300">Excluded from KPIs</b> = left out of every number (test calls).
              Add your own categories (e.g. "Lead captured") to build custom KPIs on.
            </p>
            {catEdit ? (
              <div className={card}>
                <div className="grid sm:grid-cols-2 gap-3">
                  <div><label className={label}>Name</label><input className={input} value={catEdit.name} onChange={(e) => setCatEdit({ ...catEdit, name: e.target.value })} /></div>
                  <div><label className={label}>Description</label><input className={input} value={catEdit.description} onChange={(e) => setCatEdit({ ...catEdit, description: e.target.value })} /></div>
                </div>
                <div className="flex flex-wrap gap-4 mt-3 text-sm text-gray-300">
                  <label className="flex items-center gap-2"><input type="checkbox" checked={catEdit.salesOpportunity} onChange={(e) => setCatEdit({ ...catEdit, salesOpportunity: e.target.checked })} /> Sales opportunity</label>
                  <label className="flex items-center gap-2"><input type="checkbox" checked={catEdit.excludedFromKpis} onChange={(e) => setCatEdit({ ...catEdit, excludedFromKpis: e.target.checked })} /> Excluded from KPIs</label>
                </div>
                <div className="flex gap-2 mt-3">
                  <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white`} disabled={busy || !catEdit.name.trim()}
                    onClick={() => run(async () => {
                      const body = { name: catEdit.name, description: catEdit.description || null, salesOpportunity: catEdit.salesOpportunity, excludedFromKpis: catEdit.excludedFromKpis }
                      if (catEdit.id) await dispositionsApi.updateCategory(catEdit.id, body); else await dispositionsApi.createCategory(body)
                      setCatEdit(null); return 'Saved.'
                    })}>Save</button>
                  <button className={`${btn} bg-gray-700 hover:bg-gray-600 text-white`} onClick={() => setCatEdit(null)}>Cancel</button>
                </div>
              </div>
            ) : (
              <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white mb-4`}
                onClick={() => setCatEdit({ id: null, name: '', description: '', salesOpportunity: false, excludedFromKpis: false })}>New category</button>
            )}
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-xs text-gray-500 border-b border-gray-700">
                  <th className="py-2 pr-3 font-medium">Category</th>
                  <th className="py-2 pr-3 font-medium">Sales opportunity</th>
                  <th className="py-2 pr-3 font-medium">Excluded from KPIs</th>
                  <th className="py-2 pr-3 font-medium">Dispositions</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {categories.map((c) => (
                  <tr key={c.id} className={`border-b border-gray-800 ${c.isActive ? '' : 'opacity-50'}`}>
                    <td className="py-2 pr-3 text-gray-100">{c.name}{c.isSystem && <span className="ml-2 text-xs text-gray-500">built-in</span>}
                      {c.description && <span className="block text-xs text-gray-500">{c.description}</span>}</td>
                    <td className="py-2 pr-3 text-gray-300">{c.salesOpportunity ? 'Yes' : '—'}</td>
                    <td className="py-2 pr-3 text-gray-300">{c.excludedFromKpis ? 'Yes' : '—'}</td>
                    <td className="py-2 pr-3 text-gray-400">{dispositions.filter((d) => d.categoryId === c.id).length}</td>
                    <td className="py-2 text-right whitespace-nowrap text-sm">
                      <button className="text-indigo-400 hover:text-indigo-300 mr-3"
                        onClick={() => setCatEdit({ id: c.id, name: c.name, description: c.description ?? '', salesOpportunity: c.salesOpportunity, excludedFromKpis: c.excludedFromKpis })}>Edit</button>
                      {!c.isSystem && <button className="text-gray-400 hover:text-white" onClick={() => run(async () => { await dispositionsApi.setCategoryActive(c.id, !c.isActive); return 'Saved.' })}>{c.isActive ? 'Retire' : 'Restore'}</button>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}

        {tab === 'unmapped' && (
          <>
            <p className="text-xs text-gray-400 mb-3">
              What scripts have recorded that matches no disposition{unmappedTotal > 0 && <> — <b className="text-amber-300">{unmappedTotal} production interactions</b> aren't counted in category KPIs yet</>}.
              Make each one a new disposition, or an alias of an existing one; matching calls are linked at once.
            </p>
            {resolving && (
              <div className={card}>
                <h2 className="text-sm font-semibold text-gray-100 mb-3">"{resolving.text}"</h2>
                <div className="flex gap-4 mb-3 text-sm text-gray-300">
                  <label className="flex items-center gap-2"><input type="radio" checked={resolving.mode === 'new'} onChange={() => setResolving({ ...resolving, mode: 'new' })} /> New disposition</label>
                  <label className="flex items-center gap-2"><input type="radio" checked={resolving.mode === 'existing'} disabled={dispositions.length === 0}
                    onChange={() => setResolving({ ...resolving, mode: 'existing', dispositionId: resolving.dispositionId || dispositions[0]?.id || '' })} /> Same as an existing disposition</label>
                </div>
                {resolving.mode === 'new'
                  ? <DispositionForm value={resolving.value} onChange={(v) => setResolving({ ...resolving, value: v })} categories={categories} clients={clients} campaigns={campaigns} />
                  : (
                    <select className={input} value={resolving.dispositionId} onChange={(e) => setResolving({ ...resolving, dispositionId: e.target.value })}>
                      {dispositions.filter((d) => d.isActive).map((d) => <option key={d.id} value={d.id}>{d.name} — {catName[d.categoryId]?.name} ({scopeName(d)})</option>)}
                    </select>
                  )}
                <div className="flex gap-2 mt-3">
                  <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white`} disabled={busy}
                    onClick={() => run(async () => {
                      const r = resolving.mode === 'new'
                        ? await dispositionsApi.resolve(resolving.text, { create: resolving.value })
                        : await dispositionsApi.resolve(resolving.text, { dispositionId: resolving.dispositionId })
                      setResolving(null); return relinkedMsg(r.relinked)
                    })}>Save</button>
                  <button className={`${btn} bg-gray-700 hover:bg-gray-600 text-white`} onClick={() => setResolving(null)}>Cancel</button>
                </div>
              </div>
            )}
            {unmapped.length === 0 ? <p className="text-emerald-300 text-sm">Everything recorded maps to a disposition.</p> : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-gray-500 border-b border-gray-700">
                    <th className="py-2 pr-3 font-medium">Recorded as</th>
                    <th className="py-2 pr-3 font-medium">Interactions</th>
                    <th className="py-2 pr-3 font-medium">Campaigns</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {unmapped.map((u) => (
                    <tr key={u.text} className="border-b border-gray-800 align-top">
                      <td className="py-2 pr-3 text-gray-100">{u.text}</td>
                      <td className="py-2 pr-3 text-gray-300">{u.productionCount}{u.count > u.productionCount && <span className="block text-xs text-gray-500">+{u.count - u.productionCount} practice</span>}</td>
                      <td className="py-2 pr-3 text-gray-400 text-xs">{u.campaigns.map((c) => c.name).join(', ') || '—'}</td>
                      <td className="py-2 text-right">
                        <button className="text-indigo-400 hover:text-indigo-300 text-sm"
                          onClick={() => setResolving({ text: u.text, mode: 'new', dispositionId: '', value: { ...emptyInput(defaultCategory), name: u.text } })}>Map…</button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </>
        )}
      </div>
    </AdminShell>
  )
}
