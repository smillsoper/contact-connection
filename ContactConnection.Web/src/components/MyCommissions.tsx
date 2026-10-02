import { useEffect, useRef, useState } from 'react'
import { commissionsApi, money, type MyCommissions as Mine } from '../api/commissions'
import { useAgentStateStore } from '../stores/agentStateStore'

/**
 * The agent's own commission (S171) — top-bar total for the current pay period, with today's and a
 * per-call breakdown (current or previous period) on click. Refreshes when opened and whenever the
 * agent's state changes (a call wrapping up is when commission is recorded).
 */
export default function MyCommissions() {
  const [period, setPeriod] = useState<'current' | 'previous'>('current')
  const [data, setData] = useState<Mine | null>(null)
  const [current, setCurrent] = useState<Mine | null>(null)
  const [open, setOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const agentState = useAgentStateStore((s) => s.agentStateCode)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const t = setTimeout(() => commissionsApi.mine('current').then(setCurrent).catch(() => {}), 1500)
    return () => clearTimeout(t)
  }, [agentState])

  useEffect(() => {
    if (!open) return
    setData(null); setError(null)
    commissionsApi.mine(period)
      .then((r) => { setData(r); if (period === 'current') setCurrent(r) })
      .catch((e: Error) => setError(e.message || 'Could not load commissions.'))
  }, [open, period])

  useEffect(() => {
    if (!open) return
    const close = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [open])

  return (
    <div ref={ref} className="relative">
      <button onClick={() => setOpen((v) => !v)} className="text-xs text-gray-400 hover:text-emerald-300 transition-colors">
        Commission: <span className="text-emerald-400 font-medium">{current ? money(current.total) : '—'}</span>
      </button>
      {open && (
        <div className="absolute right-0 top-full mt-2 w-96 bg-gray-900 border border-gray-700 rounded-xl shadow-2xl z-50 p-4">
          <div className="flex rounded-lg overflow-hidden border border-gray-700 mb-3">
            {(['current', 'previous'] as const).map((p) => (
              <button key={p} onClick={() => setPeriod(p)}
                className={`flex-1 px-3 py-1.5 text-xs ${period === p ? 'bg-indigo-600 text-white' : 'bg-gray-800 text-gray-300 hover:bg-gray-700'}`}>
                {p === 'current' ? 'This pay period' : 'Last pay period'}
              </button>
            ))}
          </div>
          {error ? <p className="text-xs text-red-400">{error}</p> : !data ? <p className="text-xs text-gray-400">Loading…</p> : (
            <>
              <p className="text-xs text-gray-500">{data.period.label}</p>
              <div className="flex items-baseline gap-4 mt-1 mb-3">
                <span className="text-2xl font-semibold text-white">{money(data.total)}</span>
                {period === 'current' && <span className="text-xs text-gray-400">Today {money(data.today)}</span>}
              </div>
              {data.entries.length === 0 ? <p className="text-xs text-gray-500 italic">No commissions yet this period.</p> : (
                <ul className="max-h-72 overflow-y-auto divide-y divide-gray-800">
                  {[...data.entries].reverse().map((e) => (
                    <li key={e.id} className="py-1.5 flex items-start justify-between gap-3 text-xs">
                      <span className="min-w-0">
                        <span className="text-gray-300">{e.date} · {e.campaign}{e.orderNumber ? ` · ${e.orderNumber}` : ''}</span>
                        <span className="block text-gray-500 truncate">
                          {e.description}{e.entryType === 'reversal' ? ` — reversed${e.note ? `: ${e.note}` : ''}` : ''}
                        </span>
                      </span>
                      <span className={`whitespace-nowrap ${e.amount < 0 ? 'text-red-400' : 'text-emerald-400'}`}>{money(e.amount)}</span>
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}
        </div>
      )}
    </div>
  )
}
