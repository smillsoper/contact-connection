import { useCallback, useEffect, useRef, useState } from 'react'
import { dashboardWidgetsApi, type QueuedCallRow } from '../../../api/dashboardWidgets'
import { listAdminAgents, type AgentRecord } from '../../../api/adminAgents'
import { addGroupMember, assignCampaignAgent } from '../../../api/telephony'
import SearchableSelect from '../../SearchableSelect'
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
  if (r.none_logged_in) return <span className="text-red-400 font-semibold">no agents logged in</span>
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

  // One alert per campaign (and pinned group) with calls waiting that nobody logged in can take.
  const alerts = new Map<string, { campaignId: string; campaignName: string; groupId: string | null; groupName: string | null; count: number }>()
  for (const r of rows.filter((x) => x.none_logged_in && !x.in_menu)) {
    const key = `${r.campaign_id}|${r.pinned_group_id ?? ''}`
    const a = alerts.get(key)
    if (a) a.count++
    else alerts.set(key, { campaignId: r.campaign_id, campaignName: r.campaign_name, groupId: r.pinned_group_id, groupName: r.pinned_group, count: 1 })
  }

  return (
    <div className="h-full overflow-auto text-xs">
      {[...alerts.values()].map((a) => (
        <NobodyLoggedInAlert key={`${a.campaignId}|${a.groupId ?? ''}`} alert={a} onAssigned={load} />
      ))}
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

/**
 * "Calls are waiting and nobody who could take them is logged in" — instead of hanging up on the
 * caller (TMS's old behavior), the manager on duty assigns someone right here. For a call pinned
 * to a group (e.g. Elite) the agent is added to that group; otherwise to the campaign. The queue
 * engine picks the assignment up on its next 1-second poll and offers the call once they're Available.
 */
function NobodyLoggedInAlert({ alert, onAssigned }: {
  alert: { campaignId: string; campaignName: string; groupId: string | null; groupName: string | null; count: number }
  onAssigned: () => void
}) {
  const [open, setOpen] = useState(false)
  const [agents, setAgents] = useState<AgentRecord[] | null>(null)
  const [agentId, setAgentId] = useState('')
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)

  useEffect(() => {
    if (!open || agents) return
    listAdminAgents().then((a) => setAgents(a.filter((x) => x.isActive)))
      .catch(() => { setAgents([]); setMsg('You need the Agents permission to assign from here.') })
  }, [open, agents])

  async function assign() {
    setBusy(true); setMsg(null)
    try {
      if (alert.groupId) await addGroupMember(alert.groupId, agentId)
      else await assignCampaignAgent(alert.campaignId, agentId, 50)
      setMsg('Assigned — the call is offered as soon as they are Available.')
      setAgentId('')
      onAssigned()
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Assign failed.')
    } finally {
      setBusy(false)
    }
  }

  const target = alert.groupName ? `${alert.campaignName} · ${alert.groupName}` : alert.campaignName
  return (
    <div className="mb-2 rounded-lg border border-red-700 bg-red-950/60 px-2.5 py-2">
      <div className="flex items-center gap-2">
        <span className="h-2 w-2 rounded-full bg-red-500 animate-pulse shrink-0" />
        <span className="text-red-200 font-semibold flex-1">
          {alert.count} call{alert.count === 1 ? '' : 's'} waiting — no agent logged in for {target}
        </span>
        <button onClick={() => setOpen((v) => !v)} className="text-red-200 hover:text-white underline">
          {open ? 'Close' : 'Assign agent'}
        </button>
      </div>
      {open && (
        <div className="flex items-center gap-2 mt-2 flex-wrap">
          <SearchableSelect
            options={(agents ?? []).map((a) => ({ value: a.id, label: `${a.firstName} ${a.lastName}`.trim() || a.email, sublabel: a.email }))}
            value={agentId}
            onChange={setAgentId}
            placeholder={agents ? 'Select agent…' : 'Loading…'}
            className="w-56"
          />
          <button
            onClick={assign}
            disabled={!agentId || busy}
            className="bg-red-600 hover:bg-red-500 disabled:opacity-50 text-white rounded px-2.5 py-1 font-medium"
          >
            {busy ? 'Assigning…' : alert.groupId ? 'Add to group' : 'Assign to campaign'}
          </button>
        </div>
      )}
      {msg && <p className="text-red-200/80 mt-1">{msg}</p>}
    </div>
  )
}
