import { useState } from 'react'
import { flowsApi, type FlowSummary } from '../api/flows'
import type { Campaign } from '../api/telephony'

/**
 * A flow's home client/campaign (optional). "Shared" = no campaign — for sub-flows reused by several
 * campaigns; a sub-flow always runs with the calling call's campaign anyway. Live calls never take
 * their campaign from this; it scopes the flow and gives agent-portal previews a realistic campaign
 * (tax, order numbers, payment credentials, campaign-scoped custom fields).
 */
export default function FlowScopeSelect({ flow, campaigns, onChanged }: {
  flow: FlowSummary
  campaigns: Campaign[]
  onChanged: (updated: Pick<FlowSummary, 'client_id' | 'campaign_id'>) => void
}) {
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const clients = new Map<string, string>()
  for (const c of campaigns) if (c.client) clients.set(c.client.id, c.client.name)

  const value = flow.campaign_id ? `c:${flow.campaign_id}` : flow.client_id ? `k:${flow.client_id}` : ''

  async function change(v: string) {
    setSaving(true); setError(null)
    try {
      const req = v.startsWith('c:') ? { clientId: null, campaignId: v.slice(2) }
        : v.startsWith('k:') ? { clientId: v.slice(2), campaignId: null }
        : { clientId: null, campaignId: null }
      const res = await flowsApi.setScope(flow.id, req.clientId, req.campaignId)
      onChanged({ client_id: res.client_id, campaign_id: res.campaign_id })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="flex flex-col">
      <select
        value={value}
        disabled={saving}
        onChange={(e) => change(e.target.value)}
        title="Home campaign — used for previews; live calls always use the call's own campaign"
        className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-gray-200 max-w-[16rem] disabled:opacity-50"
      >
        <option value="">Shared (no campaign)</option>
        {[...clients.entries()].map(([id, name]) => (
          <optgroup key={id} label={name}>
            <option value={`k:${id}`}>{name} — all campaigns</option>
            {campaigns.filter((c) => c.client?.id === id).map((c) => (
              <option key={c.id} value={`c:${c.id}`}>{c.name}</option>
            ))}
          </optgroup>
        ))}
      </select>
      {error && <span className="text-[10px] text-red-400 mt-0.5">{error}</span>}
    </div>
  )
}
