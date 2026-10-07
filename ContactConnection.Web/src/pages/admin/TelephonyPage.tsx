import { Fragment, useEffect, useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import SearchableSelect from '../../components/SearchableSelect'
import GroupRoutingPanel from '../../components/admin/GroupRoutingPanel'
import MediaAssignmentsModal from '../../components/admin/MediaAssignmentsModal'
import AssignmentGrid from '../../components/admin/AssignmentGrid'
import {
  listClients, createClient, activateClient, deactivateClient,
  getOrderNumberSequence, putOrderNumberSequence, deleteOrderNumberSequence,
  listCampaigns, createCampaign, activateCampaign, pauseCampaign, deactivateCampaign,
  createPhoneNumber, updatePhoneNumberProvider,
  listAllPhoneNumbers, bulkAddPhoneNumbers, bulkPhoneNumberAction, assignFromReserve,
  type NumberDirectory, type NumberRow, type NumberStatus, type BulkNumberResult, type BulkNumberAction,
  listNumberProviders, createNumberProvider, updateNumberProvider, activateNumberProvider, deactivateNumberProvider,
  issueNumberProviderApiKey, revokeNumberProviderApiKey,
  type NumberProvider, type NumberProviderType, type PhoneNumberRole,
  setPhoneNumberFlow, removePhoneNumberFlow,
  setPhoneNumberTelephonyFlow, removePhoneNumberTelephonyFlow,
  listAgentGroups, createAgentGroup, getAgentGroup, addGroupMember, removeGroupMember,
  type Client, type Campaign, type PhoneNumber, type AgentGroup, type AgentGroupDetail,
} from '../../api/telephony'
import { listAdminAgents, type AgentRecord } from '../../api/adminAgents'
import { flowsApi, type FlowSummary } from '../../api/flows'
import { api } from '../../api/client'
import { openCallTrace } from '../../components/calltrace/openCallTrace'
import { CheckIcon, ChevronDownIcon, ChevronRightIcon, CloseIcon } from '../../components/icons/Icons'

type Tab = 'clients' | 'campaigns' | 'phone-numbers' | 'providers' | 'agent-groups' | 'assignments' | 'test-call'

const TABS: { id: Tab; label: string }[] = [
  { id: 'clients',       label: 'Clients' },
  { id: 'campaigns',     label: 'Campaigns' },
  { id: 'phone-numbers', label: 'Phone Numbers' },
  { id: 'providers',     label: 'Number Providers' },
  { id: 'agent-groups',  label: 'Agent Groups' },
  { id: 'assignments',   label: 'Assignments' },
  { id: 'test-call',     label: 'Test Call' },
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

// ── Order number sequence (per client) ───────────────────────────────────────

// Must match OrderNumberSequence.MaxFormattedLength on the server — Authorize.Net's cap on
// invoiceNumber/refId, the tightest limit of any consumer of the number.
const ORDER_NUMBER_MAX_LENGTH = 20

function formatOrderNumber(prefix: string, suffix: string, width: number, value: number) {
  return `${prefix}${String(value).padStart(width, '0')}${suffix}`
}

function OrderNumberSequencePanel({ clientId }: { clientId: string }) {
  const [loading, setLoading] = useState(true)
  const [configured, setConfigured] = useState(false)
  const [savedNextValue, setSavedNextValue] = useState<number | null>(null)
  const [prefix, setPrefix] = useState('')
  const [suffix, setSuffix] = useState('')
  const [width, setWidth] = useState(8)
  const [nextValue, setNextValue] = useState(1)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [savedMsg, setSavedMsg] = useState<string | null>(null)
  const [confirmRemove, setConfirmRemove] = useState(false)

  useEffect(() => {
    getOrderNumberSequence(clientId)
      .then((seq) => {
        setConfigured(seq.configured)
        if (seq.configured) {
          setPrefix(seq.prefix); setSuffix(seq.suffix); setWidth(seq.width)
          setNextValue(seq.nextValue); setSavedNextValue(seq.nextValue)
        }
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [clientId])

  const preview = formatOrderNumber(prefix.trim(), suffix.trim(), width, nextValue)
  const tooLong = preview.length > ORDER_NUMBER_MAX_LENGTH
  const lowering = savedNextValue !== null && nextValue < savedNextValue

  async function handleSave() {
    setSaving(true); setError(null); setSavedMsg(null)
    try {
      const seq = await putOrderNumberSequence(clientId, {
        prefix: prefix.trim(), suffix: suffix.trim(), width, nextValue,
      })
      if (seq.configured) {
        setConfigured(true); setSavedNextValue(seq.nextValue)
        setSavedMsg(`Saved — next order number: ${seq.nextOrderNumber}`)
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  async function handleRemove() {
    setSaving(true); setError(null); setSavedMsg(null)
    try {
      await deleteOrderNumberSequence(clientId)
      setConfigured(false); setSavedNextValue(null); setConfirmRemove(false)
      setSavedMsg('Order numbers turned off for this client.')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Remove failed.')
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <p className="text-gray-400 text-xs">Loading…</p>

  const inputCls = 'bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500'

  return (
    <div>
      <p className="text-gray-300 text-sm font-medium">Order numbers</p>
      <p className="text-gray-500 text-xs mb-3">
        Each call that places an order gets the next number (assigned at payment authorization).
        It is sent to the payment gateway as the invoice number and is available to order APIs as{' '}
        <code className="text-gray-400">{'{{call_record.order_number}}'}</code>.
        {!configured && ' Not configured — no order numbers are generated for this client.'}
      </p>
      <div className="flex items-end gap-3 flex-wrap">
        <label className="text-xs text-gray-400">Prefix
          <input value={prefix} onChange={(e) => setPrefix(e.target.value)} placeholder="e.g. LIFSEA-"
            className={`${inputCls} block mt-1 w-32`} />
        </label>
        <label className="text-xs text-gray-400">Digits
          <input type="number" min={1} max={18} value={width}
            onChange={(e) => setWidth(Math.max(1, Math.min(18, Number(e.target.value) || 1)))}
            className={`${inputCls} block mt-1 w-20`} />
        </label>
        <label className="text-xs text-gray-400">Suffix
          <input value={suffix} onChange={(e) => setSuffix(e.target.value)} placeholder="optional"
            className={`${inputCls} block mt-1 w-24`} />
        </label>
        <label className="text-xs text-gray-400">Next number
          <input type="number" min={0} value={nextValue}
            onChange={(e) => setNextValue(Math.max(0, Math.floor(Number(e.target.value) || 0)))}
            className={`${inputCls} block mt-1 w-36`} />
        </label>
        <div className="text-xs text-gray-400">Preview
          <div className={`mt-1 px-3 py-1.5 rounded-lg font-mono text-sm ${tooLong ? 'bg-red-900/30 text-red-300' : 'bg-gray-800/60 text-emerald-300'}`}>
            {preview}
          </div>
        </div>
        <button onClick={handleSave} disabled={saving || tooLong}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-1.5 text-sm font-medium transition-colors">
          {saving ? 'Saving…' : configured ? 'Save' : 'Turn on'}
        </button>
        {configured && !confirmRemove && (
          <button onClick={() => setConfirmRemove(true)} className="text-gray-500 hover:text-red-400 text-xs">
            Turn off
          </button>
        )}
        {configured && confirmRemove && (
          <span className="text-xs text-gray-400">
            Stop generating order numbers?{' '}
            <button onClick={handleRemove} className="text-red-400 hover:text-red-300 font-medium">Yes, turn off</button>{' '}
            <button onClick={() => setConfirmRemove(false)} className="text-gray-500 hover:text-white">Cancel</button>
          </span>
        )}
      </div>
      {tooLong && (
        <p className="text-red-400 text-xs mt-2">
          Order numbers can be at most {ORDER_NUMBER_MAX_LENGTH} characters — payment gateways reject longer invoice numbers.
        </p>
      )}
      {lowering && !tooLong && (
        <p className="text-amber-400 text-xs mt-2">
          The next number is lower than the current one ({savedNextValue}) — numbers already used may be issued again.
        </p>
      )}
      {error && <p className="text-red-400 text-xs mt-2">{error}</p>}
      {savedMsg && <p className="text-emerald-400 text-xs mt-2">{savedMsg}</p>}
    </div>
  )
}

// ── Clients Tab ───────────────────────────────────────────────────────────────

function ClientsTab() {
  const [clients, setClients] = useState<Client[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [orderNumbersFor, setOrderNumbersFor] = useState<string | null>(null)

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
                <Fragment key={c.id}>
                <tr className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30">
                  <td className="px-4 py-3 text-white font-medium">{c.name}</td>
                  <td className="px-4 py-3 text-gray-400">{c.accountNumber ?? <span className="text-gray-600">—</span>}</td>
                  <td className="px-4 py-3 text-gray-400">{c.campaigns.length}</td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${STATUS_COLORS[c.status] ?? STATUS_COLORS.inactive}`}>
                      {c.status}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right whitespace-nowrap">
                    <button
                      onClick={() => setOrderNumbersFor((cur) => cur === c.id ? null : c.id)}
                      className="text-gray-400 hover:text-white text-xs font-medium mr-4"
                    >
                      {orderNumbersFor === c.id ? 'Close' : 'Order numbers'}
                    </button>
                    <button
                      onClick={() => handleToggleActive(c)}
                      className="text-indigo-400 hover:text-indigo-300 text-xs font-medium"
                    >
                      {c.status === 'active' ? 'Deactivate' : 'Activate'}
                    </button>
                  </td>
                </tr>
                {orderNumbersFor === c.id && (
                  <tr className="border-b border-gray-800 bg-gray-950/40">
                    <td colSpan={5} className="px-4 py-4">
                      <OrderNumberSequencePanel clientId={c.id} />
                    </td>
                  </tr>
                )}
                </Fragment>
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
                        onClick={() => openCallTrace({ campaignId: c.id })}
                        className="text-gray-400 hover:text-gray-200 text-xs font-medium border border-gray-700 hover:border-gray-500 rounded px-2.5 py-1 transition-colors"
                      >
                        Trace
                      </button>
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

/** Tenant default outbound caller ID (S179) — direct dials, and manual outbound campaigns with no caller ID of their own. */
function DefaultOutboundCallerId({ numbers }: { numbers: { id: string; number: string; label?: string | null }[] }) {
  const [value, setValue] = useState('')
  const [saved, setSaved] = useState('')
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  useEffect(() => {
    api.get<{ defaultOutboundCallerId: string | null }>('/api/v1/telephony/outbound-settings')
      .then((r) => { setValue(r.defaultOutboundCallerId ?? ''); setSaved(r.defaultOutboundCallerId ?? '') })
      .catch(() => {})
  }, [])
  async function save() {
    setMsg(null)
    try {
      const r = await api.put<{ defaultOutboundCallerId: string | null }>('/api/v1/telephony/outbound-settings',
        { defaultOutboundCallerId: value.trim() || null })
      setValue(r.defaultOutboundCallerId ?? ''); setSaved(r.defaultOutboundCallerId ?? '')
      setMsg({ ok: true, text: 'Saved.' })
    } catch (e) { setMsg({ ok: false, text: e instanceof Error ? e.message : 'Save failed.' }) }
  }
  return (
    <div className="mb-5 rounded-lg border border-gray-800 bg-gray-900/60 px-4 py-3">
      <label className="block text-xs text-gray-400 mb-1">Default outbound caller ID</label>
      <div className="flex flex-wrap items-center gap-2">
        <input list="tenant-dids" value={value} onChange={(e) => setValue(e.target.value)} placeholder="+15415551234"
          className="w-56 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm font-mono outline-none focus:ring-2 focus:ring-indigo-500" />
        <datalist id="tenant-dids">{numbers.map((n) => <option key={n.id} value={n.number}>{n.label ?? ''}</option>)}</datalist>
        <button onClick={() => void save()} disabled={value.trim() === saved}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded-lg px-3 py-2 text-sm">Save</button>
        {msg && <span className={`text-xs ${msg.ok ? 'text-emerald-400' : 'text-red-400'}`}>{msg.text}</span>}
      </div>
      <p className="text-[11px] text-gray-500 mt-1">
        Used for direct dials and for manual outbound campaigns that have no caller ID of their own. Use a number on your
        carrier account — the carrier rejects any other.
      </p>
    </div>
  )
}

const NUMBER_STATUS: Record<NumberStatus, { label: string; cls: string; hint: string }> = {
  active:   { label: 'active',   cls: 'bg-emerald-900/50 text-emerald-400', hint: 'Takes calls for its campaign.' },
  inactive: { label: 'inactive', cls: 'bg-gray-700/60 text-gray-400',       hint: 'Held for its campaign — calls are rejected.' },
  reserve:  { label: 'reserve',  cls: 'bg-sky-900/50 text-sky-300',         hint: 'Waiting to be assigned — calls are rejected.' },
  released: { label: 'released', cls: 'bg-red-950/60 text-red-300',         hint: 'No longer your number — kept for its call history.' },
}

const VIEW_ALL = '__all', VIEW_RESERVE = '__reserve', VIEW_RELEASED = '__released'

function sinceText(iso: string | null) {
  if (!iso) return ''
  const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000)
  return days <= 0 ? 'today' : days === 1 ? '1 day' : `${days} days`
}

function PhoneNumbersTab() {
  const [dir, setDir] = useState<NumberDirectory | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [scriptFlows, setScriptFlows] = useState<FlowSummary[]>([])
  const [inboundFlows, setInboundFlows] = useState<FlowSummary[]>([])
  const [providers, setProviders] = useState<NumberProvider[]>([])
  const [mediaFor, setMediaFor] = useState<NumberRow | null>(null)
  const [editingProviderFor, setEditingProviderFor] = useState<string | null>(null)

  const [view, setView] = useState(VIEW_ALL)
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [panel, setPanel] = useState<'add' | 'reserve' | null>(null)
  const [notice, setNotice] = useState<{ ok: boolean; text: string; results?: BulkNumberResult[] } | null>(null)
  const [busy, setBusy] = useState(false)

  // Bulk bar inputs
  const [moveTo, setMoveTo] = useState('')
  const [bulkLabel, setBulkLabel] = useState('')
  const [confirmRelease, setConfirmRelease] = useState<string[] | null>(null)
  const [confirmDelete, setConfirmDelete] = useState<string[] | null>(null)

  function reload() {
    return listAllPhoneNumbers().then(setDir).catch((e: Error) => setError(e.message))
  }

  useEffect(() => {
    void reload()
    flowsApi.listAll().then((all) => {
      setScriptFlows(all.filter((f) => f.flow_type === 'crm'))
      setInboundFlows(all.filter((f) => f.flow_type === 'telephony' && f.flow_direction === 'inbound'))
    }).catch(() => {})
    listNumberProviders().then(setProviders).catch(() => {})
  }, [])

  const numbers = dir?.numbers ?? []
  const campaigns = dir?.campaigns ?? []
  const campaignLabel = (c: { name: string; clientName: string | null }) => c.clientName ? `${c.clientName} — ${c.name}` : c.name
  const counts = (['active', 'inactive', 'reserve', 'released'] as NumberStatus[])
    .map((s) => [s, numbers.filter((n) => n.status === s).length] as const)

  const q = search.trim().toLowerCase()
  const qDigits = q.replace(/\D/g, '')
  const visible = numbers
    .filter((n) => view === VIEW_ALL ? n.status !== 'released'
      : view === VIEW_RESERVE ? n.status === 'reserve'
      : view === VIEW_RELEASED ? n.status === 'released'
      : n.campaignId === view)
    .filter((n) => !q
      || (qDigits.length > 0 && n.number.replace(/\D/g, '').includes(qDigits))
      || (n.label ?? '').toLowerCase().includes(q)
      || (n.campaignName ?? '').toLowerCase().includes(q)
      || (n.clientName ?? '').toLowerCase().includes(q))
    .sort((a, b) => view === VIEW_RESERVE ? (a.reserveRank ?? 0) - (b.reserveRank ?? 0) : a.number.localeCompare(b.number))

  const selectedRows = numbers.filter((n) => selected.has(n.id))
  const allVisibleSelected = visible.length > 0 && visible.every((n) => selected.has(n.id))
  function toggle(id: string) {
    setSelected((prev) => { const next = new Set(prev); if (next.has(id)) next.delete(id); else next.add(id); return next })
  }
  function toggleAllVisible() {
    setSelected((prev) => {
      const next = new Set(prev)
      if (allVisibleSelected) visible.forEach((n) => next.delete(n.id)); else visible.forEach((n) => next.add(n.id))
      return next
    })
  }

  async function runBulk(action: BulkNumberAction, ids: string[], extra: { campaignId?: string; label?: string | null } = {}) {
    setBusy(true); setNotice(null)
    try {
      const r = await bulkPhoneNumberAction({ ids, action, ...extra })
      const problems = r.results.filter((x) => x.outcome !== 'done')
      setNotice({ ok: problems.length === 0, text: `${r.done} number${r.done === 1 ? '' : 's'} updated.`, results: problems.length ? problems : undefined })
      setSelected(new Set())
      await reload()
    } catch (e) { setNotice({ ok: false, text: e instanceof Error ? e.message : 'Update failed.' }) }
    finally { setBusy(false); setConfirmRelease(null); setConfirmDelete(null) }
  }

  /** Deactivating a Reserve number releases it — ask first. */
  function deactivate(ids: string[]) {
    const releasing = numbers.filter((n) => ids.includes(n.id) && n.status === 'reserve')
    if (releasing.length > 0) setConfirmRelease(ids)
    else void runBulk('deactivate', ids)
  }

  async function rowFlow(n: NumberRow, kind: 'script' | 'telephony', flowId: string) {
    try {
      if (kind === 'script') await (flowId ? setPhoneNumberFlow(n.id, flowId) : removePhoneNumberFlow(n.id))
      else await (flowId ? setPhoneNumberTelephonyFlow(n.id, flowId) : removePhoneNumberTelephonyFlow(n.id))
      await reload()
    } catch (e) { setNotice({ ok: false, text: e instanceof Error ? e.message : 'Update failed.' }) }
  }

  const viewOptions = [
    { value: VIEW_ALL, label: 'All numbers' },
    { value: VIEW_RESERVE, label: 'Reserve' },
    { value: VIEW_RELEASED, label: 'Released' },
    ...campaigns.map((c) => ({ value: c.id, label: campaignLabel(c) })),
  ]
  const btn = 'rounded-lg px-3 py-1.5 text-xs font-medium disabled:opacity-40'

  return (
    <div>
      {mediaFor && (
        <MediaAssignmentsModal phoneNumberId={mediaFor.id} number={mediaFor.number}
          clientNumber={mediaFor.clientNumber} onClose={() => setMediaFor(null)} />
      )}
      <DefaultOutboundCallerId numbers={numbers.filter((n) => n.status !== 'released')} />

      {/* Status counts double as quick views */}
      <div className="flex flex-wrap items-center gap-2 mb-3 text-xs">
        {counts.map(([s, c]) => (
          <button key={s} type="button" title={NUMBER_STATUS[s].hint}
            onClick={() => { setView(s === 'reserve' ? VIEW_RESERVE : s === 'released' ? VIEW_RELEASED : VIEW_ALL); setSearch('') }}
            className={`px-2 py-0.5 rounded ${NUMBER_STATUS[s].cls}`}>
            {c} {NUMBER_STATUS[s].label}
          </button>
        ))}
        <span className="text-gray-600">Reserve, inactive and released numbers reject calls (not in service).</span>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3 mb-4">
        <div className="flex flex-wrap items-center gap-3">
          <SearchableSelect options={viewOptions} value={view} onChange={(v) => { setView(v || VIEW_ALL); setSelected(new Set()) }}
            placeholder="All numbers" className="w-72" />
          <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search number, label, campaign…"
            className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-60" />
        </div>
        <div className="flex gap-2">
          <button onClick={() => setPanel((p) => p === 'reserve' ? null : 'reserve')}
            className="bg-gray-800 hover:bg-gray-700 text-gray-200 rounded-lg px-4 py-2 text-sm font-medium">Assign from Reserve</button>
          <button onClick={() => setPanel((p) => p === 'add' ? null : 'add')}
            className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium">Add numbers</button>
        </div>
      </div>

      {panel === 'add' && (
        <BulkAddNumbersPanel campaigns={campaigns} campaignLabel={campaignLabel} providers={providers}
          defaultCampaignId={view.startsWith('__') ? '' : view}
          onDone={async (n) => { setNotice(n); await reload() }} onClose={() => setPanel(null)} />
      )}
      {panel === 'reserve' && (
        <AssignFromReservePanel campaigns={campaigns} campaignLabel={campaignLabel}
          reserve={numbers.filter((n) => n.status === 'reserve').sort((a, b) => (a.reserveRank ?? 0) - (b.reserveRank ?? 0))}
          defaultCampaignId={view.startsWith('__') ? '' : view}
          onDone={async (n) => { setNotice(n); await reload() }} onClose={() => setPanel(null)} />
      )}

      {notice && (
        <div className={`mb-4 rounded-lg border px-4 py-2 text-sm ${notice.ok ? 'border-emerald-800 text-emerald-300' : 'border-amber-800 text-amber-300'}`}>
          <div className="flex items-start justify-between gap-3">
            <span>{notice.text}</span>
            <button onClick={() => setNotice(null)} className="text-gray-500 hover:text-white text-xs">Dismiss</button>
          </div>
          {notice.results && (
            <ul className="mt-1 text-xs text-gray-300 space-y-0.5">
              {notice.results.map((r, i) => (
                <li key={i}><span className="font-mono">{r.number ?? r.input}</span> — <span className="text-amber-300">{r.outcome}</span>{r.detail ? `: ${r.detail}` : ''}</li>
              ))}
            </ul>
          )}
        </div>
      )}

      {/* Bulk bar */}
      {selected.size > 0 && (
        <div className="sticky top-0 z-10 mb-3 rounded-lg border border-indigo-800 bg-gray-950/95 px-4 py-2 flex flex-wrap items-center gap-2">
          <span className="text-sm text-white font-medium mr-1">{selected.size} selected</span>
          <select value={moveTo} onChange={(e) => setMoveTo(e.target.value)}
            className="bg-gray-800 text-white rounded-lg px-2 py-1.5 text-xs outline-none">
            <option value="">Move to campaign…</option>
            {campaigns.map((c) => <option key={c.id} value={c.id}>{campaignLabel(c)}</option>)}
          </select>
          <button disabled={busy || !moveTo} onClick={() => void runBulk('move', [...selected], { campaignId: moveTo })}
            className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white`}>Move</button>
          <button disabled={busy} onClick={() => void runBulk('reserve', [...selected])}
            className={`${btn} bg-sky-900/60 hover:bg-sky-800/60 text-sky-200`}>Move to Reserve</button>
          <button disabled={busy} onClick={() => void runBulk('activate', [...selected])}
            className={`${btn} bg-emerald-900/60 hover:bg-emerald-800/60 text-emerald-200`}>Activate</button>
          <button disabled={busy} onClick={() => deactivate([...selected])}
            className={`${btn} bg-gray-800 hover:bg-gray-700 text-gray-200`}>Deactivate</button>
          <input value={bulkLabel} onChange={(e) => setBulkLabel(e.target.value)} placeholder="Label"
            className="bg-gray-800 text-white rounded-lg px-2 py-1.5 text-xs outline-none w-36" />
          <button disabled={busy} onClick={() => void runBulk('label', [...selected], { label: bulkLabel.trim() || null })}
            className={`${btn} bg-gray-800 hover:bg-gray-700 text-gray-200`}>{bulkLabel.trim() ? 'Set label' : 'Clear label'}</button>
          <button disabled={busy} onClick={() => setConfirmDelete([...selected])}
            className={`${btn} text-red-300 hover:text-red-200`} title="Only numbers that never took a call">Delete…</button>
          <button onClick={() => setSelected(new Set())} className="text-gray-500 hover:text-white text-xs ml-auto">Clear selection</button>
          {selectedRows.some((n) => n.status === 'released') && (
            <p className="basis-full text-[11px] text-gray-500">Released numbers come back only if no other account has taken them since.</p>
          )}
        </div>
      )}

      {confirmRelease && (
        <div className="mb-3 rounded-lg border border-red-800 bg-red-950/40 px-4 py-3 text-sm">
          <p className="text-red-200">
            Deactivating {numbers.filter((n) => confirmRelease.includes(n.id) && n.status === 'reserve').length} Reserve
            number(s) <strong>releases</strong> them: they no longer belong to your account and another account may take them.
            Their call history stays. Numbers on a campaign are just held inactive for that campaign.
          </p>
          <div className="mt-2 flex gap-2">
            <button disabled={busy} onClick={() => void runBulk('deactivate', confirmRelease)}
              className={`${btn} bg-red-700 hover:bg-red-600 text-white`}>Release and deactivate</button>
            <button onClick={() => setConfirmRelease(null)} className="text-gray-400 hover:text-white text-xs">Cancel</button>
          </div>
        </div>
      )}

      {confirmDelete && (
        <div className="mb-3 rounded-lg border border-red-800 bg-red-950/40 px-4 py-3 text-sm">
          <p className="text-red-200">
            Delete {confirmDelete.length} number(s) permanently? Only numbers that never took a call (a typo, a number never pointed
            here) are deleted — any with call or media history are kept; release those instead so their history stays.
          </p>
          <div className="mt-2 flex gap-2">
            <button disabled={busy} onClick={() => void runBulk('delete', confirmDelete)}
              className={`${btn} bg-red-700 hover:bg-red-600 text-white`}>Delete never-used numbers</button>
            <button onClick={() => setConfirmDelete(null)} className="text-gray-400 hover:text-white text-xs">Cancel</button>
          </div>
        </div>
      )}

      {error && <p className="text-red-400 text-sm">{error}</p>}
      {!dir && !error && <p className="text-gray-400 text-sm">Loading…</p>}
      {dir && visible.length === 0 && (
        <p className="text-gray-500 text-sm">
          {q ? `No numbers match "${search}".` : view === VIEW_RESERVE ? 'Reserve is empty.' : view === VIEW_RELEASED ? 'No released numbers.' : 'No numbers here yet.'}
        </p>
      )}

      {visible.length > 0 && (
        <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-800 text-gray-400 text-left">
                <th className="pl-4 py-3 w-8"><input type="checkbox" checked={allVisibleSelected} onChange={toggleAllVisible} /></th>
                <th className="px-3 py-3 font-medium">Number</th>
                <th className="px-3 py-3 font-medium">Label</th>
                <th className="px-3 py-3 font-medium">Campaign</th>
                <th className="px-3 py-3 font-medium">Provider</th>
                <th className="px-3 py-3 font-medium">Script Flow Override</th>
                <th className="px-3 py-3 font-medium">Telephony Flow Override</th>
                <th className="px-3 py-3 font-medium">Status</th>
                <th className="px-3 py-3 font-medium"></th>
              </tr>
            </thead>
            <tbody>
              {visible.map((n) => (
                <Fragment key={n.id}>
                <tr className={`border-b border-gray-800 last:border-0 hover:bg-gray-800/30 ${selected.has(n.id) ? 'bg-indigo-950/30' : ''}`}>
                  <td className="pl-4 py-3"><input type="checkbox" checked={selected.has(n.id)} onChange={() => toggle(n.id)} /></td>
                  <td className="px-3 py-3 text-white font-mono whitespace-nowrap">{n.number}</td>
                  <td className="px-3 py-3 text-gray-400">{n.label ?? <span className="text-gray-600">—</span>}</td>
                  <td className="px-3 py-3 text-xs">
                    {n.campaignName ? (
                      <><span className="text-gray-200">{n.campaignName}</span>{n.clientName && <span className="block text-gray-500">{n.clientName}</span>}</>
                    ) : n.status === 'reserve' ? (
                      <span className="text-sky-300" title="Reserve is handed out oldest first">
                        Reserve · #{n.reserveRank} in line<span className="block text-gray-500">waiting {sinceText(n.reservedAt)}</span>
                      </span>
                    ) : <span className="text-gray-500">—</span>}
                  </td>
                  <td className="px-3 py-3 text-xs">
                    <button type="button" onClick={() => setEditingProviderFor((cur) => cur === n.id ? null : n.id)}
                      className="text-left hover:text-white" title="Edit provider">
                      {n.providerId ? (
                        <>
                          <span className="text-gray-300">{n.providerName ?? 'Unknown provider'}</span>
                          {n.role === 'routing_delivery' && (
                            <span className="block text-amber-400/90">delivery for <span className="font-mono">{n.clientNumber}</span></span>
                          )}
                        </>
                      ) : <span className="text-gray-600">— set provider</span>}
                    </button>
                  </td>
                  <td className="px-3 py-3 w-52">
                    {n.campaignId ? (
                      <SearchableSelect options={scriptFlows.map((f) => ({ value: f.id, label: f.name }))} value={n.flowId ?? ''}
                        onChange={(v) => void rowFlow(n, 'script', v)} allLabel="Campaign default" className="w-full" />
                    ) : <span className="text-gray-600 text-xs">—</span>}
                  </td>
                  <td className="px-3 py-3 w-52">
                    {n.campaignId ? (
                      <SearchableSelect options={inboundFlows.map((f) => ({ value: f.id, label: f.name }))} value={n.telephonyFlowId ?? ''}
                        onChange={(v) => void rowFlow(n, 'telephony', v)} allLabel="Campaign default" className="w-full" />
                    ) : <span className="text-gray-600 text-xs">—</span>}
                  </td>
                  <td className="px-3 py-3">
                    <span title={NUMBER_STATUS[n.status].hint} className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${NUMBER_STATUS[n.status].cls}`}>
                      {NUMBER_STATUS[n.status].label}
                    </span>
                  </td>
                  <td className="px-3 py-3 text-right">
                    <div className="flex items-center justify-end gap-3 whitespace-nowrap">
                      <button onClick={() => setMediaFor(n)} className="text-sky-400 hover:text-sky-300 text-xs font-medium"
                        title="Media agency attribution for calls on this number">Media</button>
                      <button onClick={() => openCallTrace({ dnis: n.number })} className="text-gray-400 hover:text-gray-200 text-xs font-medium">Trace</button>
                      <button disabled={busy}
                        onClick={() => n.isActive ? deactivate([n.id]) : void runBulk('activate', [n.id])}
                        className="text-indigo-400 hover:text-indigo-300 text-xs font-medium">
                        {n.status === 'released' ? 'Reclaim' : n.status === 'reserve' ? 'Release' : n.isActive ? 'Deactivate' : 'Activate'}
                      </button>
                    </div>
                  </td>
                </tr>
                {editingProviderFor === n.id && (
                  <tr className="border-b border-gray-800 bg-gray-950/40">
                    <td colSpan={9} className="px-4 py-3">
                      <PhoneNumberProviderEditor
                        number={{ ...n, label: n.label ?? undefined } as unknown as PhoneNumber}
                        providers={providers}
                        onSaved={() => { setEditingProviderFor(null); void reload() }}
                        onCancel={() => setEditingProviderFor(null)}
                      />
                    </td>
                  </tr>
                )}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

type CampaignOption = NumberDirectory['campaigns'][number]
type Notice = { ok: boolean; text: string; results?: BulkNumberResult[] }

/** Paste any number of numbers (one per line, or comma-separated) into a campaign or Reserve. */
function BulkAddNumbersPanel({ campaigns, campaignLabel, providers, defaultCampaignId, onDone, onClose }: {
  campaigns: CampaignOption[]
  campaignLabel: (c: CampaignOption) => string
  providers: NumberProvider[]
  defaultCampaignId: string
  onDone: (n: Notice) => Promise<void>
  onClose: () => void
}) {
  const [text, setText] = useState('')
  const [campaignId, setCampaignId] = useState(defaultCampaignId)
  const [label, setLabel] = useState('')
  const [provider, setProvider] = useState<ProviderSelection>(EMPTY_PROVIDER)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const lines = text.split(/[\n,;\t]+/).map((s) => s.trim()).filter(Boolean)
  const single = lines.length === 1
  const routing = provider.role === 'routing_delivery'

  async function add() {
    setSaving(true); setError(null)
    try {
      if (single && routing) {
        // A routing delivery number names its own client number — one at a time.
        await createPhoneNumber(campaignId || null, lines[0], label.trim() || undefined,
          { providerId: provider.providerId, role: provider.role, clientNumber: provider.clientNumber.trim() || null })
        await onDone({ ok: true, text: `${lines[0]} added.` })
      } else {
        const r = await bulkAddPhoneNumbers({ numbers: lines, campaignId: campaignId || null, label: label.trim() || null,
          providerId: provider.providerId || null, role: provider.providerId ? provider.role : null })
        const problems = r.results.filter((x) => x.outcome !== 'added' && x.outcome !== 'reacquired')
        await onDone({ ok: problems.length === 0,
          text: `${r.added} of ${r.results.length} number${r.results.length === 1 ? '' : 's'} added${campaignId ? '' : ' to Reserve'}.`,
          results: problems.length ? problems : undefined })
      }
      setText('')
      onClose()
    } catch (e) { setError(e instanceof Error ? e.message : 'Add failed.') }
    finally { setSaving(false) }
  }

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-4 mb-4">
      <p className="text-gray-300 text-sm font-medium mb-1">Add numbers</p>
      <p className="text-gray-500 text-xs mb-3">One per line or comma-separated, any format (+1 503 555 1234, 5035551234…). Leave the campaign
        blank to put them in Reserve.</p>
      <textarea value={text} onChange={(e) => setText(e.target.value)} rows={5} autoFocus
        placeholder={'+15035551234\n+15035551235'}
        className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm font-mono outline-none focus:ring-2 focus:ring-indigo-500 mb-3" />
      <div className="flex items-center gap-3 flex-wrap">
        <select value={campaignId} onChange={(e) => setCampaignId(e.target.value)}
          className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500">
          <option value="">Reserve (no campaign)</option>
          {campaigns.map((c) => <option key={c.id} value={c.id}>{campaignLabel(c)}</option>)}
        </select>
        <input value={label} onChange={(e) => setLabel(e.target.value)} placeholder="Label (optional)"
          className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-44" />
        <ProviderFields providers={providers} value={provider} onChange={setProvider} />
        <button onClick={() => void add()}
          disabled={saving || lines.length === 0 || (routing && (!single || !providerSelectionValid(provider)))}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium">
          {saving ? 'Adding…' : `Add ${lines.length || ''}`.trim()}
        </button>
        <button onClick={onClose} className="text-gray-500 hover:text-white text-sm">Cancel</button>
      </div>
      {routing && !single && lines.length > 0 && (
        <p className="text-amber-300 text-xs mt-2">Routing delivery numbers each stand for their own client number — add them one at a time.</p>
      )}
      {error && <p className="text-red-400 text-xs mt-2">{error}</p>}
    </div>
  )
}

/** Hands out the longest-waiting Reserve numbers to a campaign. */
function AssignFromReservePanel({ campaigns, campaignLabel, reserve, defaultCampaignId, onDone, onClose }: {
  campaigns: CampaignOption[]
  campaignLabel: (c: CampaignOption) => string
  reserve: NumberRow[]
  defaultCampaignId: string
  onDone: (n: Notice) => Promise<void>
  onClose: () => void
}) {
  const [campaignId, setCampaignId] = useState(defaultCampaignId)
  const [count, setCount] = useState(1)
  const [label, setLabel] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const next = reserve.slice(0, Math.max(0, count))

  async function assign() {
    setSaving(true); setError(null)
    try {
      const r = await assignFromReserve(campaignId, count, label.trim() || null)
      const name = campaigns.find((c) => c.id === campaignId)?.name ?? 'the campaign'
      await onDone({ ok: r.shortBy === 0,
        text: `${r.assigned.length} number${r.assigned.length === 1 ? '' : 's'} assigned to ${name}: ${r.assigned.join(', ')}` +
          (r.shortBy > 0 ? ` — Reserve was ${r.shortBy} short.` : '') })
      onClose()
    } catch (e) { setError(e instanceof Error ? e.message : 'Assign failed.') }
    finally { setSaving(false) }
  }

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-4 mb-4">
      <p className="text-gray-300 text-sm font-medium mb-1">Assign from Reserve</p>
      <p className="text-gray-500 text-xs mb-3">
        {reserve.length} number{reserve.length === 1 ? '' : 's'} in Reserve. The longest-waiting go first, so drag calls from a recently
        retired campaign have died down before a number is reused.
      </p>
      <div className="flex items-center gap-3 flex-wrap">
        <select value={campaignId} onChange={(e) => setCampaignId(e.target.value)}
          className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500">
          <option value="">Select campaign *</option>
          {campaigns.map((c) => <option key={c.id} value={c.id}>{campaignLabel(c)}</option>)}
        </select>
        <label className="text-xs text-gray-400 flex items-center gap-2">How many
          <input type="number" min={1} max={Math.max(1, reserve.length)} value={count}
            onChange={(e) => setCount(Math.max(1, Number(e.target.value) || 1))}
            className="w-20 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500" />
        </label>
        <input value={label} onChange={(e) => setLabel(e.target.value)} placeholder="Label (optional)"
          className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-44" />
        <button onClick={() => void assign()} disabled={saving || !campaignId || reserve.length === 0}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium">
          {saving ? 'Assigning…' : 'Assign'}
        </button>
        <button onClick={onClose} className="text-gray-500 hover:text-white text-sm">Cancel</button>
      </div>
      {next.length > 0 && (
        <p className="text-xs text-gray-500 mt-2">
          Next out: {next.map((n) => <span key={n.id} className="font-mono text-gray-300 mr-2">{n.number} <span className="text-gray-500">({sinceText(n.reservedAt)})</span></span>)}
          {count > reserve.length && <span className="text-amber-300">— only {reserve.length} available</span>}
        </p>
      )}
      {error && <p className="text-red-400 text-xs mt-2">{error}</p>}
    </div>
  )
}

// ── Number providers ──────────────────────────────────────────────────────────

interface ProviderSelection { providerId: string; role: PhoneNumberRole; clientNumber: string }
const EMPTY_PROVIDER: ProviderSelection = { providerId: '', role: 'hosted', clientNumber: '' }

function providerSelectionValid(p: ProviderSelection) {
  return p.role !== 'routing_delivery' || (!!p.providerId && !!p.clientNumber.trim())
}

const PROVIDER_TYPE_LABEL: Record<NumberProviderType, string> = {
  carrier: 'Carrier',
  routing_platform: 'Routing platform',
}

/** Provider + role + client number inputs, shared by "Add DID" and the per-number editor. A
 *  routing-platform provider implies a routing delivery number (pseudo-DNIS) that stands for a
 *  client number the platform houses. */
function ProviderFields({ providers, value, onChange }: {
  providers: NumberProvider[]
  value: ProviderSelection
  onChange: (v: ProviderSelection) => void
}) {
  const selected = providers.find((p) => p.id === value.providerId)
  const inputCls = 'bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500'
  return (
    <>
      <select
        value={value.providerId}
        onChange={(e) => {
          const p = providers.find((x) => x.id === e.target.value)
          onChange({
            providerId: e.target.value,
            role: p?.type === 'routing_platform' ? 'routing_delivery' : 'hosted',
            clientNumber: p?.type === 'routing_platform' ? value.clientNumber : '',
          })
        }}
        className={inputCls}
      >
        <option value="">Provider (optional)</option>
        {providers.filter((p) => p.isActive || p.id === value.providerId).map((p) => (
          <option key={p.id} value={p.id}>{p.name} — {PROVIDER_TYPE_LABEL[p.type]}</option>
        ))}
      </select>
      {selected?.type === 'routing_platform' && (
        <input
          value={value.clientNumber}
          onChange={(e) => onChange({ ...value, clientNumber: e.target.value })}
          placeholder="Client number it stands for *"
          title="The public number callers dial, housed at the routing platform"
          className={`${inputCls} w-56 ${!value.clientNumber.trim() ? 'ring-2 ring-amber-500/60' : ''}`}
        />
      )}
    </>
  )
}

function PhoneNumberProviderEditor({ number, providers, onSaved, onCancel }: {
  number: PhoneNumber
  providers: NumberProvider[]
  onSaved: (updated: PhoneNumber) => void
  onCancel: () => void
}) {
  const [value, setValue] = useState<ProviderSelection>({
    providerId: number.providerId ?? '',
    role: number.role ?? 'hosted',
    clientNumber: number.clientNumber ?? '',
  })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function save() {
    setSaving(true); setError(null)
    try {
      onSaved(await updatePhoneNumberProvider(number.id, value.providerId || null, value.role,
        value.role === 'routing_delivery' ? value.clientNumber.trim() : null))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      <p className="text-gray-500 text-xs mb-2">
        Who houses <span className="font-mono text-gray-300">{number.number}</span>. For a routing platform
        (e.g. RingSquared), this number is where they deliver calls — enter the public client number it stands for.
      </p>
      <div className="flex items-center gap-2 flex-wrap">
        <ProviderFields providers={providers} value={value} onChange={setValue} />
        <button
          onClick={save}
          disabled={saving || !providerSelectionValid(value)}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium"
        >
          {saving ? 'Saving…' : 'Save'}
        </button>
        <button onClick={onCancel} className="text-gray-500 hover:text-white text-sm">Cancel</button>
      </div>
      {error && <p className="text-red-400 text-xs mt-2">{error}</p>}
    </div>
  )
}

function NumberProvidersTab() {
  const [providers, setProviders] = useState<NumberProvider[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<NumberProvider | 'new' | null>(null)
  const [form, setForm] = useState({ name: '', type: 'carrier' as NumberProviderType, sourceIps: '', notes: '' })
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [issuedKey, setIssuedKey] = useState<{ providerName: string; key: string } | null>(null)
  const [confirmRevoke, setConfirmRevoke] = useState<string | null>(null)

  useEffect(() => {
    listNumberProviders()
      .then(setProviders)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  function openForm(p: NumberProvider | 'new') {
    setEditing(p)
    setFormError(null)
    setForm(p === 'new'
      ? { name: '', type: 'carrier', sourceIps: '', notes: '' }
      : { name: p.name, type: p.type, sourceIps: p.sourceIps ?? '', notes: p.notes ?? '' })
  }

  function replace(updated: NumberProvider) {
    setProviders((prev) => prev.some((p) => p.id === updated.id)
      ? prev.map((p) => p.id === updated.id ? updated : p)
      : [...prev, updated].sort((a, b) => a.name.localeCompare(b.name)))
  }

  async function save() {
    if (!editing || !form.name.trim()) return
    setSaving(true); setFormError(null)
    try {
      const body = { name: form.name.trim(), type: form.type, sourceIps: form.sourceIps.trim(), notes: form.notes.trim() }
      replace(editing === 'new' ? await createNumberProvider(body) : await updateNumberProvider(editing.id, body))
      setEditing(null)
    } catch (e) {
      setFormError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  async function issueKey(p: NumberProvider) {
    try {
      const res = await issueNumberProviderApiKey(p.id)
      replace(res.provider)
      setIssuedKey({ providerName: p.name, key: res.apiKey })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not issue key.')
    }
  }

  const inputCls = 'bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500'

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <p className="text-gray-500 text-sm max-w-2xl">
          Who houses your numbers. <span className="text-gray-300">Carriers</span> (Telnyx, Bandwidth) host numbers and
          hand us the calls; <span className="text-gray-300">routing platforms</span> (e.g. RingSquared) house the public
          numbers and deliver calls to a delivery number of ours — set on the Phone Numbers tab.
        </p>
        <button
          onClick={() => openForm('new')}
          className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors shrink-0"
        >
          Add provider
        </button>
      </div>

      {issuedKey && (
        <div className="bg-emerald-950/40 border border-emerald-800 rounded-xl p-4 mb-4">
          <p className="text-emerald-300 text-sm font-medium">API key for {issuedKey.providerName}</p>
          <p className="text-gray-400 text-xs mt-1">
            Copy it now — it is not stored and won't be shown again. The provider sends it in the
            <span className="font-mono text-gray-300"> x-api-key</span> header when calling your routing endpoints.
          </p>
          <p className="font-mono text-sm text-white bg-gray-950 rounded-lg px-3 py-2 mt-2 break-all select-all">{issuedKey.key}</p>
          <button onClick={() => setIssuedKey(null)} className="text-gray-400 hover:text-white text-xs mt-2">I've copied it — dismiss</button>
        </div>
      )}

      {editing && (
        <div className="bg-gray-900 border border-gray-800 rounded-xl p-4 mb-4">
          <p className="text-gray-300 text-sm font-medium mb-3">{editing === 'new' ? 'New provider' : `Edit ${editing.name}`}</p>
          <div className="flex items-center gap-3 flex-wrap">
            <input autoFocus value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })}
              placeholder="Name * (e.g. RingSquared)" className={`${inputCls} w-56`} />
            <select value={form.type} onChange={(e) => setForm({ ...form, type: e.target.value as NumberProviderType })} className={inputCls}>
              <option value="carrier">Carrier</option>
              <option value="routing_platform">Routing platform</option>
            </select>
            <input value={form.sourceIps} onChange={(e) => setForm({ ...form, sourceIps: e.target.value })}
              placeholder="Source IPs (optional, comma-separated)" className={`${inputCls} w-72`} />
          </div>
          <textarea value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })}
            placeholder="Notes (SIP connection details, contacts, …)" rows={2}
            className={`${inputCls} w-full mt-3 resize-y`} />
          <div className="flex items-center gap-3 mt-3">
            <button onClick={save} disabled={saving || !form.name.trim()}
              className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium">
              {saving ? 'Saving…' : 'Save'}
            </button>
            <button onClick={() => setEditing(null)} className="text-gray-500 hover:text-white text-sm">Cancel</button>
            {formError && <span className="text-red-400 text-xs">{formError}</span>}
          </div>
        </div>
      )}

      {loading && <p className="text-gray-400 text-sm">Loading…</p>}
      {error && <p className="text-red-400 text-sm mb-2">{error}</p>}
      {!loading && providers.length === 0 && <p className="text-gray-500 text-sm">No providers yet.</p>}

      {providers.length > 0 && (
        <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-800 text-gray-400 text-left">
                <th className="px-4 py-3 font-medium">Name</th>
                <th className="px-4 py-3 font-medium">Type</th>
                <th className="px-4 py-3 font-medium">Routing API key</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3 font-medium"></th>
              </tr>
            </thead>
            <tbody>
              {providers.map((p) => (
                <tr key={p.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30">
                  <td className="px-4 py-3 text-white font-medium">
                    {p.name}
                    {p.notes && <span className="block text-gray-500 text-xs font-normal truncate max-w-xs">{p.notes}</span>}
                  </td>
                  <td className="px-4 py-3 text-gray-400">{PROVIDER_TYPE_LABEL[p.type]}</td>
                  <td className="px-4 py-3 text-xs">
                    {p.type !== 'routing_platform' ? (
                      <span className="text-gray-600">n/a</span>
                    ) : p.hasApiKey ? (
                      <span className="text-gray-300 font-mono">{p.apiKeyPrefix}…</span>
                    ) : (
                      <span className="text-gray-500">none issued</span>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${p.isActive ? STATUS_COLORS.active : STATUS_COLORS.inactive}`}>
                      {p.isActive ? 'active' : 'inactive'}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right whitespace-nowrap">
                    <button onClick={() => openForm(p)} className="text-gray-400 hover:text-white text-xs font-medium mr-4">Edit</button>
                    {p.type === 'routing_platform' && (
                      <button onClick={() => issueKey(p)} className="text-gray-400 hover:text-white text-xs font-medium mr-4">
                        {p.hasApiKey ? 'Rotate key' : 'Issue key'}
                      </button>
                    )}
                    {p.hasApiKey && confirmRevoke !== p.id && (
                      <button onClick={() => setConfirmRevoke(p.id)} className="text-gray-500 hover:text-red-400 text-xs font-medium mr-4">Revoke key</button>
                    )}
                    {confirmRevoke === p.id && (
                      <span className="text-xs text-gray-400 mr-4">
                        Revoke?{' '}
                        <button onClick={async () => { replace(await revokeNumberProviderApiKey(p.id)); setConfirmRevoke(null) }}
                          className="text-red-400 hover:text-red-300 font-medium">Yes</button>{' '}
                        <button onClick={() => setConfirmRevoke(null)} className="text-gray-500 hover:text-white">No</button>
                      </span>
                    )}
                    <button
                      onClick={async () => replace(p.isActive ? await deactivateNumberProvider(p.id) : await activateNumberProvider(p.id))}
                      className="text-indigo-400 hover:text-indigo-300 text-xs font-medium"
                    >
                      {p.isActive ? 'Deactivate' : 'Activate'}
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
  const [allAgents, setAllAgents] = useState<AgentRecord[]>([])

  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [detail, setDetail] = useState<AgentGroupDetail | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)

  const [showCreate, setShowCreate] = useState(false)
  const [newName, setNewName] = useState('')
  const [newSlug, setNewSlug] = useState('')
  const [newDesc, setNewDesc] = useState('')
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  const [selectedAgentId, setSelectedAgentId] = useState('')
  const [memberError, setMemberError] = useState<string | null>(null)
  const [memberAdding, setMemberAdding] = useState(false)

  useEffect(() => {
    Promise.all([listAgentGroups(), listAdminAgents()])
      .then(([g, a]) => { setGroups(g); setAllAgents(a) })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  async function handleExpand(group: AgentGroup) {
    if (expandedId === group.id) { setExpandedId(null); setDetail(null); return }
    setExpandedId(group.id)
    setDetail(null)
    setSelectedAgentId('')
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
    if (!expandedId || !selectedAgentId) return
    setMemberAdding(true)
    setMemberError(null)
    try {
      const m = await addGroupMember(expandedId, selectedAgentId)
      setDetail((prev) => prev ? { ...prev, members: [...prev.members, m] } : prev)
      setGroups((prev) => prev.map((g) => g.id === expandedId ? { ...g, memberCount: g.memberCount + 1 } : g))
      setSelectedAgentId('')
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
      setGroups((prev) => prev.map((g) => g.id === groupId ? { ...g, memberCount: Math.max(0, g.memberCount - 1) } : g))
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
                <span className="text-gray-400 text-xs w-4">{expandedId === g.id ? <ChevronDownIcon size={13} /> : <ChevronRightIcon size={13} />}</span>
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
                      {(() => {
                        const memberIds = new Set(detail.members.map((m) => m.agentId))
                        const eligible = allAgents.filter((a) => a.isActive && !memberIds.has(a.id))
                        return (
                          <div className="flex items-center gap-3 mb-4">
                            <SearchableSelect
                              options={eligible.map((a) => ({
                                value: a.id,
                                label: `${a.firstName} ${a.lastName}`,
                                sublabel: a.email,
                              }))}
                              value={selectedAgentId}
                              onChange={(v) => { setSelectedAgentId(v); setMemberError(null) }}
                              placeholder="Select agent…"
                              className="w-72"
                            />
                            <button
                              onClick={handleAddMember}
                              disabled={memberAdding || !selectedAgentId}
                              className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-3 py-1.5 text-sm font-medium transition-colors"
                            >
                              {memberAdding ? 'Adding…' : 'Add member'}
                            </button>
                            {memberError && <span className="text-red-400 text-xs">{memberError}</span>}
                          </div>
                        )
                      })()}

                      {detail.members.length === 0 ? (
                        <p className="text-gray-500 text-xs">No members yet.</p>
                      ) : (
                        <div className="space-y-1">
                          {detail.members.map((m) => {
                            const agent = allAgents.find((a) => a.id === m.agentId)
                            return (
                              <div key={m.agentId} className="flex items-center gap-3 text-sm">
                                <span className="text-gray-200 text-sm">
                                  {agent ? `${agent.firstName} ${agent.lastName}` : m.agentId}
                                </span>
                                {agent && <span className="text-gray-500 text-xs">{agent.email}</span>}
                                <span className="text-gray-600 text-xs">
                                  · joined {new Date(m.joinedAt).toLocaleDateString()}
                                </span>
                                <button
                                  onClick={() => handleRemoveMember(g.id, m.agentId)}
                                  className="text-red-500 hover:text-red-400 text-xs ml-auto"
                                >
                                  Remove
                                </button>
                              </div>
                            )
                          })}
                        </div>
                      )}

                      <GroupRoutingPanel groupId={g.id} agents={allAgents} membersVersion={detail.members.length} />
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

// ── Test Call Tab ─────────────────────────────────────────────────────────────

function TestCallTab() {
  const [ani, setAni]         = useState('5416704541')
  const [did, setDid]         = useState('18001234567')
  const [sipHost, setSipHost] = useState('172.19.0.5')
  const [sipPort, setSipPort] = useState('5060')
  const [busy, setBusy]       = useState(false)
  const [result, setResult]   = useState<{ success: boolean; result: string; command: string } | null>(null)
  const [error, setError]     = useState<string | null>(null)

  const command = `originate {origination_caller_id_number=${ani}}sofia/internal/${did}@${sipHost}:${sipPort} &park()`

  async function handleOriginate() {
    setBusy(true)
    setResult(null)
    setError(null)
    try {
      const data = await api.post<{ success: boolean; result: string; command: string }>(
        '/api/v1/telephony/originate-test',
        { callerIdNumber: ani, destinationNumber: did, sipHost, sipPort },
      )
      setResult(data)
    } catch (e) {
      // api.post throws on non-2xx — parse the body out of the error message
      const msg = e instanceof Error ? e.message : 'Request failed.'
      // Try to extract FreeSWITCH response from the error text
      const jsonMatch = msg.match(/\{.+\}/)
      if (jsonMatch) {
        try {
          const data = JSON.parse(jsonMatch[0]) as { success: boolean; result: string; command: string }
          setResult(data)
          return
        } catch {}
      }
      setError(msg)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="max-w-2xl">
      <p className="text-gray-500 text-sm mb-6">
        Simulate an inbound call by originating a test leg through FreeSWITCH. The call will park and
        flow through the inbound telephony flow configured for the destination DID.
      </p>

      <div className="bg-gray-900 border border-gray-800 rounded-xl p-5 space-y-4">
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-gray-400 text-xs font-medium mb-1.5">Caller ANI (from)</label>
            <input
              value={ani}
              onChange={(e) => setAni(e.target.value)}
              placeholder="5416704541"
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 font-mono"
            />
          </div>
          <div>
            <label className="block text-gray-400 text-xs font-medium mb-1.5">Destination DID (to)</label>
            <input
              value={did}
              onChange={(e) => setDid(e.target.value)}
              placeholder="18001234567"
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 font-mono"
            />
          </div>
          <div>
            <label className="block text-gray-400 text-xs font-medium mb-1.5">FreeSWITCH SIP Host</label>
            <input
              value={sipHost}
              onChange={(e) => setSipHost(e.target.value)}
              placeholder="172.19.0.5"
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 font-mono"
            />
          </div>
          <div>
            <label className="block text-gray-400 text-xs font-medium mb-1.5">SIP Port</label>
            <input
              value={sipPort}
              onChange={(e) => setSipPort(e.target.value)}
              placeholder="5060"
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 font-mono"
            />
          </div>
        </div>

        {/* Command preview */}
        <div>
          <label className="block text-gray-500 text-xs font-medium mb-1.5">Command preview</label>
          <div className="bg-gray-950 rounded-lg px-3 py-2.5 text-xs font-mono text-gray-300 break-all border border-gray-800">
            {command}
          </div>
        </div>

        <button
          onClick={handleOriginate}
          disabled={busy || !ani.trim() || !did.trim() || !sipHost.trim()}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-5 py-2.5 text-sm font-medium transition-colors"
        >
          {busy ? 'Originating…' : 'Originate Call'}
        </button>
      </div>

      {/* Result */}
      {(result || error) && (
        <div className={`mt-4 bg-gray-900 border rounded-xl p-5 space-y-3 ${result?.success ? 'border-emerald-800' : 'border-red-900'}`}>
          <div className="flex items-center gap-2">
            <span className={`text-sm font-semibold ${result?.success ? 'text-emerald-400' : 'text-red-400'}`}>
              {result?.success ? <><CheckIcon size={12} className="inline -mt-0.5 mr-1" />Originated successfully</> : <><CloseIcon size={12} className="inline -mt-0.5 mr-1" />Origination failed</>}
            </span>
          </div>
          {result?.result && (
            <div>
              <p className="text-gray-500 text-xs mb-1">FreeSWITCH response</p>
              <div className="bg-gray-950 rounded-lg px-3 py-2 text-xs font-mono text-gray-200 border border-gray-800">
                {result.result}
              </div>
            </div>
          )}
          {result?.command && (
            <div>
              <p className="text-gray-500 text-xs mb-1">Command sent</p>
              <div className="bg-gray-950 rounded-lg px-3 py-2 text-xs font-mono text-gray-400 break-all border border-gray-800">
                {result.command}
              </div>
            </div>
          )}
          {error && !result && (
            <p className="text-red-400 text-sm">{error}</p>
          )}
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
        {tab === 'providers'     && <NumberProvidersTab />}
        {tab === 'agent-groups'  && <AgentGroupsTab />}
        {tab === 'assignments'   && <AssignmentGrid />}
        {tab === 'test-call'     && <TestCallTab />}
      </div>
    </AdminShell>
  )
}
