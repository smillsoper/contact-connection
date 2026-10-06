import { useEffect, useState } from 'react'
import { dispositionsApi, type Disposition, type DispositionCategory } from '../../api/dispositions'
import { Link, useSearchParams } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import { abandonLabel, callReviewApi, type CallSearchPage } from '../../api/callReview'
import { listCampaigns, type Campaign } from '../../api/telephony'

const inputCls = 'bg-gray-900 border border-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500'

function fmtDate(iso: string | null) {
  return iso ? new Date(iso).toLocaleString() : '—'
}

function fmtDuration(s: number | null) {
  if (s == null) return '—'
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
}

function fmtPhone(p: string | null) {
  if (!p) return '—'
  const d = p.replace(/\D/g, '').replace(/^1(?=\d{10}$)/, '')
  return d.length === 10 ? `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}` : p
}

/**
 * Call Records (S165) — search past calls; open one to review it, correct its data and resubmit a
 * failed order. Filters live in the URL so a reviewer's link (e.g. from an order-failure email)
 * and the browser back button both keep them.
 */
export default function AdminCallsPage() {
  const [params, setParams] = useSearchParams()
  const [campaigns, setCampaigns] = useState<Campaign[]>([])
  const [categories, setCategories] = useState<DispositionCategory[]>([])
  const [dispositions, setDispositions] = useState<Disposition[]>([])
  const [result, setResult] = useState<CallSearchPage | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const filters = {
    from: params.get('from') ?? '',
    to: params.get('to') ?? '',
    campaignId: params.get('campaignId') ?? '',
    phone: params.get('phone') ?? '',
    orderNumber: params.get('orderNumber') ?? '',
    name: params.get('name') ?? '',
    failedOnly: params.get('failedOnly') === 'true',
    runMode: params.get('runMode') ?? '',
    // One picker (S181): '' | 'unmapped' | 'cat:<id>' | 'disp:<id>'
    disposition: params.get('disposition') ?? '',
    page: Number(params.get('page') ?? '1') || 1,
  }
  const [draft, setDraft] = useState(filters)

  useEffect(() => {
    listCampaigns().then(setCampaigns).catch(() => { /* filter just stays empty */ })
    dispositionsApi.categories().then(setCategories).catch(() => { })
    dispositionsApi.list().then(setDispositions).catch(() => { })
  }, [])

  const key = params.toString()
  useEffect(() => {
    setLoading(true)
    setError(null)
    callReviewApi.search({
      // Date inputs are local calendar days; "to" is inclusive of that whole day.
      from: filters.from ? new Date(`${filters.from}T00:00:00`).toISOString() : undefined,
      to: filters.to ? new Date(new Date(`${filters.to}T00:00:00`).getTime() + 86_400_000).toISOString() : undefined,
      campaignId: filters.campaignId || undefined,
      phone: filters.phone || undefined,
      orderNumber: filters.orderNumber || undefined,
      name: filters.name || undefined,
      failedOnly: filters.failedOnly,
      runMode: filters.runMode || undefined,
      dispositionId: filters.disposition.startsWith('disp:') ? filters.disposition.slice(5) : undefined,
      dispositionCategoryId: filters.disposition.startsWith('cat:') ? filters.disposition.slice(4) : undefined,
      unmappedDisposition: filters.disposition === 'unmapped' || undefined,
      page: filters.page,
      pageSize: 50,
    })
      .then(setResult)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [key])

  function apply(next: typeof draft, page = 1) {
    const p = new URLSearchParams()
    for (const [k, v] of Object.entries({ ...next, page })) {
      if (v === '' || v === false || (k === 'page' && v === 1)) continue
      p.set(k, String(v))
    }
    setParams(p)
  }

  const totalPages = result ? Math.max(1, Math.ceil(result.total / result.pageSize)) : 1

  return (
    <AdminShell>
      <div className="p-6 max-w-7xl">
        <div className="mb-6">
          <h1 className="text-white text-xl font-semibold">Call Records</h1>
          <p className="text-gray-500 text-sm mt-0.5">
            Find a call to review what was captured, correct the customer's details or order, and resubmit an
            order that failed to post.
          </p>
        </div>

        <form
          onSubmit={(e) => { e.preventDefault(); apply(draft) }}
          className="bg-gray-900/60 border border-gray-800 rounded-xl p-4 mb-4 flex flex-wrap items-end gap-3"
        >
          <div className="flex flex-col gap-1">
            <label className="text-gray-500 text-xs">From</label>
            <input type="date" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} className={inputCls} />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-gray-500 text-xs">To</label>
            <input type="date" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} className={inputCls} />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-gray-500 text-xs">Campaign</label>
            <select value={draft.campaignId} onChange={(e) => setDraft({ ...draft, campaignId: e.target.value })} className={inputCls}>
              <option value="">All campaigns</option>
              {campaigns.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-gray-500 text-xs">Phone</label>
            <input value={draft.phone} onChange={(e) => setDraft({ ...draft, phone: e.target.value })} placeholder="Any part of the number" className={`${inputCls} w-44`} />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-gray-500 text-xs">Customer name</label>
            <input value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={`${inputCls} w-44`} />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-gray-500 text-xs">Order #</label>
            <input value={draft.orderNumber} onChange={(e) => setDraft({ ...draft, orderNumber: e.target.value })} className={`${inputCls} w-32`} />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-gray-500 text-xs">Calls</label>
            <select value={draft.runMode} onChange={(e) => setDraft({ ...draft, runMode: e.target.value })} className={`${inputCls} w-40`}
              title="Training and designer-sandbox runs are kept for review but never counted as live calls">
              <option value="">Live calls</option>
              <option value="training">Training runs</option>
              <option value="sandbox">Sandbox runs</option>
              <option value="all">All</option>
            </select>
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-gray-500 text-xs">Disposition</label>
            <select value={draft.disposition} onChange={(e) => setDraft({ ...draft, disposition: e.target.value })} className={`${inputCls} w-52`}
              title="Calls where any interaction (a transferred call has several) recorded it">
              <option value="">Any</option>
              <option value="unmapped">Unmapped (not in the catalog)</option>
              {categories.length > 0 && (
                <optgroup label="Reporting category">
                  {categories.map((c) => <option key={c.id} value={`cat:${c.id}`}>{c.name}</option>)}
                </optgroup>
              )}
              {dispositions.length > 0 && (
                <optgroup label="Disposition">
                  {dispositions.map((d) => <option key={d.id} value={`disp:${d.id}`}>{d.name}{d.isActive ? '' : ' (retired)'}</option>)}
                </optgroup>
              )}
            </select>
          </div>
          <label className="flex items-center gap-2 text-sm text-gray-300 pb-2">
            <input type="checkbox" checked={draft.failedOnly} onChange={(e) => setDraft({ ...draft, failedOnly: e.target.checked })} className="accent-red-500" />
            Failed API call only
          </label>
          <div className="flex gap-2 ml-auto">
            <button type="button" onClick={() => { const empty = { from: '', to: '', campaignId: '', phone: '', orderNumber: '', name: '', failedOnly: false, runMode: '', disposition: '', page: 1 }; setDraft(empty); apply(empty) }}
              className="text-gray-400 hover:text-white text-sm px-3 py-2">Clear</button>
            <button type="submit" className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium">Search</button>
          </div>
        </form>

        {error && <p className="text-red-400 text-sm mb-3">{error}</p>}

        <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-gray-500 text-xs uppercase tracking-wide border-b border-gray-800">
                <th className="px-4 py-2.5 font-medium">Started</th>
                <th className="px-4 py-2.5 font-medium">Campaign</th>
                <th className="px-4 py-2.5 font-medium">Customer</th>
                <th className="px-4 py-2.5 font-medium">Caller ID</th>
                <th className="px-4 py-2.5 font-medium">Billing / shipping phone</th>
                <th className="px-4 py-2.5 font-medium">Agent</th>
                <th className="px-4 py-2.5 font-medium">Disposition</th>
                <th className="px-4 py-2.5 font-medium">Order #</th>
                <th className="px-4 py-2.5 font-medium text-right">Cart</th>
                <th className="px-4 py-2.5 font-medium">Duration</th>
                <th className="px-4 py-2.5 font-medium">Source</th>
                <th className="px-4 py-2.5 font-medium" />
              </tr>
            </thead>
            <tbody>
              {loading && (
                <tr><td colSpan={12} className="px-4 py-6 text-center text-gray-500">Loading…</td></tr>
              )}
              {!loading && result?.items.length === 0 && (
                <tr><td colSpan={12} className="px-4 py-6 text-center text-gray-500">No calls match these filters.</td></tr>
              )}
              {!loading && result?.items.map((c) => (
                <tr key={c.id} className="border-b border-gray-800/60 hover:bg-gray-800/40">
                  <td className="px-4 py-2 text-gray-300 whitespace-nowrap">
                    <Link to={`/admin/calls/${c.id}`} className="hover:text-indigo-300">{fmtDate(c.callStartAt ?? c.createdAt)}</Link>
                  </td>
                  <td className="px-4 py-2 text-gray-300">{c.campaignName ?? '—'}</td>
                  <td className="px-4 py-2 text-white">{c.customerName ?? <span className="text-gray-600">—</span>}</td>
                  <td className="px-4 py-2 text-gray-300 whitespace-nowrap">{fmtPhone(c.callerId)}</td>
                  <td className="px-4 py-2 text-gray-300 whitespace-nowrap">
                    {fmtPhone(c.billingPhone)}
                    {c.shippingPhone && c.shippingPhone !== c.billingPhone && <span className="text-gray-500"> / {fmtPhone(c.shippingPhone)}</span>}
                  </td>
                  <td className="px-4 py-2 text-gray-300">{c.agentName ?? '—'}</td>
                  <td className="px-4 py-2 text-gray-300 text-xs">
                    {c.compoundDisposition ?? <span className="text-gray-600">—</span>}
                    {c.hasUnmappedDisposition && (
                      <span className="block w-fit mt-1 bg-amber-900/40 text-amber-300 border border-amber-800 rounded px-1.5 py-0.5" title="Not in the disposition catalog — map it in Admin → Dispositions">unmapped</span>
                    )}
                  </td>
                  <td className="px-4 py-2 text-gray-300 font-mono text-xs">{c.orderNumber ?? '—'}</td>
                  <td className="px-4 py-2 text-gray-300 text-right">{c.cartTotal != null ? `$${c.cartTotal.toFixed(2)}` : '—'}</td>
                  <td className="px-4 py-2 text-gray-400">{fmtDuration(c.handleTimeSeconds)}</td>
                  <td className="px-4 py-2 text-gray-500 text-xs">
                    {c.source}
                    {c.runMode && c.runMode !== 'production' && (
                      <span className={`block w-fit mt-1 rounded px-1.5 py-0.5 uppercase tracking-wide font-semibold border ${c.runMode === 'training'
                        ? 'bg-amber-950/60 text-amber-300 border-amber-800' : 'bg-violet-950/60 text-violet-300 border-violet-800'}`}>{c.runMode}</span>
                    )}
                    {c.abandon && (
                      <span className="block w-fit mt-1 bg-amber-900/40 text-amber-300 border border-amber-800 rounded px-1.5 py-0.5 whitespace-nowrap">{abandonLabel(c.abandon)}</span>
                    )}
                  </td>
                  <td className="px-4 py-2 text-right whitespace-nowrap">
                    {c.hasFailedApiCall && (
                      <span className="inline-block bg-red-900/50 text-red-300 border border-red-800 rounded px-1.5 py-0.5 text-xs mr-2">API failed</span>
                    )}
                    <Link to={`/admin/calls/${c.id}`} className="text-indigo-400 hover:text-indigo-300 text-xs font-medium">Open →</Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {result && result.total > 0 && (
          <div className="flex items-center justify-between mt-3 text-sm text-gray-400">
            <span>{result.total.toLocaleString()} call{result.total === 1 ? '' : 's'}</span>
            <div className="flex items-center gap-3">
              <button disabled={filters.page <= 1} onClick={() => apply(filters, filters.page - 1)}
                className="px-2 py-1 rounded hover:bg-gray-800 disabled:opacity-30">← Prev</button>
              <span>Page {filters.page} of {totalPages}</span>
              <button disabled={filters.page >= totalPages} onClick={() => apply(filters, filters.page + 1)}
                className="px-2 py-1 rounded hover:bg-gray-800 disabled:opacity-30">Next →</button>
            </div>
          </div>
        )}
      </div>
    </AdminShell>
  )
}
