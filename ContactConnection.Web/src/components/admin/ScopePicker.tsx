import type { Client } from '../../api/telephony'

// Client → campaigns scope selector shared by the product and offer editors (S169). '' client =
// tenant-wide. Campaigns are a multi-select: none checked = all of the client's campaigns; checking
// some limits the item to those (e.g. NeuroQ offers on the NeuroQ scripts, not My Best Heart). Changing
// the client clears the campaigns — a campaign only means something under the client that owns it.

export function scopeLabel(clientId: string | null | undefined, campaignIds: string[] | null | undefined, clients: Client[]): string {
  if (!clientId) return 'Tenant-wide'
  const client = clients.find((c) => c.id === clientId)
  const clientName = client?.name ?? '(unknown)'
  if (!campaignIds?.length) return `Client: ${clientName} (all campaigns)`
  const names = campaignIds.map((id) => client?.campaigns.find((c) => c.id === id)?.name ?? '(unknown)')
  return `${clientName}: ${names.join(', ')}`
}

export default function ScopePicker({
  clients, clientId, campaignIds, onChange, help,
}: {
  clients: Client[]
  clientId: string
  campaignIds: string[]
  onChange: (clientId: string, campaignIds: string[]) => void
  help?: string
}) {
  const selectedClient = clients.find((c) => c.id === clientId)
  const toggle = (id: string) =>
    onChange(clientId, campaignIds.includes(id) ? campaignIds.filter((c) => c !== id) : [...campaignIds, id])

  return (
    <div>
      <label className="block text-sm font-medium text-gray-300 mb-1">Scope</label>
      <select
        value={clientId}
        onChange={(e) => onChange(e.target.value, [])}
        className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
      >
        <option value="">Tenant-wide</option>
        {clients.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
      </select>
      {selectedClient && (
        <div className="mt-2 bg-gray-800/60 border border-gray-700 rounded-lg p-2">
          <p className="text-xs text-gray-400 mb-1.5">
            Campaigns — {campaignIds.length === 0 ? 'none checked = all of this client’s campaigns' : `${campaignIds.length} selected`}
          </p>
          {selectedClient.campaigns.length === 0 ? (
            <p className="text-xs text-gray-500 italic">This client has no campaigns.</p>
          ) : (
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-1 max-h-40 overflow-y-auto">
              {selectedClient.campaigns.map((c) => (
                <label key={c.id} className="flex items-center gap-2 text-sm text-gray-200 cursor-pointer">
                  <input type="checkbox" checked={campaignIds.includes(c.id)} onChange={() => toggle(c.id)} className="accent-indigo-600" />
                  <span className="truncate">{c.name}</span>
                </label>
              ))}
            </div>
          )}
        </div>
      )}
      {help && <p className="text-xs text-gray-500 mt-1">{help}</p>}
    </div>
  )
}
