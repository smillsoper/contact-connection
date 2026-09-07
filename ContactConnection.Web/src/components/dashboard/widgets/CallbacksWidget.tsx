import { useCallback, useEffect, useRef, useState } from 'react'
import { dashboardWidgetsApi, type PendingQueueCallbackRow } from '../../../api/dashboardWidgets'
import { scheduledCallbacksApi, type ScheduledCallback } from '../../../api/scheduledCallbacks'
import type { WidgetFilterConfig } from '../../../types/dashboard'
import { useDashboardLiveAgentState, useDashboardLiveCallState } from '../DashboardLiveContext'

function ago(iso: string | null): string {
  if (!iso) return '—'
  const s = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 1000))
  const m = Math.floor(s / 60)
  return m > 0 ? `${m}m ${s % 60}s` : `${s}s`
}

function until(iso: string | null): string {
  if (!iso) return '—'
  const s = Math.floor((new Date(iso).getTime() - Date.now()) / 1000)
  if (s <= 0) return 'due'
  const m = Math.floor(s / 60)
  const h = Math.floor(m / 60)
  if (h > 0) return `${h}h ${m % 60}m`
  return m > 0 ? `${m}m` : `${s}s`
}

export default function CallbacksWidget({ config }: { config: WidgetFilterConfig }) {
  const [queued, setQueued] = useState<PendingQueueCallbackRow[]>([])
  const [scheduled, setScheduled] = useState<ScheduledCallback[]>([])
  const [error, setError] = useState<string | null>(null)
  const [, setTick] = useState(0)
  const [cancelling, setCancelling] = useState<Set<string>>(new Set())
  const liveEvent = useDashboardLiveAgentState()
  const liveCall = useDashboardLiveCallState()
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const load = useCallback(() => {
    Promise.all([
      dashboardWidgetsApi.pendingQueueCallbacks(config),
      scheduledCallbacksApi.list({
        status: 'scheduled',
        campaignId: config.campaignId,
        clientId: config.clientId,
        limit: 100,
      }),
    ])
      .then(([q, s]) => { setQueued(q); setScheduled(s); setError(null) })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.campaignId, config.clientId])

  useEffect(() => { load() }, [load])

  // Ticking "waiting"/"due in" columns without refetching.
  useEffect(() => {
    const id = setInterval(() => setTick((t) => t + 1), 1000)
    return () => clearInterval(id)
  }, [])

  // Callback lifecycle shows up as agent-state (→ callback_pending) and call-state pushes on
  // this dashboard's SignalR feed — debounce-refetch on either rather than interval-polling.
  useEffect(() => {
    if (!liveEvent && !liveCall) return
    if (debounceRef.current) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(load, 400)
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current) }
  }, [liveEvent, liveCall, load])

  async function handleCancel(id: string) {
    setCancelling((prev) => new Set(prev).add(id))
    try {
      await scheduledCallbacksApi.cancel(id)
      setScheduled((prev) => prev.filter((c) => c.id !== id))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Cancel failed')
    } finally {
      setCancelling((prev) => { const n = new Set(prev); n.delete(id); return n })
    }
  }

  if (error) return <div className="text-xs text-red-400">{error}</div>

  return (
    <div className="h-full overflow-auto text-xs space-y-3">
      <section>
        <h4 className="text-[11px] font-semibold uppercase tracking-wide text-gray-400 mb-1">
          In queue for callback ({queued.length})
        </h4>
        {queued.length === 0 ? (
          <p className="text-gray-600 py-1">None waiting.</p>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="text-left text-gray-500 border-b border-gray-800">
                <th className="py-1 pr-2 font-medium">Caller</th>
                <th className="py-1 pr-2 font-medium">Waiting</th>
                <th className="py-1 font-medium">Attempts</th>
              </tr>
            </thead>
            <tbody>
              {queued.map((r) => (
                <tr key={r.call_record_id} className="border-b border-gray-800/60 last:border-0">
                  <td className="py-1.5 pr-2 text-gray-200">
                    {r.caller_number || r.callback_number || '—'}
                    {r.reserved_agent_id && (
                      <span className="ml-1.5 text-sky-400 text-[10px] uppercase">dialing</span>
                    )}
                  </td>
                  <td className="py-1.5 pr-2 text-gray-400">{ago(r.queued_since)}</td>
                  <td className="py-1.5 text-gray-400">
                    {r.attempts}{r.max_attempts ? ` / ${r.max_attempts}` : ''}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section>
        <h4 className="text-[11px] font-semibold uppercase tracking-wide text-gray-400 mb-1">
          Scheduled ({scheduled.length})
        </h4>
        {scheduled.length === 0 ? (
          <p className="text-gray-600 py-1">None scheduled.</p>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="text-left text-gray-500 border-b border-gray-800">
                <th className="py-1 pr-2 font-medium">Number</th>
                <th className="py-1 pr-2 font-medium">Due in</th>
                <th className="py-1 pr-2 font-medium">Att.</th>
                <th className="py-1 font-medium"></th>
              </tr>
            </thead>
            <tbody>
              {scheduled.map((c) => (
                <tr key={c.id} className="border-b border-gray-800/60 last:border-0">
                  <td className="py-1.5 pr-2 text-gray-200">{c.callbackNumber}</td>
                  <td className="py-1.5 pr-2 text-gray-400" title={new Date(c.scheduledFor).toLocaleString()}>
                    {until(c.scheduledFor)}
                  </td>
                  <td className="py-1.5 pr-2 text-gray-400">{c.attemptCount}/{c.maxAttempts}</td>
                  <td className="py-1.5 text-right">
                    <button
                      onClick={() => handleCancel(c.id)}
                      disabled={cancelling.has(c.id)}
                      className="text-[11px] text-red-400 hover:text-red-300 disabled:opacity-40"
                    >
                      {cancelling.has(c.id) ? '…' : 'Cancel'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  )
}
