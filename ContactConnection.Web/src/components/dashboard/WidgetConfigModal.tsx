import { useEffect, useState } from 'react'
import SearchableSelect from '../SearchableSelect'
import { listClients, listCampaigns, listAgentGroups } from '../../api/telephony'
import type { WidgetFilterConfig, WidgetFilterFields } from '../../types/dashboard'

interface Props {
  title: string
  fields: WidgetFilterFields
  initial: WidgetFilterConfig
  onSave: (config: WidgetFilterConfig) => void
  onClose: () => void
}

// Exactly one of client/campaign/group scopes a widget at a time — picking one clears the
// others, since the backend's agent-set resolution only honors a single filter dimension.
export default function WidgetConfigModal({ title, fields, initial, onSave, onClose }: Props) {
  const [clients, setClients] = useState<{ id: string; name: string }[]>([])
  const [campaigns, setCampaigns] = useState<{ id: string; name: string }[]>([])
  const [groups, setGroups] = useState<{ id: string; name: string }[]>([])
  const [clientId, setClientId] = useState(initial.clientId ?? '')
  const [campaignId, setCampaignId] = useState(initial.campaignId ?? '')
  const [groupId, setGroupId] = useState(initial.groupId ?? '')
  const [loggedInOnly, setLoggedInOnly] = useState(initial.loggedInOnly ?? false)

  useEffect(() => {
    listClients().then(setClients).catch(() => {})
    listCampaigns().then(setCampaigns).catch(() => {})
    listAgentGroups().then(setGroups).catch(() => {})
  }, [])

  function handleSave() {
    onSave({
      clientId: clientId || undefined,
      campaignId: campaignId || undefined,
      groupId: groupId || undefined,
      loggedInOnly: loggedInOnly || undefined,
    })
    onClose()
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div
        className="bg-gray-900 border border-gray-800 rounded-xl p-5 w-96 shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="text-sm font-semibold text-white mb-1">{title}</h3>
        <p className="text-xs text-gray-500 mb-4">Filter which data this widget shows. Pick one — the most specific wins.</p>

        <div className="space-y-3">
          {fields.client && (
            <div>
              <label className="block text-xs text-gray-400 mb-1">Client</label>
              <SearchableSelect
                options={clients.map((c) => ({ value: c.id, label: c.name }))}
                value={clientId}
                onChange={(v) => { setClientId(v); if (v) { setCampaignId(''); setGroupId('') } }}
                allLabel="All clients"
                className="w-full"
              />
            </div>
          )}
          {fields.campaign && (
            <div>
              <label className="block text-xs text-gray-400 mb-1">Campaign</label>
              <SearchableSelect
                options={campaigns.map((c) => ({ value: c.id, label: c.name }))}
                value={campaignId}
                onChange={(v) => { setCampaignId(v); if (v) { setClientId(''); setGroupId('') } }}
                allLabel="All campaigns"
                className="w-full"
              />
            </div>
          )}
          {fields.group && (
            <div>
              <label className="block text-xs text-gray-400 mb-1">Agent Group</label>
              <SearchableSelect
                options={groups.map((g) => ({ value: g.id, label: g.name }))}
                value={groupId}
                onChange={(v) => { setGroupId(v); if (v) { setClientId(''); setCampaignId('') } }}
                allLabel="All groups"
                className="w-full"
              />
            </div>
          )}
          {fields.loggedInOnly && (
            <label className="flex items-center gap-2 text-sm text-gray-300 pt-1">
              <input
                type="checkbox"
                checked={loggedInOnly}
                onChange={(e) => setLoggedInOnly(e.target.checked)}
                className="rounded"
              />
              Logged in only
            </label>
          )}
        </div>

        <div className="flex justify-end gap-2 mt-5">
          <button
            onClick={onClose}
            className="text-sm text-gray-300 border border-gray-700 hover:border-gray-500 px-4 py-1.5 rounded-lg transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleSave}
            className="text-sm bg-blue-600 hover:bg-blue-700 text-white px-4 py-1.5 rounded-lg transition-colors"
          >
            Save
          </button>
        </div>
      </div>
    </div>
  )
}
