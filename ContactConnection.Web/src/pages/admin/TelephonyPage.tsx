import { useEffect, useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import SearchableSelect from '../../components/SearchableSelect'
import {
  listClients, createClient, activateClient, deactivateClient,
  listCampaigns, createCampaign, activateCampaign, pauseCampaign, deactivateCampaign,
  listPhoneNumbers, createPhoneNumber, activatePhoneNumber, deactivatePhoneNumber,
  listAgentGroups, createAgentGroup, getAgentGroup, addGroupMember, removeGroupMember,
  type Client, type Campaign, type PhoneNumber, type AgentGroup, type AgentGroupDetail,
} from '../../api/telephony'

type Tab = 'clients' | 'campaigns' | 'phone-numbers' | 'agent-groups'

const TABS: { id: Tab; label: string }[] = [
  { id: 'clients',       label: 'Clients' },
  { id: 'campaigns',     label: 'Campaigns' },
  { id: 'phone-numbers', label: 'Phone Numbers' },
  { id: 'agent-groups',  label: 'Agent Groups' },
]

const STATUS_COLORS: Record<string, string> = {
  active:   'bg-emerald-900/50 text-emerald-400',
  paused:   'bg-amber-900/50 text-amber-400',
  inactive: 'bg-gray-700/60 text-gray-400',
}

const DIRECTION_COLORS: Record<string, string> = {
  inbound:  'bg-blue-900/50 text-blue-400',
  outbound: 'bg-violet-900/50 text-violet-400',
}

// ── Clients Tab ───────────────────────────────────────────────────────────────

function ClientsTab() {
  const [clients, setClients] = useState<Client[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')

  const [showCreate, setShowCreate] = useState(false)
  const [newName, setNewName] = useState('')
  const [newAccount, setNewAccount] = useState('')
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  useEffect(() => {
    listClients()
      .then(setClients)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  async function handleCreate() {
    if (!newName.trim()) return
    setCreating(true)
    setCreateError(null)
    try {
      const c = await createClient(newName.trim(), newAccount.trim() || undefined)
      setClients((prev) => [...prev, c])
      setNewName(''); setNewAccount(''); setShowCreate(false)
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : 'Create failed.')
    } finally {
      setCreating(false)
    }
  }

  async function handleToggleActive(client: Client) {
    try {
      const updated = client.status === 'active'
        ? await deactivateClient(client.id)
        : await activateClient(client.id)
      setClients((prev) => prev.map((c) => c.id === client.id ? updated : c))
    } catch {}
  }

  const q = search.toLowerCase()
  const visible = clients.filter((c) =>
    c.name.toLowerCase().includes(q) ||
    (c.accountNumber ?? '').toLowerCase().includes(q)
  )

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-3">
          <p className="text-gray-500 text-sm">Clients are the organizations your campaigns serve.</p>
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search…"
            className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-48"
          />
        </div>
        <button
          onClick={() => { setShowCreate((v) => !v); setCreateError(null) }}
          className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
        >
          Add client
        </button>
      </div>

      {showCreate && (
        <div className="bg-gray-900 border border-gray-800 rounded-xl p-4 mb-4">
          <p className="text-gray-300 text-sm font-medium mb-3">New client</p>
          <div className="flex items-center gap-3 flex-wrap">
            <input
              autoFocus
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleCreate()}
              placeholder="Client name *"
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-56"
            />
            <input
              value={newAccount}
              onChange={(e) => setNewAccount(e.target.value)}
              placeholder="Account number (optional)"
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-56"
            />
            <button
              onClick={handleCreate}
              disabled={creating || !newName.trim()}
              className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
            >
              {creating ? 'Creating…' : 'Create'}
            </button>
            <button onClick={() => setShowCreate(false)} className="text-gray-500 hover:text-white text-sm">
              Cancel
            </button>
          </div>
          {createError && <p className="text-red-400 text-xs mt-2">{createError}</p>}
        </div>
      )}

      {loading && <p className="text-gray-400 text-sm">Loading…</p>}
      {error && <p className="text-red-400 text-sm">{error}</p>}
      {!loading && !error && clients.length === 0 && (
        <p className="text-gray-500 text-sm">No clients yet.</p>
      )}
      {!loading && !error && clients.length > 0 && visible.length === 0 && (
        <p className="text-gray-500 text-sm">No clients match "{search}".</p>
      )}

      {visible.length > 0 && (
        <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-800 text-gray-400 text-left">
                <th className="px-4 py-3 font-medium">Name</th>
                <th className="px-4 py-3 font-medium">Account #</th>
                <th className="px-4 py-3 font-medium">Campaigns</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3 font-medium"></th>
              </tr>
            </thead>
            <tbody>
              {visible.map((c) => (
                <tr key={c.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30">
                  <td className="px-4 py-3 text-white font-medium">{c.name}</td>
                  <td className="px-4 py-3 text-gray-400">{c.accountNumber ?? <span className="text-gray-600">—</span>}</td>
                  <td className="px-4 py-3 text-gray-400">{c.campaigns.length}</td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${STATUS_COLORS[c.status] ?? STATUS_COLORS.inactive}`}>
                      {c.status}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <button
                      onClick={() => handleToggleActive(c)}
                      className="text-indigo-400 hover:text-indigo-300 text-xs font-medium"
                    >
                      {c.status === 'active' ? 'Deactivate' : 'Activate'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

// ── Campaigns Tab ─────────────────────────────────────────────────────────────

function CampaignsTab() {
  const navigate = useNavigate()
  const [clients, setClients] = useState<Client[]>([])
  const [campaigns, setCampaigns] = useState<Campaign[]>([])
  const [filterClientId, setFilterClientId] = useState('')
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [showCreate, setShowCreate] = useState(false)
  const [newClientId, setNewClientId] = useState('')
  const [newName, setNewName] = useState('')
  const [newSlug, setNewSlug] = useState('')
  const [newDesc, setNewDesc] = useState('')
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([listClients(), listCampaigns()])
      .then(([cl, ca]) => { setClients(cl); setCampaigns(ca) })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  const q = search.toLowerCase()
  const visible = campaigns.filter((c) =>
    (filterClientId ? c.clientId === filterClientId : true) &&
    (c.name.toLowerCase().includes(q) || c.slug.toLowerCase().includes(q))
  )

  async function handleCreate() {
    if (!newClientId || !newName.trim() || !newSlug.trim()) return
    setCreating(true)
    setCreateError(null)
    try {
      const c = await createCampaign(newClientId, newName.trim(), newSlug.trim(), newDesc.trim() || undefined)
      setCampaigns((prev) => [...prev, c])
      setNewName(''); setNewSlug(''); setNewDesc(''); setShowCreate(false)
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : 'Create failed.')
    } finally {
      setCreating(false)
    }
  }

  async function handleStatusChange(campaign: Campaign, action: 'activate' | 'pause' | 'deactivate') {
    try {
      const fn = action === 'activate' ? activateCampaign
        : action === 'pause' ? pauseCampaign
        : deactivateCampaign
      const updated = await fn(campaign.id)
      setCampaigns((prev) => prev.map((c) => c.id === campaign.id ? updated : c))
    } catch {}
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-3">
          <p className="text-gray-500 text-sm">Campaigns map phone numbers to flows and queues.</p>
          <SearchableSelect
            options={clients.map((c) => ({ value: c.id, label: c.name }))}
            value={filterClientId}
            onChange={setFilterClientId}
            allLabel="All clients"
            className="w-48"
          />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search…"
            className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-48"
          />
        </div>
        <button
          onClick={() => { setShowCreate((v) => !v); setCreateError(null) }}
          className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
        >
          Add campaign
        </button>
      </div>

      {showCreate && (
        <div className="bg-gray-900 border border-gray-800 rounded-xl p-4 mb-4">
          <p className="text-gray-300 text-sm font-medium mb-3">New campaign</p>
          <div className="grid grid-cols-2 gap-3 mb-3">
            <select
              value={newClientId}
              onChange={(e) => setNewClientId(e.target.value)}
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            >
              <option value="">Select client *</option>
              {clients.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
            <input
              autoFocus
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              placeholder="Campaign name *"
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            />
            <input
              value={newSlug}
              onChange={(e) => setNewSlug(e.target.value)}
              placeholder="Slug * (e.g. inbound-sales)"
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            />
            <input
              value={newDesc}
              onChange={(e) => setNewDesc(e.target.value)}
              placeholder="Description (optional)"
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>
          <div className="flex items-center gap-3">
            <button
              onClick={handleCreate}
              disabled={creating || !newClientId || !newName.trim() || !newSlug.trim()}
              className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
            >
              {creating ? 'Creating…' : 'Create'}
            </button>
            <button onClick={() => setShowCreate(false)} className="text-gray-500 hover:text-white text-sm">
              Cancel
            </button>
          </div>
          {createError && <p className="text-red-400 text-xs mt-2">{createError}</p>}
        </div>
      )}

      {loading && <p className="text-gray-400 text-sm">Loading…</p>}
      {error && <p className="text-red-400 text-sm">{error}</p>}
      {!loading && !error && visible.length === 0 && campaigns.length === 0 && (
        <p className="text-gray-500 text-sm">No campaigns yet.</p>
      )}
      {!loading && !error && visible.length === 0 && campaigns.length > 0 && (
        <p className="text-gray-500 text-sm">No campaigns match your filters.</p>
      )}

      {visible.length > 0 && (
        <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-800 text-gray-400 text-left">
                <th className="px-4 py-3 font-medium">Name</th>
                <th className="px-4 py-3 font-medium">Client</th>
                <th className="px-4 py-3 font-medium">Slug</th>
                <th className="px-4 py-3 font-medium">Direction</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3 font-medium"></th>
              </tr>
            </thead>
            <tbody>
              {visible.map((c) => (
                <tr key={c.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30">
                  <td className="px-4 py-3 text-white font-medium">{c.name}</td>
                  <td className="px-4 py-3 text-gray-400">
                    {c.client?.name ?? clients.find((cl) => cl.id === c.clientId)?.name ?? '—'}
                  </td>
                  <td className="px-4 py-3 text-gray-500 font-mono text-xs">{c.slug}</td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${DIRECTION_COLORS[c.direction] ?? 'bg-gray-700/60 text-gray-400'}`}>
                      {c.direction}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${STATUS_COLORS[c.status] ?? STATUS_COLORS.inactive}`}>
                      {c.status}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <div className="flex items-center justify-end gap-3">
                      {c.status !== 'active' && (
                        <button onClick={() => handleStatusChange(c, 'activate')} className="text-emerald-400 hover:text-emerald-300 text-xs font-medium">Activate</button>
                      )}
                      {c.status === 'active' && (
                        <button onClick={() => handleStatusChange(c, 'pause')} className="text-amber-400 hover:text-amber-300 text-xs font-medium">Pause</button>
                      )}
                      {c.status !== 'inactive' && (
                        <button onClick={() => handleStatusChange(c, 'deactivate')} className="text-gray-500 hover:text-gray-300 text-xs font-medium">Deactivate</button>
                      )}
                      <button
                        onClick={() => navigate(`/admin/campaigns/${c.id}`)}
                        className="text-indigo-400 hover:text-indigo-300 text-xs font-medium border border-indigo-900 hover:border-indigo-700 rounded px-2.5 py-1 transition-colors"
                      >
                        Manage →
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

// ── Phone Numbers Tab ─────────────────────────────────────────────────────────

function PhoneNumbersTab() {
  const [campaigns, setCampaigns] = useState<Campaign[]>([])
  const [numbers, setNumbers] = useState<PhoneNumber[]>([])
  const [selectedCampaignId, setSelectedCampaignId] = useState('')
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [numLoading, setNumLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [showCreate, setShowCreate] = useState(false)
  const [newCampaignId, setNewCampaignId] = useState('')
  const [newNumber, setNewNumber] = useState('')
  const [newLabel, setNewLabel] = useState('')
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  useEffect(() => {
    listCampaigns()
      .then(setCampaigns)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    if (!selectedCampaignId) { setNumbers([]); return }
    setNumLoading(true)
    listPhoneNumbers(selectedCampaignId)
      .then(setNumbers)
      .catch(() => {})
      .finally(() => setNumLoading(false))
  }, [selectedCampaignId])

  async function handleCreate() {
    if (!newCampaignId || !newNumber.trim()) return
    setCreating(true)
    setCreateError(null)
    try {
      const pn = await createPhoneNumber(newCampaignId, newNumber.trim(), newLabel.trim() || undefined)
      if (pn.campaignId === selectedCampaignId) setNumbers((prev) => [...prev, pn])
      setNewNumber(''); setNewLabel(''); setShowCreate(false)
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : 'Create failed.')
    } finally {
      setCreating(false)
    }
  }

  async function handleToggle(pn: PhoneNumber) {
    try {
      const updated = pn.isActive
        ? await deactivatePhoneNumber(pn.id)
        : await activatePhoneNumber(pn.id)
      setNumbers((prev) => prev.map((n) => n.id === pn.id ? updated : n))
    } catch {}
  }

  const pq = search.toLowerCase()
  const visibleNumbers = numbers.filter((n) =>
    n.number.toLowerCase().includes(pq) ||
    (n.label ?? '').toLowerCase().includes(pq)
  )

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-3">
          <p className="text-gray-500 text-sm">DIDs assigned to campaigns.</p>
          <SearchableSelect
            options={campaigns.map((c) => ({ value: c.id, label: c.name }))}
            value={selectedCampaignId}
            onChange={(v) => { setSelectedCampaignId(v); setSearch('') }}
            placeholder="Select a campaign…"
            className="w-56"
          />
          {selectedCampaignId && (
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search…"
              className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-48"
            />
          )}
        </div>
        <button
          onClick={() => {
            setShowCreate((v) => !v)
            setNewCampaignId(selectedCampaignId)
            setCreateError(null)
          }}
          className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
        >
          Add DID
        </button>
      </div>

      {showCreate && (
        <div className="bg-gray-900 border border-gray-800 rounded-xl p-4 mb-4">
          <p className="text-gray-300 text-sm font-medium mb-3">Add phone number</p>
          <div className="flex items-center gap-3 flex-wrap">
            <select
              value={newCampaignId}
              onChange={(e) => setNewCampaignId(e.target.value)}
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            >
              <option value="">Select campaign *</option>
              {campaigns.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
            <input
              autoFocus
              value={newNumber}
              onChange={(e) => setNewNumber(e.target.value)}
              placeholder="+15035551234 (E.164) *"
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-48"
            />
            <input
              value={newLabel}
              onChange={(e) => setNewLabel(e.target.value)}
              placeholder="Label (optional)"
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-44"
            />
            <button
              onClick={handleCreate}
              disabled={creating || !newCampaignId || !newNumber.trim()}
              className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
            >
              {creating ? 'Adding…' : 'Add'}
            </button>
            <button onClick={() => setShowCreate(false)} className="text-gray-500 hover:text-white text-sm">
              Cancel
            </button>
          </div>
          {createError && <p className="text-red-400 text-xs mt-2">{createError}</p>}
        </div>
      )}

      {loading && <p className="text-gray-400 text-sm">Loading campaigns…</p>}
      {error && <p className="text-red-400 text-sm">{error}</p>}
      {!loading && !selectedCampaignId && (
        <p className="text-gray-500 text-sm">Select a campaign to view its phone numbers.</p>
      )}
      {numLoading && <p className="text-gray-400 text-sm">Loading…</p>}
      {!numLoading && selectedCampaignId && numbers.length === 0 && (
        <p className="text-gray-500 text-sm">No phone numbers assigned to this campaign yet.</p>
      )}
      {!numLoading && selectedCampaignId && numbers.length > 0 && visibleNumbers.length === 0 && (
        <p className="text-gray-500 text-sm">No numbers match "{search}".</p>
      )}

      {visibleNumbers.length > 0 && (
        <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-800 text-gray-400 text-left">
                <th className="px-4 py-3 font-medium">Number</th>
                <th className="px-4 py-3 font-medium">Label</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3 font-medium"></th>
              </tr>
            </thead>
            <tbody>
              {visibleNumbers.map((n) => (
                <tr key={n.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30">
                  <td className="px-4 py-3 text-white font-mono">{n.number}</td>
                  <td className="px-4 py-3 text-gray-400">{n.label ?? <span className="text-gray-600">—</span>}</td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${n.isActive ? STATUS_COLORS.active : STATUS_COLORS.inactive}`}>
                      {n.isActive ? 'active' : 'inactive'}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <button
                      onClick={() => handleToggle(n)}
                      className="text-indigo-400 hover:text-indigo-300 text-xs font-medium"
                    >
                      {n.isActive ? 'Deactivate' : 'Activate'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

// ── Agent Groups Tab ──────────────────────────────────────────────────────────

function AgentGroupsTab() {
  const [groups, setGroups] = useState<AgentGroup[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')

  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [detail, setDetail] = useState<AgentGroupDetail | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)

  const [showCreate, setShowCreate] = useState(false)
  const [newName, setNewName] = useState('')
  const [newSlug, setNewSlug] = useState('')
  const [newDesc, setNewDesc] = useState('')
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  const [memberInput, setMemberInput] = useState('')
  const [memberError, setMemberError] = useState<string | null>(null)
  const [memberAdding, setMemberAdding] = useState(false)

  useEffect(() => {
    listAgentGroups()
      .then(setGroups)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  async function handleExpand(group: AgentGroup) {
    if (expandedId === group.id) { setExpandedId(null); setDetail(null); return }
    setExpandedId(group.id)
    setDetail(null)
    setMemberInput('')
    setMemberError(null)
    setDetailLoading(true)
    try {
      const d = await getAgentGroup(group.id)
      setDetail(d)
    } catch {}
    finally { setDetailLoading(false) }
  }

  async function handleCreate() {
    if (!newName.trim() || !newSlug.trim()) return
    setCreating(true)
    setCreateError(null)
    try {
      const g = await createAgentGroup(newName.trim(), newSlug.trim(), newDesc.trim() || undefined)
      setGroups((prev) => [...prev, g])
      setNewName(''); setNewSlug(''); setNewDesc(''); setShowCreate(false)
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : 'Create failed.')
    } finally {
      setCreating(false)
    }
  }

  async function handleAddMember() {
    if (!expandedId || !memberInput.trim()) return
    setMemberAdding(true)
    setMemberError(null)
    try {
      const m = await addGroupMember(expandedId, memberInput.trim())
      setDetail((prev) => prev ? { ...prev, members: [...prev.members, m] } : prev)
      setMemberInput('')
    } catch (e) {
      setMemberError(e instanceof Error ? e.message : 'Failed to add member.')
    } finally {
      setMemberAdding(false)
    }
  }

  async function handleRemoveMember(groupId: string, agentId: string) {
    try {
      await removeGroupMember(groupId, agentId)
      setDetail((prev) => prev ? { ...prev, members: prev.members.filter((m) => m.agentId !== agentId) } : prev)
    } catch {}
  }

  const gq = search.toLowerCase()
  const visibleGroups = groups.filter((g) =>
    g.name.toLowerCase().includes(gq) ||
    g.slug.toLowerCase().includes(gq)
  )

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-3">
          <p className="text-gray-500 text-sm">Groups allow agents to serve multiple campaigns in parallel.</p>
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search…"
            className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-48"
          />
        </div>
        <button
          onClick={() => { setShowCreate((v) => !v); setCreateError(null) }}
          className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
        >
          Add group
        </button>
      </div>

      {showCreate && (
        <div className="bg-gray-900 border border-gray-800 rounded-xl p-4 mb-4">
          <p className="text-gray-300 text-sm font-medium mb-3">New agent group</p>
          <div className="flex items-center gap-3 flex-wrap mb-3">
            <input
              autoFocus
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              placeholder="Group name *"
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-48"
            />
            <input
              value={newSlug}
              onChange={(e) => setNewSlug(e.target.value)}
              placeholder="Slug * (e.g. alpha-sales)"
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-48"
            />
            <input
              value={newDesc}
              onChange={(e) => setNewDesc(e.target.value)}
              placeholder="Description (optional)"
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-52"
            />
          </div>
          <div className="flex items-center gap-3">
            <button
              onClick={handleCreate}
              disabled={creating || !newName.trim() || !newSlug.trim()}
              className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
            >
              {creating ? 'Creating…' : 'Create'}
            </button>
            <button onClick={() => setShowCreate(false)} className="text-gray-500 hover:text-white text-sm">
              Cancel
            </button>
          </div>
          {createError && <p className="text-red-400 text-xs mt-2">{createError}</p>}
        </div>
      )}

      {loading && <p className="text-gray-400 text-sm">Loading…</p>}
      {error && <p className="text-red-400 text-sm">{error}</p>}
      {!loading && !error && groups.length === 0 && (
        <p className="text-gray-500 text-sm">No agent groups yet.</p>
      )}
      {!loading && !error && groups.length > 0 && visibleGroups.length === 0 && (
        <p className="text-gray-500 text-sm">No groups match "{search}".</p>
      )}

      {visibleGroups.length > 0 && (
        <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden">
          {visibleGroups.map((g, i) => (
            <div key={g.id}>
              {/* Group row */}
              <div
                className={`flex items-center gap-4 px-4 py-3 cursor-pointer hover:bg-gray-800/30 transition-colors ${i < visibleGroups.length - 1 || expandedId === g.id ? 'border-b border-gray-800' : ''}`}
                onClick={() => handleExpand(g)}
              >
                <span className="text-gray-400 text-xs w-4">{expandedId === g.id ? '▾' : '▸'}</span>
                <span className="text-white font-medium text-sm flex-1">{g.name}</span>
                <span className="text-gray-500 font-mono text-xs">{g.slug}</span>
                <span className="text-gray-500 text-xs">{g.memberCount} member{g.memberCount !== 1 ? 's' : ''}</span>
                <span className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${g.isActive ? STATUS_COLORS.active : STATUS_COLORS.inactive}`}>
                  {g.isActive ? 'active' : 'inactive'}
                </span>
              </div>

              {/* Expanded member panel */}
              {expandedId === g.id && (
                <div className={`bg-gray-950 px-6 py-4 ${i < visibleGroups.length - 1 ? 'border-b border-gray-800' : ''}`}>
                  {detailLoading && <p className="text-gray-400 text-sm">Loading…</p>}
                  {detail && (
                    <>
                      {/* Add member */}
                      <div className="flex items-center gap-3 mb-4">
                        <input
                          value={memberInput}
                          onChange={(e) => { setMemberInput(e.target.value); setMemberError(null) }}
                          onKeyDown={(e) => e.key === 'Enter' && handleAddMember()}
                          placeholder="Agent ID (UUID)"
                          className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-80 font-mono"
                        />
                        <button
                          onClick={handleAddMember}
                          disabled={memberAdding || !memberInput.trim()}
                          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-3 py-1.5 text-sm font-medium transition-colors"
                        >
                          {memberAdding ? 'Adding…' : 'Add member'}
                        </button>
                        {memberError && <span className="text-red-400 text-xs">{memberError}</span>}
                      </div>

                      {detail.members.length === 0 ? (
                        <p className="text-gray-500 text-xs">No members yet.</p>
                      ) : (
                        <div className="space-y-1">
                          {detail.members.map((m) => (
                            <div key={m.agentId} className="flex items-center gap-3 text-sm">
                              <span className="text-gray-300 font-mono text-xs">{m.agentId}</span>
                              <span className="text-gray-600 text-xs">
                                joined {new Date(m.joinedAt).toLocaleDateString()}
                              </span>
                              <button
                                onClick={() => handleRemoveMember(g.id, m.agentId)}
                                className="text-red-500 hover:text-red-400 text-xs ml-auto"
                              >
                                Remove
                              </button>
                            </div>
                          ))}
                        </div>
                      )}
                    </>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function TelephonyPage() {
  const location = useLocation()
  const initialTab = (location.state as { tab?: Tab } | null)?.tab ?? 'clients'
  const [tab, setTab] = useState<Tab>(initialTab)

  return (
    <AdminShell>
      <div className="p-6 max-w-6xl">
        <div className="mb-6">
          <h1 className="text-white text-xl font-semibold">Telephony</h1>
          <p className="text-gray-500 text-sm mt-0.5">Configure clients, campaigns, DIDs, and agent groups.</p>
        </div>

        {/* Tab bar */}
        <div className="flex gap-1 mb-6 border-b border-gray-800">
          {TABS.map((t) => (
            <button
              key={t.id}
              onClick={() => setTab(t.id)}
              className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
                tab === t.id
                  ? 'border-indigo-500 text-white'
                  : 'border-transparent text-gray-500 hover:text-gray-300'
              }`}
            >
              {t.label}
            </button>
          ))}
        </div>

        {tab === 'clients'       && <ClientsTab />}
        {tab === 'campaigns'     && <CampaignsTab />}
        {tab === 'phone-numbers' && <PhoneNumbersTab />}
        {tab === 'agent-groups'  && <AgentGroupsTab />}
      </div>
    </AdminShell>
  )
}
