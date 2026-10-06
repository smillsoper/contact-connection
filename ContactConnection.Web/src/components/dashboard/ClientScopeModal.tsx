import { useEffect, useState } from 'react'
import { listClients, listCampaigns, type Campaign } from '../../api/telephony'
import { CLIENT_WIDGET_TYPES, WIDGET_META, type DashboardWidgetInstance } from '../../types/dashboard'

export interface ClientScope {
  isClientDashboard: boolean
  scopeClientId: string | null
  scopeCampaignIds: string[]
}

/**
 * Client dashboard settings (S181): who the dashboard is for. Client users only ever see data inside this scope — the
 * server enforces it whatever a widget's own filters say — and only report widgets can sit on a client dashboard.
 */
export default function ClientScopeModal({ initial, widgets, onSave, onClose }: {
  initial: ClientScope
  widgets: DashboardWidgetInstance[]
  onSave: (scope: ClientScope) => void
  onClose: () => void
}) {
  const [isClient, setIsClient] = useState(initial.isClientDashboard)
  const [clientId, setClientId] = useState(initial.scopeClientId ?? '')
  const [campaignIds, setCampaignIds] = useState<string[]>(initial.scopeCampaignIds)
  const [clients, setClients] = useState<{ id: string; name: string }[]>([])
  const [campaigns, setCampaigns] = useState<Campaign[]>([])

  useEffect(() => { listClients().then(setClients).catch(() => {}) }, [])
  useEffect(() => {
    if (!clientId) { setCampaigns([]); return }
    listCampaigns(clientId).then(setCampaigns).catch(() => setCampaigns([]))
  }, [clientId])

  const blocked = widgets.filter((w) => !CLIENT_WIDGET_TYPES.includes(w.widgetType))
  const canSave = !isClient || (!!clientId && blocked.length === 0)

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" onClick={onClose}>
      <div className="bg-gray-900 border border-gray-800 rounded-xl p-5 w-full max-w-md shadow-xl max-h-[90vh] flex flex-col" onClick={(e) => e.stopPropagation()}>
        <h3 className="text-sm font-semibold text-white mb-1">Client dashboard</h3>
        <p className="text-xs text-gray-500 mb-4">
          A client dashboard can be given to client users (your clients, or vendors such as a media agency) in the client portal.
          They only ever see data for the client and campaigns chosen here.
        </p>

        <div className="space-y-4 overflow-y-auto min-h-0 pr-1">
          <label className="flex items-center gap-2 text-sm text-gray-200">
            <input type="checkbox" checked={isClient} onChange={(e) => setIsClient(e.target.checked)} />
            This is a client dashboard
          </label>

          {isClient && (
            <>
              <div>
                <label className="block text-xs text-gray-400 mb-1">Client</label>
                <select value={clientId} onChange={(e) => { setClientId(e.target.value); setCampaignIds([]) }}
                  className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-white">
                  <option value="">Choose a client…</option>
                  {clients.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                </select>
              </div>
              {clientId && (
                <div>
                  <label className="block text-xs text-gray-400 mb-1">Campaigns</label>
                  <label className="flex items-center gap-2 text-xs text-gray-300 mb-1">
                    <input type="checkbox" checked={campaignIds.length === 0} onChange={() => setCampaignIds([])} />
                    All of this client's campaigns (including ones added later)
                  </label>
                  <div className="border border-gray-800 rounded p-2 space-y-1 max-h-48 overflow-y-auto">
                    {campaigns.length === 0 && <p className="text-xs text-gray-600">This client has no campaigns.</p>}
                    {campaigns.map((c) => (
                      <label key={c.id} className="flex items-center gap-2 text-xs text-gray-300">
                        <input type="checkbox" checked={campaignIds.includes(c.id)}
                          onChange={(e) => setCampaignIds(e.target.checked ? [...campaignIds, c.id] : campaignIds.filter((x) => x !== c.id))} />
                        {c.name}
                      </label>
                    ))}
                  </div>
                </div>
              )}
              <p className="text-[11px] text-gray-500">
                Allowed widgets: {CLIENT_WIDGET_TYPES.map((t) => WIDGET_META[t].label).join(', ')}. Recordings are
                only playable by client users you allow, under Admin → Client users.
              </p>
              {blocked.length > 0 && (
                <p className="text-xs text-amber-300">
                  Remove these first — they're supervisor tools, not for clients: {[...new Set(blocked.map((w) => WIDGET_META[w.widgetType].label))].join(', ')}.
                </p>
              )}
            </>
          )}
        </div>

        <div className="flex justify-end gap-2 pt-4">
          <button onClick={onClose} className="text-sm text-gray-300 border border-gray-700 hover:border-gray-500 px-4 py-1.5 rounded-lg">Cancel</button>
          <button disabled={!canSave}
            onClick={() => { onSave({ isClientDashboard: isClient, scopeClientId: isClient ? clientId : null, scopeCampaignIds: isClient ? campaignIds : [] }); onClose() }}
            className="text-sm bg-blue-600 hover:bg-blue-700 text-white px-4 py-1.5 rounded-lg disabled:opacity-50">
            Apply
          </button>
        </div>
      </div>
    </div>
  )
}
