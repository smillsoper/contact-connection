import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import AdminShell from '../components/admin/AdminShell'
import {
  commissionsApi, money,
  type CommissionEntryRow, type CommissionReport, type PeriodQuery,
} from '../api/commissions'

// Commission report (S171) — reports.view. Per-agent totals for a pay period (current, previous, or the
// period containing a picked date), drill into an agent's entries, and the payroll CSV.

function EntriesTable({ entries, showAgent }: { entries: CommissionEntryRow[]; showAgent: boolean }) {
  if (entries.length === 0) return <p className="text-gray-500 italic text-sm">No commission entries in this period.</p>
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left text-xs text-gray-500 border-b border-gray-700">
            <th className="py-1.5 pr-3 font-medium">Date</th>
            {showAgent && <th className="py-1.5 pr-3 font-medium">Agent</th>}
            <th className="py-1.5 pr-3 font-medium">Campaign</th>
            <th className="py-1.5 pr-3 font-medium">Order / call</th>
            <th className="py-1.5 pr-3 font-medium">Rule</th>
            <th className="py-1.5 pr-3 font-medium text-right">Amount</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((e) => (
            <tr key={e.id} className="border-b border-gray-800 align-top">
              <td className="py-1.5 pr-3 text-gray-400 whitespace-nowrap">{e.date}</td>
              {showAgent && <td className="py-1.5 pr-3 text-gray-200">{e.agentName}</td>}
              <td className="py-1.5 pr-3 text-gray-300">{e.client} · {e.campaign}</td>
              <td className="py-1.5 pr-3">
                <Link to={`/admin/calls/${e.callRecordId}`} className="text-indigo-400 hover:text-indigo-300">
                  {e.orderNumber ?? 'View call'}
                </Link>
              </td>
              <td className="py-1.5 pr-3 text-gray-300">
                {e.ruleName}
                <span className="block text-xs text-gray-500">
                  {e.description}{e.entryType === 'reversal' ? ` — reversed${e.note ? `: ${e.note}` : ''}` : ''}
                </span>
              </td>
              <td className={`py-1.5 pr-3 text-right whitespace-nowrap ${e.amount < 0 ? 'text-red-400' : 'text-emerald-400'}`}>{money(e.amount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export default function CommissionReportPage() {
  const [which, setWhich] = useState<'current' | 'previous' | 'date'>('current')
  const [date, setDate] = useState('')
  const [report, setReport] = useState<CommissionReport | null>(null)
  const [agentId, setAgentId] = useState<string | null>(null)
  const [entries, setEntries] = useState<CommissionEntryRow[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [exporting, setExporting] = useState(false)

  const query = (): PeriodQuery => which === 'date' && date ? { start: date } : { period: which === 'previous' ? 'previous' : 'current' }

  useEffect(() => {
    if (which === 'date' && !date) return
    setReport(null); setError(null); setAgentId(null); setEntries(null)
    commissionsApi.report(query()).then(setReport).catch((e: Error) => setError(e.message))
  }, [which, date]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!agentId) { setEntries(null); return }
    setEntries(null)
    commissionsApi.entries({ ...query(), agentId }).then((r) => setEntries(r.entries)).catch((e: Error) => setError(e.message))
  }, [agentId]) // eslint-disable-line react-hooks/exhaustive-deps

  async function exportCsv() {
    setExporting(true); setError(null)
    try { await commissionsApi.downloadCsv({ ...query(), ...(agentId ? { agentId } : {}) }) }
    catch (e) { setError(e instanceof Error ? e.message : 'Export failed.') }
    finally { setExporting(false) }
  }

  const selected = report?.agents.find((a) => a.agentId === agentId)

  return (
    <AdminShell>
      <div className="max-w-5xl mx-auto space-y-5">
        <div>
          <h1 className="text-2xl font-bold text-white">Commission Report</h1>
          <p className="text-sm text-gray-400 mt-1">
            Earnings per agent by pay period. Reversals (a cancelled order, a resubmitted cart) count in the period they happened.
          </p>
        </div>

        <div className="flex flex-wrap items-end gap-3">
          <div className="flex rounded-lg overflow-hidden border border-gray-700">
            {(['current', 'previous', 'date'] as const).map((w) => (
              <button key={w} onClick={() => setWhich(w)}
                className={`px-3 py-2 text-sm ${which === w ? 'bg-indigo-600 text-white' : 'bg-gray-800 text-gray-300 hover:bg-gray-700'}`}>
                {w === 'current' ? 'Current period' : w === 'previous' ? 'Previous period' : 'Pick a date'}
              </button>
            ))}
          </div>
          {which === 'date' && (
            <input type="date" value={date} onChange={(e) => setDate(e.target.value)}
              className="bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm" />
          )}
          <div className="flex-1" />
          <button onClick={exportCsv} disabled={!report || exporting}
            className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded-lg disabled:opacity-50">
            {exporting ? 'Exporting…' : `Export CSV${selected ? ` (${selected.agentName})` : ''}`}
          </button>
        </div>

        {error && <p className="text-red-400 text-sm">{error}</p>}

        {report && (
          <div className="bg-gray-800 border border-gray-700 rounded-xl p-4">
            <div className="flex items-baseline justify-between mb-3">
              <h2 className="text-white font-medium">{report.period.label}</h2>
              <span className="text-sm text-gray-400">Total <span className="text-white font-semibold">{money(report.total)}</span></span>
            </div>
            {report.agents.length === 0 ? <p className="text-gray-500 italic text-sm">No commissions in this period.</p> : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-gray-500 border-b border-gray-700">
                    <th className="py-1.5 pr-3 font-medium">Agent</th>
                    <th className="py-1.5 pr-3 font-medium text-right">Calls</th>
                    <th className="py-1.5 pr-3 font-medium text-right">Earned</th>
                    <th className="py-1.5 pr-3 font-medium text-right">Reversed</th>
                    <th className="py-1.5 pr-3 font-medium text-right">Net</th>
                  </tr>
                </thead>
                <tbody>
                  {report.agents.map((a) => (
                    <tr key={a.agentId} onClick={() => setAgentId(a.agentId === agentId ? null : a.agentId)}
                      className={`border-b border-gray-800 cursor-pointer hover:bg-gray-700/40 ${a.agentId === agentId ? 'bg-gray-700/60' : ''}`}>
                      <td className="py-1.5 pr-3 text-gray-200">{a.agentName}</td>
                      <td className="py-1.5 pr-3 text-right text-gray-300">{a.calls}</td>
                      <td className="py-1.5 pr-3 text-right text-gray-300">{money(a.earned)}</td>
                      <td className="py-1.5 pr-3 text-right text-red-400">{a.reversed ? money(a.reversed) : '—'}</td>
                      <td className="py-1.5 pr-3 text-right text-white font-medium">{money(a.total)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            <p className="text-[11px] text-gray-500 mt-2">Click an agent to see the calls behind their total.</p>
          </div>
        )}

        {selected && (
          <div className="bg-gray-800 border border-gray-700 rounded-xl p-4">
            <h2 className="text-white font-medium mb-3">{selected.agentName} — {report?.period.label}</h2>
            {!entries ? <p className="text-gray-400 text-sm">Loading…</p> : <EntriesTable entries={entries} showAgent={false} />}
          </div>
        )}
      </div>
    </AdminShell>
  )
}
