import { useCallback, useEffect, useRef, useState } from 'react'
import { dashboardWidgetsApi, type QueuedCallRow } from '../../../api/dashboardWidgets'
import type { WidgetFilterConfig } from '../../../types/dashboard'
import { useDashboardLiveCallState, useDashboardLiveQueueOffer } from '../DashboardLiveContext'

function ago(iso: string | null): string {
  if (!iso) return '—'
  const s = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 1000))
  const m = Math.floor(s / 60)
  return m > 0 ? `${m}m ${s % 60}s` : `${s}s`
}

/** Who a queued call is being offered to right now — parallel queuing (docs/design/parallel-queuing.md). */
function OfferCell({ r }: { r: QueuedCallRow }) {
  if (r.in_menu) return <span className="text-gray-500">in menu</span>
  if (r.held_for_tier != null && r.offer_tier == null)
    return <span className="text-amber-400">held for tier {r.held_for_tier}</span>
  if (r.offer_tier == null) return <span className="text-gray-500">no agent available</span>
  return (
    <span className="text-gray-300">
      {r.offer_labels
        ? <span className="px-1.5 rounded-full bg-fuchsia-600/30 text-fuchsia-300 font-semibold uppercase text-[10px] mr-1">{r.offer_labels}</span>
        : <span className="mr-1">{r.offer_tier === 0 ? 'Regular' : `Tier ${r.offer_tier}`}</span>}
      <span className="text-gray-500">· {r.offered_agents} agent{r.offered_agents === 1 ? '' : 's'}</span>
    </span>
  )
}

export default function QueuedCallsWidget({ config }: { config: WidgetFilterConfig }) {
  const [rows, setRows] = useState<QueuedCallRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [, setTick] = useState(0)
  const liveCall = useDashboardLiveCallState()
  const liveOffer = useDashboardLiveQueueOffer()
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const load = useCallback(() => {
    dashboardWidgetsApi.queuedCalls(config)
      .then((r) => { setRows(r); setError(null) })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.campaignId, config.clientId])

  useEffect(() => { load() }, [load])

  // Ticking "waiting" column without refetching.
  useEffect(() => {
    const id = setInterval(() => setTick((t) => t + 1), 1000)
    return () => clearInterval(id)
  }, [])

  // Refetch on push: call-state transitions (a call entering/leaving the queue) and the queue
  // engine's offer-tier changes (ReceiveQueueOfferChanged). Debounced together.
  useEffect(() => {
    if (!liveCall && !liveOffer) return
    if (debounceRef.current) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(load, 400)
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current) }
  }, [liveCall, liveOffer, load])

  if (error) return <div className="text-xs text-red-400">{error}</div>

  return (
    <div className="h-full overflow-auto text-xs">
      {rows.length === 0 ? (
        <p className="text-gray-600 py-1">No calls in queue.</p>
      ) : (
        <table className="w-full">
          <thead>
            <tr className="text-left text-gray-500 border-b border-gray-800">
              <th className="py-1 pr-2 font-medium">Caller</th>
              <th className="py-1 pr-2 font-medium">Campaign</th>
              <th className="py-1 pr-2 font-medium">Waiting</th>
              <th className="py-1 font-medium">Offered to</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.call_record_id} className="border-b border-gray-800/60 last:border-0">
                <td className="py-1.5 pr-2 text-gray-200">
                  {r.caller_number || '—'}
                  {r.is_queue_callback && <span className="ml-1.5 text-sky-400 text-[10px] uppercase">callback</span>}
                </td>
                <td className="py-1.5 pr-2 text-gray-400">
                  {r.campaign_name}
                  {r.pinned_group && <span className="ml-1 text-amber-300">· {r.pinned_group} only</span>}
                </td>
                <td className="py-1.5 pr-2 text-gray-400">{ago(r.queued_since)}</td>
                <td className="py-1.5"><OfferCell r={r} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
