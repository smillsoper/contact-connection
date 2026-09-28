import { useEffect, useState } from 'react'
import SearchableSelect from '../SearchableSelect'
import {
  assignGroupToCampaign, getGroupMemberCampaigns, listCampaigns, removeGroupFromCampaign,
  setGroupCampaignProficiency, setGroupCampaignRouting, setGroupMemberCampaigns,
  type Campaign, type GroupMemberCampaigns,
} from '../../api/telephony'
import type { AgentRecord } from '../../api/adminAgents'

/**
 * Parallel queuing for one agent group (docs/design/parallel-queuing.md):
 *  - which campaigns the group takes, and at what routing tier (Alpha = a higher tier, offered a
 *    waiting call before the regular pool), optional exclusive window, and the label calls are
 *    tagged with for commissions;
 *  - per member, which of those campaigns they may take (unchecked = excluded).
 * `membersVersion` changes when members are added/removed so the grid reloads.
 */
export default function GroupRoutingPanel({ groupId, agents, membersVersion }: {
  groupId: string
  agents: AgentRecord[]
  membersVersion: number
}) {
  const [data, setData] = useState<GroupMemberCampaigns | null>(null)
  const [campaigns, setCampaigns] = useState<Campaign[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [newCampaignId, setNewCampaignId] = useState('')

  async function reload() {
    try { setData(await getGroupMemberCampaigns(groupId)) }
    catch (e) { setError(e instanceof Error ? e.message : 'Failed to load campaigns.') }
  }

  useEffect(() => { reload() }, [groupId, membersVersion]) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { listCampaigns().then(setCampaigns).catch(() => {}) }, [])

  async function run(key: string, fn: () => Promise<unknown>) {
    setBusy(key); setError(null)
    try { await fn(); await reload() }
    catch (e) { setError(e instanceof Error ? e.message : 'Save failed.') }
    finally { setBusy(null) }
  }

  if (!data) return error ? <p className="text-red-400 text-xs">{error}</p> : <p className="text-gray-500 text-xs">Loading routing…</p>

  const assignedIds = new Set(data.campaigns.map((c) => c.campaignId))
  const assignable = campaigns.filter((c) => !assignedIds.has(c.id))

  return (
    <div className="mt-5 space-y-5">
      {/* ── Campaigns & routing tier ── */}
      <div>
        <p className="text-gray-300 text-xs font-semibold uppercase tracking-wide mb-1">Campaigns &amp; routing tier</p>
        <p className="text-gray-500 text-xs mb-3">
          Tier 0 = regular pool. A higher tier (e.g. 10 for Alpha) is offered a waiting call first; the offer
          drops to the next tier only when nobody in the higher one is available. An exclusive window holds
          new calls for the tier even when lower tiers are free.
        </p>

        {data.campaigns.length > 0 && (
          <div className="overflow-x-auto">
            <table className="text-sm min-w-full">
              <thead>
                <tr className="text-gray-500 text-xs text-left">
                  <th className="font-medium pr-3 pb-1">Campaign</th>
                  <th className="font-medium pr-3 pb-1">Tier</th>
                  <th className="font-medium pr-3 pb-1">Label</th>
                  <th className="font-medium pr-3 pb-1">Exclusive window (s)</th>
                  <th className="font-medium pr-3 pb-1">Proficiency</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {data.campaigns.map((c) => (
                  <CampaignRoutingRow
                    key={c.campaignId}
                    row={c}
                    busy={busy === c.campaignId}
                    onSave={(routing, proficiency) => run(c.campaignId, async () => {
                      await setGroupCampaignRouting(c.campaignId, groupId, routing)
                      if (proficiency !== c.proficiency) await setGroupCampaignProficiency(c.campaignId, groupId, proficiency)
                    })}
                    onRemove={() => run(c.campaignId, () => removeGroupFromCampaign(c.campaignId, groupId))}
                  />
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="flex items-center gap-3 mt-3 flex-wrap">
          <SearchableSelect
            options={assignable.map((c) => ({ value: c.id, label: c.name, sublabel: c.client?.name }))}
            value={newCampaignId}
            onChange={setNewCampaignId}
            placeholder="Assign a campaign…"
            className="w-72"
          />
          <button
            onClick={() => run('assign', async () => {
              await assignGroupToCampaign(newCampaignId, groupId, 50, { routingTier: 0, exclusiveWindowSeconds: null, tierLabel: null })
              setNewCampaignId('')
            })}
            disabled={!newCampaignId || busy === 'assign'}
            className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-3 py-1.5 text-sm font-medium transition-colors"
          >
            {busy === 'assign' ? 'Assigning…' : 'Assign'}
          </button>
        </div>
      </div>

      {/* ── Per-member campaigns ── */}
      {data.campaigns.length > 0 && data.members.length > 0 && (
        <div>
          <p className="text-gray-300 text-xs font-semibold uppercase tracking-wide mb-1">Member campaigns</p>
          <p className="text-gray-500 text-xs mb-3">Uncheck a campaign a member shouldn't take through this group.</p>
          <div className="overflow-x-auto">
            <table className="text-sm">
              <thead>
                <tr className="text-gray-500 text-xs">
                  <th className="text-left font-medium pr-4 pb-1">Agent</th>
                  {data.campaigns.map((c) => (
                    <th key={c.campaignId} className="font-medium px-2 pb-1 whitespace-nowrap">{c.campaignName}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data.members.map((m) => {
                  const agent = agents.find((a) => a.id === m.agentId)
                  const allowed = new Set(m.allowedCampaignIds)
                  return (
                    <tr key={m.agentId}>
                      <td className="text-gray-200 pr-4 py-1 whitespace-nowrap">
                        {agent ? `${agent.firstName} ${agent.lastName}` : m.agentId}
                      </td>
                      {data.campaigns.map((c) => (
                        <td key={c.campaignId} className="text-center px-2 py-1">
                          <input
                            type="checkbox"
                            className="accent-indigo-500"
                            checked={allowed.has(c.campaignId)}
                            disabled={busy === `m:${m.agentId}`}
                            onChange={(e) => {
                              const next = new Set(allowed)
                              if (e.target.checked) next.add(c.campaignId); else next.delete(c.campaignId)
                              run(`m:${m.agentId}`, () => setGroupMemberCampaigns(groupId, m.agentId, [...next]))
                            }}
                          />
                        </td>
                      ))}
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {error && <p className="text-red-400 text-xs">{error}</p>}
    </div>
  )
}

function CampaignRoutingRow({ row, busy, onSave, onRemove }: {
  row: GroupMemberCampaigns['campaigns'][number]
  busy: boolean
  onSave: (routing: { routingTier: number; exclusiveWindowSeconds: number | null; tierLabel: string | null }, proficiency: number) => void
  onRemove: () => void
}) {
  const [tier, setTier] = useState(String(row.routingTier))
  const [label, setLabel] = useState(row.tierLabel ?? '')
  const [windowSecs, setWindowSecs] = useState(row.exclusiveWindowSeconds?.toString() ?? '')
  const [proficiency, setProficiency] = useState(String(row.proficiency))

  const tierNum = Number(tier)
  const dirty = tier !== String(row.routingTier) || label !== (row.tierLabel ?? '')
    || windowSecs !== (row.exclusiveWindowSeconds?.toString() ?? '') || proficiency !== String(row.proficiency)
  const input = 'bg-gray-800 text-white rounded px-2 py-1 text-sm outline-none focus:ring-2 focus:ring-indigo-500'

  return (
    <tr>
      <td className="text-gray-200 pr-3 py-1 whitespace-nowrap">{row.campaignName}</td>
      <td className="pr-3 py-1"><input type="number" min={0} max={100} value={tier} onChange={(e) => setTier(e.target.value)} className={`${input} w-16`} /></td>
      <td className="pr-3 py-1"><input value={label} onChange={(e) => setLabel(e.target.value)} placeholder="e.g. Alpha" maxLength={50} className={`${input} w-28`} /></td>
      <td className="pr-3 py-1">
        <input
          type="number" min={1} max={600} value={windowSecs}
          onChange={(e) => setWindowSecs(e.target.value)}
          disabled={tierNum <= 0}
          placeholder={tierNum > 0 ? 'none' : 'tier > 0'}
          className={`${input} w-24 disabled:opacity-40`}
        />
      </td>
      <td className="pr-3 py-1"><input type="number" min={1} max={100} value={proficiency} onChange={(e) => setProficiency(e.target.value)} className={`${input} w-16`} /></td>
      <td className="py-1 whitespace-nowrap">
        <button
          onClick={() => onSave({
            routingTier: tierNum,
            exclusiveWindowSeconds: tierNum > 0 && windowSecs ? Number(windowSecs) : null,
            tierLabel: label.trim() || null,
          }, Number(proficiency))}
          disabled={!dirty || busy}
          className="text-indigo-400 hover:text-indigo-300 disabled:opacity-40 text-xs mr-3"
        >
          {busy ? 'Saving…' : 'Save'}
        </button>
        <button onClick={onRemove} disabled={busy} className="text-red-500 hover:text-red-400 text-xs">Remove</button>
      </td>
    </tr>
  )
}
