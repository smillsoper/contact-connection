import { useEffect, useState } from 'react'
import { mediaApi, type MediaReplayBatch, type MediaReplayPreview, type ReplayNumber } from '../../api/media'

const inputCls = 'w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm'
const when = (local: string) => new Date(local).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })

/**
 * Replay media attribution (S171): re-attribute past calls as if they'd arrived under the assignments
 * as they are now set up — for assignments that arrived late or were corrected after the fact. Preview
 * first; the Worker applies it as a batch, and each changed call keeps its previous attribution in its
 * change history.
 */
/** <paramref name="fixedNumberId"/>: shown inside a number's Media dialog — replays just that number. */
export default function MediaReplayCard({ phoneNumberId: fixedNumberId }: { phoneNumberId?: string } = {}) {
  const [numbers, setNumbers] = useState<ReplayNumber[]>([])
  const [phoneNumberId, setPhoneNumberId] = useState(fixedNumberId ?? '')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState(() => new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 16))
  const [reason, setReason] = useState('')
  const [preview, setPreview] = useState<MediaReplayPreview | null>(null)
  const [batches, setBatches] = useState<MediaReplayBatch[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const loadBatches = () => mediaApi.replays().then(setBatches).catch(() => {})
  const shown = fixedNumberId ? batches.filter((b) => b.scope === 'All numbers' || numbers.length === 0 || b.scope === numbers.find((n) => n.id === fixedNumberId)?.number) : batches
  useEffect(() => {
    mediaApi.replayNumbers().then(setNumbers).catch(() => {})
    loadBatches()
  }, [])
  useEffect(() => { setPreview(null) }, [phoneNumberId, from, to])

  const active = batches.some((b) => b.status === 'pending' || b.status === 'running')
  useEffect(() => {
    if (!active) return
    const t = setInterval(loadBatches, 2000)
    return () => clearInterval(t)
  }, [active])

  const input = () => ({ phoneNumberId: phoneNumberId || null, from, to, reason })

  async function run(fn: () => Promise<void>) {
    setBusy(true); setError(null)
    try { await fn() } catch (e) { setError(e instanceof Error ? e.message : 'Failed.') } finally { setBusy(false) }
  }

  return (
    <div className="bg-gray-800 border border-gray-700 rounded-xl p-4 space-y-3 mt-6">
      <div>
        <h2 className="text-white font-medium">Replay attribution onto past calls</h2>
        <p className="text-xs text-gray-400 mt-1">
          Re-attributes calls that already happened as if they arrived under the assignments as they're set up now — for an
          assignment that came in late or a station corrected after the fact. Each call uses the assignments in effect on its date
          and the caller location it already has (captured zip, else area code). Preview shows what would change first.
        </p>
      </div>
      <div className={`grid grid-cols-1 gap-3 ${fixedNumberId ? 'sm:grid-cols-2' : 'sm:grid-cols-3'}`}>
        {!fixedNumberId && <label className="block">
          <span className="block text-xs text-gray-400 mb-1">Number</span>
          <select value={phoneNumberId} onChange={(e) => setPhoneNumberId(e.target.value)} className={inputCls}>
            <option value="">All numbers with assignments</option>
            {numbers.map((n) => <option key={n.id} value={n.id}>{n.clientNumber ?? n.number}{n.label ? ` — ${n.label}` : ''}</option>)}
          </select>
        </label>}
        <label className="block">
          <span className="block text-xs text-gray-400 mb-1">Calls that started from</span>
          <input type="datetime-local" value={from} onChange={(e) => setFrom(e.target.value)} className={inputCls} />
        </label>
        <label className="block">
          <span className="block text-xs text-gray-400 mb-1">Until</span>
          <input type="datetime-local" value={to} onChange={(e) => setTo(e.target.value)} className={inputCls} />
        </label>
      </div>
      <button disabled={busy || !from || !to} onClick={() => run(async () => setPreview(await mediaApi.previewReplay(input())))}
        className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded-lg disabled:opacity-50">
        {busy && !preview ? 'Checking…' : 'Preview'}
      </button>

      {preview && (
        <div className="border border-gray-700 rounded-lg p-3 space-y-3">
          <p className="text-sm text-gray-300">
            {preview.calls.toLocaleString()} call{preview.calls === 1 ? '' : 's'} in the window —{' '}
            <span className="text-white font-medium">{preview.changedCalls.toLocaleString()}</span> would change.
          </p>
          {preview.changes.length > 0 && (
            <table className="w-full text-xs">
              <thead>
                <tr className="text-left text-gray-500 border-b border-gray-700">
                  <th className="py-1 pr-3 font-medium">Now</th>
                  <th className="py-1 pr-3 font-medium">Becomes</th>
                  <th className="py-1 pr-3 font-medium text-right">Calls</th>
                </tr>
              </thead>
              <tbody>
                {preview.changes.map((c, i) => (
                  <tr key={i} className="border-b border-gray-800">
                    <td className="py-1 pr-3 text-gray-400">{c.from}</td>
                    <td className="py-1 pr-3 text-gray-200">{c.to}</td>
                    <td className="py-1 pr-3 text-right text-gray-300">{c.calls}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {preview.changedCalls > 0 && (
            <div className="space-y-2">
              <input value={reason} onChange={(e) => setReason(e.target.value)} className={inputCls}
                placeholder="Reason (noted on every call that changes) — e.g. Cannella sent the Sep 28 schedule late" />
              <button disabled={busy || !reason.trim() || active}
                onClick={() => run(async () => { await mediaApi.startReplay(input()); setPreview(null); setReason(''); await loadBatches() })}
                className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg disabled:opacity-50">
                Apply to {preview.changedCalls.toLocaleString()} call{preview.changedCalls === 1 ? '' : 's'}
              </button>
              {active && <p className="text-xs text-amber-300">Another replay is still running.</p>}
            </div>
          )}
        </div>
      )}
      {error && <p className="text-red-400 text-sm">{error}</p>}

      {shown.length > 0 && (
        <div>
          <p className="text-xs font-medium text-gray-400 mb-1">Recent replays</p>
          <ul className="divide-y divide-gray-700 text-xs">
            {shown.map((b) => (
              <li key={b.id} className="py-1.5">
                <div className="flex justify-between gap-3">
                  <span className="text-gray-200">{b.scope} · {when(b.from)} – {when(b.to)}</span>
                  <span className={b.status === 'failed' ? 'text-red-400' : b.status === 'completed' ? 'text-emerald-400' : 'text-amber-300'}>
                    {b.status === 'running' ? `Running ${b.processedCalls}/${b.totalCalls}` : b.status}
                  </span>
                </div>
                <div className="text-gray-500">
                  {b.reason}
                  {b.status === 'completed' && <> · {b.changedCalls} of {b.totalCalls} calls changed</>}
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
