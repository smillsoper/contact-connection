import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { invoiceLabel, invoicesApi, money, periodLabel, STATUS_STYLE, type InvoiceSummary } from '../../api/invoices'

function lastMonth() {
  const d = new Date()
  d.setDate(1)
  d.setMonth(d.getMonth() - 1)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

/** A tenant's invoices and credit notes (S179). The Worker drafts last month's invoice on the 1st; this also lets you draft
 *  one for any month, or a one-off (e.g. the setup fee). */
export default function TenantInvoicesCard({ tenantId }: { tenantId: string }) {
  const navigate = useNavigate()
  const [list, setList] = useState<InvoiceSummary[] | null>(null)
  const [month, setMonth] = useState(lastMonth())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    invoicesApi.list(tenantId).then(setList).catch((e: Error) => setError(e.message))
  }, [tenantId])

  async function create(kind: 'monthly' | 'standalone') {
    setBusy(true); setError(null)
    try {
      const inv = kind === 'monthly' ? await invoicesApi.createMonthly(tenantId, month) : await invoicesApi.createStandalone(tenantId)
      navigate(`/portal/invoices/${inv.id}`)
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not create the draft.') } finally { setBusy(false) }
  }

  return (
    <section className="bg-gray-900 border border-gray-800 rounded-xl p-6">
      <h2 className="text-white text-sm font-semibold mb-1">Invoices</h2>
      <p className="text-gray-500 text-xs mb-4">
        Last month's draft is built automatically on the 1st (tenant's time zone) once billing rates are saved. Review, add any
        adjustments, then Issue — that numbers it and emails the billing contact.
      </p>

      <div className="flex flex-wrap items-center gap-2 mb-4">
        <input type="month" value={month} onChange={(e) => setMonth(e.target.value)}
          className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500" />
        <button onClick={() => void create('monthly')} disabled={busy || !month}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-3 py-1.5 text-sm">
          Draft for this month
        </button>
        <button onClick={() => void create('standalone')} disabled={busy}
          className="border border-gray-700 hover:bg-gray-800 text-gray-200 rounded-lg px-3 py-1.5 text-sm">
          One-off draft (e.g. setup fee)
        </button>
        {error && <span className="text-red-400 text-xs">{error}</span>}
      </div>

      {list === null && !error && <p className="text-gray-500 text-sm">Loading…</p>}
      {list?.length === 0 && <p className="text-gray-500 text-sm">No invoices yet.</p>}
      {list && list.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-gray-500 text-xs border-b border-gray-800">
                <th className="py-2 pr-4 font-medium">Number</th>
                <th className="py-2 pr-4 font-medium">Period</th>
                <th className="py-2 pr-4 font-medium">Status</th>
                <th className="py-2 pr-4 font-medium text-right">Total</th>
                <th className="py-2 font-medium">Due</th>
              </tr>
            </thead>
            <tbody>
              {list.map((i) => (
                <tr key={i.id} className="border-b border-gray-800/60 hover:bg-gray-800/40">
                  <td className="py-2 pr-4">
                    <Link to={`/portal/invoices/${i.id}`} className="text-indigo-400 hover:text-indigo-300 font-mono text-xs">
                      {invoiceLabel(i)}
                    </Link>
                    {i.kind === 'credit_note' && <span className="ml-2 text-[10px] text-gray-500">credit note</span>}
                  </td>
                  <td className="py-2 pr-4 text-gray-300">{periodLabel(i)}</td>
                  <td className="py-2 pr-4">
                    <span className={`text-[11px] px-1.5 py-0.5 rounded border ${STATUS_STYLE[i.status]}`}>{i.status}</span>
                  </td>
                  <td className="py-2 pr-4 text-right text-gray-200 font-mono">{money(i.total)}</td>
                  <td className="py-2 text-gray-400 text-xs">{i.dueOn ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
