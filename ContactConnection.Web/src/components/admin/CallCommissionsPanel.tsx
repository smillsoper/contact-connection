import { useEffect, useState } from 'react'
import { commissionsApi, money, type CallCommissions } from '../../api/commissions'

/**
 * A call's commission entries on Call Records (S171), with reverse (e.g. the order was cancelled) and
 * restore for calls.manage. Reloads when the call changes (`version`), since a resubmit or a custom
 * field edit can recalculate it.
 */
export default function CallCommissionsPanel({ callId, canManage, version }: { callId: string; canManage: boolean; version: unknown }) {
  const [data, setData] = useState<CallCommissions | null>(null)
  const [reason, setReason] = useState('')
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = () => commissionsApi.forCall(callId).then(setData).catch(() => setData(null))
  useEffect(() => { load() }, [callId, version]) // eslint-disable-line react-hooks/exhaustive-deps

  async function act(fn: () => Promise<void>) {
    setBusy(true); setError(null)
    try { await fn(); setConfirming(false); setReason(''); await load() }
    catch (e) { setError(e instanceof Error ? e.message : 'Failed.') }
    finally { setBusy(false) }
  }

  if (!data || (data.entries.length === 0 && !data.commissionsReversedAt)) return null

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl px-4 py-3">
      <div className="flex items-center justify-between mb-2">
        <p className="text-xs font-semibold uppercase tracking-wide text-gray-500">
          Commission <span className="normal-case font-normal">— net {money(data.total)}</span>
        </p>
        {canManage && !data.commissionsReversedAt && data.entries.some((e) => e.entryType === 'earned' && !e.isReversed) && !confirming && (
          <button onClick={() => setConfirming(true)} className="text-xs text-gray-400 hover:text-red-400">Reverse commission…</button>
        )}
        {canManage && data.commissionsReversedAt && (
          <button disabled={busy} onClick={() => act(() => commissionsApi.restoreCall(callId))} className="text-xs text-indigo-400 hover:text-indigo-300 disabled:opacity-50">
            Restore commission
          </button>
        )}
      </div>
      {data.commissionsReversedAt && (
        <p className="text-xs text-amber-300 mb-2">
          Reversed{data.commissionsReversedReason ? `: ${data.commissionsReversedReason}` : ''} — this call earns nothing until restored.
        </p>
      )}
      {confirming && (
        <div className="flex flex-wrap items-center gap-2 mb-2">
          <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Reason (e.g. order cancelled)"
            className="flex-1 min-w-48 bg-gray-800 border border-gray-600 rounded-lg px-3 py-1.5 text-white text-xs" />
          <button disabled={busy} onClick={() => act(() => commissionsApi.reverseCall(callId, reason))}
            className="px-3 py-1.5 bg-red-700 hover:bg-red-600 text-white text-xs rounded-lg disabled:opacity-50">Reverse</button>
          <button onClick={() => setConfirming(false)} className="text-xs text-gray-400 hover:text-white">Cancel</button>
        </div>
      )}
      {error && <p className="text-red-400 text-xs mb-2">{error}</p>}
      <ul className="divide-y divide-gray-800">
        {data.entries.map((e) => (
          <li key={e.id} className="py-1.5 flex items-start justify-between gap-3 text-xs">
            <span className="min-w-0">
              <span className={e.isReversed ? 'text-gray-500 line-through' : 'text-gray-300'}>{e.ruleName}</span>
              <span className="text-gray-500"> · {e.agentName} · {new Date(e.occurredAt).toLocaleString()}</span>
              <span className="block text-gray-500">
                {e.description}{e.entryType === 'reversal' ? ` — reversed${e.note ? `: ${e.note}` : ''}` : ''}
              </span>
            </span>
            <span className={`whitespace-nowrap ${e.amount < 0 ? 'text-red-400' : e.isReversed ? 'text-gray-500' : 'text-emerald-400'}`}>{money(e.amount)}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}
