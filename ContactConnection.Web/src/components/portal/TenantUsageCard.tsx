import { useEffect, useState } from 'react'
import { getTenantUsage, saveTenantBillingRates, type TenantUsage } from '../../api/portal'

const money = (n: number) => n.toLocaleString(undefined, { style: 'currency', currency: 'USD' })
const num = (n: number) => n.toLocaleString(undefined, { maximumFractionDigits: 2 })

function fmtNumber(n: string) {
  return n.length === 10 ? `(${n.slice(0, 3)}) ${n.slice(3, 6)}-${n.slice(6)}` : n
}

function thisMonth() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

/**
 * Billable carrier minutes for a month (S174 usage metering), charged at the tenant's saved rates (S175): every minute
 * at the base rate, toll-free minutes plus the surcharge, raised to the monthly minimum. Editing the rates previews a
 * "what if" price; Save makes them the tenant's rates.
 */
export default function TenantUsageCard({ tenantId }: { tenantId: string }) {
  const [month, setMonth] = useState(thisMonth())
  // null = use the tenant's saved rates; a number = previewing an edit
  const [rate, setRate] = useState<number | null>(null)
  const [surcharge, setSurcharge] = useState<number | null>(null)
  const [minimum, setMinimum] = useState<number | null>(null)
  const [usage, setUsage] = useState<TenantUsage | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saveMsg, setSaveMsg] = useState<string | null>(null)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    const t = setTimeout(() => {
      getTenantUsage(tenantId, {
        month, rate: rate ?? undefined, tollFreeSurcharge: surcharge ?? undefined, minimum: minimum ?? undefined,
      })
        .then((u) => { setUsage(u); setError(null) })
        .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load usage'))
    }, 300)
    return () => clearTimeout(t)
  }, [tenantId, month, rate, surcharge, minimum, reload])

  const shown = usage?.rates
  const saved = usage?.savedRates
  const notSaved = !!saved && saved.rate === null
  const edited = !!shown && !!saved && (shown.rate !== saved.rate || shown.tollFreeSurcharge !== saved.tollFreeSurcharge
    || shown.minimum !== saved.minimum)

  const save = () => {
    if (!shown) return
    saveTenantBillingRates(tenantId, shown)
      .then(() => { setRate(null); setSurcharge(null); setMinimum(null); setReload((n) => n + 1); setSaveMsg('Rates saved') })
      .catch((e) => setSaveMsg(e instanceof Error ? e.message : 'Save failed'))
  }

  const input = 'bg-gray-800 border border-gray-700 rounded-lg px-2 py-1 text-sm text-white w-28'
  const rows = usage ? [
    { label: 'Inbound — local numbers', line: usage.inboundLocal, price: usage.rates.rate },
    { label: 'Inbound — toll-free numbers', line: usage.inboundTollFree, price: usage.rates.rate + usage.rates.tollFreeSurcharge },
    { label: 'Outbound', line: usage.outbound, price: usage.rates.rate },
  ] : []

  return (
    <section className="bg-gray-900 rounded-xl border border-gray-800 p-5 mt-4">
      <h2 className="text-white text-sm font-semibold mb-1">Usage &amp; billing</h2>
      <p className="text-gray-500 text-xs mb-4">
        Billable carrier minutes: real callers to real numbers, and real numbers dialed. Internal calls and tests are not counted.
        Each call is billed in whole minutes, rounded up, the way the carrier bills. Month boundaries use the tenant's time zone{usage ? ` (${usage.timezone})` : ''}.
      </p>

      <div className="flex flex-wrap gap-4 mb-4 text-xs text-gray-400">
        <label className="flex flex-col gap-1">Month<input type="month" value={month} onChange={(e) => setMonth(e.target.value)} className={input} /></label>
        <label className="flex flex-col gap-1">Rate / min<input type="number" step="0.001" min="0" value={rate ?? shown?.rate ?? ''} onChange={(e) => { setRate(Number(e.target.value)); setSaveMsg(null) }} className={input} /></label>
        <label className="flex flex-col gap-1">Toll-free surcharge / min<input type="number" step="0.001" min="0" value={surcharge ?? shown?.tollFreeSurcharge ?? ''} onChange={(e) => { setSurcharge(Number(e.target.value)); setSaveMsg(null) }} className={input} /></label>
        <label className="flex flex-col gap-1">Monthly minimum<input type="number" step="100" min="0" value={minimum ?? shown?.minimum ?? ''} onChange={(e) => { setMinimum(Number(e.target.value)); setSaveMsg(null) }} className={input} /></label>
        <div className="flex items-end gap-2">
          {(edited || notSaved) && (
            <button onClick={save} className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-3 py-1.5 text-sm font-medium">
              Save as tenant rates
            </button>
          )}
          {edited && !notSaved && (
            <button onClick={() => { setRate(null); setSurcharge(null); setMinimum(null); setSaveMsg(null) }}
              className="text-gray-400 hover:text-white text-sm px-2 py-1.5">Reset</button>
          )}
        </div>
      </div>
      <p className="text-xs mb-4 -mt-2">
        {saveMsg ? <span className="text-emerald-400">{saveMsg}</span>
          : notSaved ? <span className="text-amber-400">No rates saved for this tenant yet: showing platform defaults.</span>
          : edited ? <span className="text-amber-400">Previewing unsaved rates.</span>
          : <span className="text-gray-500">Tenant's saved rates.</span>}
      </p>

      {error && <p className="text-red-400 text-sm mb-3">{error}</p>}

      {usage && (
        <>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-gray-500 text-xs text-left">
                  <th className="py-1 font-medium">Type</th>
                  <th className="py-1 font-medium text-right">Calls</th>
                  <th className="py-1 font-medium text-right">Actual min</th>
                  <th className="py-1 font-medium text-right">Billed min</th>
                  <th className="py-1 font-medium text-right">Price / min</th>
                  <th className="py-1 font-medium text-right">Amount</th>
                </tr>
              </thead>
              <tbody className="text-gray-300">
                {rows.map((r) => (
                  <tr key={r.label} className="border-t border-gray-800">
                    <td className="py-1.5">{r.label}</td>
                    <td className="py-1.5 text-right">{r.line.calls}</td>
                    <td className="py-1.5 text-right text-gray-500">{num(r.line.minutes)}</td>
                    <td className="py-1.5 text-right">{r.line.billedMinutes}</td>
                    <td className="py-1.5 text-right">${r.price.toFixed(3)}</td>
                    <td className="py-1.5 text-right">{money(r.line.billedMinutes * r.price)}</td>
                  </tr>
                ))}
                <tr className="border-t border-gray-700 text-white">
                  <td className="py-1.5 font-medium">Usage</td>
                  <td />
                  <td className="py-1.5 text-right text-gray-500">{num(usage.totalMinutes)}</td>
                  <td className="py-1.5 text-right">{usage.totalBilledMinutes}</td>
                  <td />
                  <td className="py-1.5 text-right">{money(usage.charges.usage)}</td>
                </tr>
                {usage.charges.minimum > 0 && (
                  <tr className="text-gray-400">
                    <td className="py-1">Monthly minimum</td><td /><td /><td /><td />
                    <td className="py-1 text-right">{money(usage.charges.minimum)}</td>
                  </tr>
                )}
                <tr className="text-white">
                  <td className="py-1.5 font-semibold">Amount due</td><td /><td /><td /><td />
                  <td className="py-1.5 text-right font-semibold">{money(usage.charges.total)}</td>
                </tr>
              </tbody>
            </table>
          </div>

          {usage.needsReview > 0 && (
            <p className="text-amber-400 text-xs mt-3">
              {usage.needsReview} call{usage.needsReview === 1 ? ' was' : 's were'} closed without a recorded hang-up (system
              cleanup), so the real length is unknown. Not billed: check them in Call Records.
            </p>
          )}

          {(usage.unended > 0 || usage.internal > 0) && (
            <p className="text-gray-500 text-xs mt-3">
              Not billed: {usage.internal} internal/test call{usage.internal === 1 ? '' : 's'}
              {usage.unended > 0 && `, ${usage.unended} call${usage.unended === 1 ? '' : 's'} with no end time (still live or never closed)`}.
            </p>
          )}

          {usage.byNumber.length > 0 && (
            <details className="mt-3">
              <summary className="text-gray-400 text-xs cursor-pointer">Inbound by number ({usage.byNumber.length})</summary>
              <table className="w-full text-xs mt-2 text-gray-300">
                <tbody>
                  {usage.byNumber.map((n) => (
                    <tr key={n.number} className="border-t border-gray-800">
                      <td className="py-1">{fmtNumber(n.number)}{n.tollFree && <span className="ml-2 text-amber-400">toll-free</span>}</td>
                      <td className="py-1 text-right">{n.calls} calls</td>
                      <td className="py-1 text-right">{n.billedMinutes} billed min ({num(n.minutes)} actual)</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </details>
          )}
        </>
      )}
    </section>
  )
}
