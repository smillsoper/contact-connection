import { Fragment, useEffect, useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import SearchableSelect from '../../components/SearchableSelect'
import GroupRoutingPanel from '../../components/admin/GroupRoutingPanel'
import MediaAssignmentsModal from '../../components/admin/MediaAssignmentsModal'
import {
  listClients, createClient, activateClient, deactivateClient,
  getOrderNumberSequence, putOrderNumberSequence, deleteOrderNumberSequence,
  listCampaigns, createCampaign, activateCampaign, pauseCampaign, deactivateCampaign,
  listPhoneNumbers, createPhoneNumber, activatePhoneNumber, deactivatePhoneNumber, updatePhoneNumberProvider,
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

type Tab = 'clients' | 'campaigns' | 'phone-numbers' | 'providers' | 'agent-groups' | 'test-call'

const TABS: { id: Tab; label: string }[] = [
  { id: 'clients',       label: 'Clients' },
  { id: 'campaigns',     label: 'Campaigns' },
  { id: 'phone-numbers', label: 'Phone Numbers' },
  { id: 'providers',     label: 'Number Providers' },
  { id: 'agent-groups',  label: 'Agent Groups' },
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
function DefaultOutboundCallerId({ numbers }: { numbers: PhoneNumber[] }) {
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

function PhoneNumbersTab() {
  const [campaigns, setCampaigns] = useState<Campaign[]>([])
  const [numbers, setNumbers] = useState<PhoneNumber[]>([])
  const [mediaFor, setMediaFor] = useState<PhoneNumber | null>(null)
  const [scriptFlows, setScriptFlows] = useState<FlowSummary[]>([])
  const [inboundFlows, setInboundFlows] = useState<FlowSummary[]>([])
  const [selectedCampaignId, setSelectedCampaignId] = useState('')
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [numLoading, setNumLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [showCreate, setShowCreate] = useState(false)
  const [newCampaignId, setNewCampaignId] = useState('')
  const [newNumber, setNewNumber] = useState('')
  const [newLabel, setNewLabel] = useState('')
  const [newProvider, setNewProvider] = useState<ProviderSelection>(EMPTY_PROVIDER)
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)
  const [providers, setProviders] = useState<NumberProvider[]>([])
  const [editingProviderFor, setEditingProviderFor] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([
      listCampaigns(),
      flowsApi.listAll(),
      listNumberProviders().catch(() => [] as NumberProvider[]),
    ])
      .then(([c, allFlows, provs]) => {
        setCampaigns(c)
        setProviders(provs)
        setScriptFlows(allFlows.filter((f) => f.flow_type === 'crm'))
        setInboundFlows(allFlows.filter((f) => f.flow_type === 'telephony' && f.flow_direction === 'inbound'))
      })
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
      const pn = await createPhoneNumber(newCampaignId, newNumber.trim(), newLabel.trim() || undefined,
        newProvider.providerId
          ? { providerId: newProvider.providerId, role: newProvider.role, clientNumber: newProvider.clientNumber.trim() || null }
          : undefined)
      if (pn.campaignId === selectedCampaignId) setNumbers((prev) => [...prev, pn])
      setNewNumber(''); setNewLabel(''); setNewProvider(EMPTY_PROVIDER); setShowCreate(false)
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

  async function handleFlowChange(pn: PhoneNumber, flowId: string) {
    try {
      const updated = flowId
        ? await setPhoneNumberFlow(pn.id, flowId)
        : await removePhoneNumberFlow(pn.id)
      setNumbers((prev) => prev.map((n) => n.id === pn.id ? updated : n))
    } catch {}
  }

  async function handleTelephonyFlowChange(pn: PhoneNumber, flowId: string) {
    try {
      const updated = flowId
        ? await setPhoneNumberTelephonyFlow(pn.id, flowId)
        : await removePhoneNumberTelephonyFlow(pn.id)
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
      {mediaFor && (
        <MediaAssignmentsModal phoneNumberId={mediaFor.id} number={mediaFor.number}
          clientNumber={mediaFor.clientNumber} onClose={() => setMediaFor(null)} />
      )}
      <DefaultOutboundCallerId numbers={numbers} />
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
            <ProviderFields providers={providers} value={newProvider} onChange={setNewProvider} />
            <button
              onClick={handleCreate}
              disabled={creating || !newCampaignId || !newNumber.trim() || !providerSelectionValid(newProvider)}
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
                <th className="px-4 py-3 font-medium">Provider</th>
                <th className="px-4 py-3 font-medium">Script Flow Override</th>
                <th className="px-4 py-3 font-medium">Telephony Flow Override</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3 font-medium"></th>
              </tr>
            </thead>
            <tbody>
              {visibleNumbers.map((n) => (
                <Fragment key={n.id}>
                <tr className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30">
                  <td className="px-4 py-3 text-white font-mono">{n.number}</td>
                  <td className="px-4 py-3 text-gray-400">{n.label ?? <span className="text-gray-600">—</span>}</td>
                  <td className="px-4 py-3 text-xs">
                    <button
                      type="button"
                      onClick={() => setEditingProviderFor((cur) => cur === n.id ? null : n.id)}
                      className="text-left hover:text-white"
                      title="Edit provider"
                    >
                      {n.providerId ? (
                        <>
                          <span className="text-gray-300">{providers.find((p) => p.id === n.providerId)?.name ?? 'Unknown provider'}</span>
                          {n.role === 'routing_delivery' && (
                            <span className="block text-amber-400/90">
                              delivery for <span className="font-mono">{n.clientNumber}</span>
                            </span>
                          )}
                        </>
                      ) : (
                        <span className="text-gray-600">— set provider</span>
                      )}
                    </button>
                  </td>
                  <td className="px-4 py-3 w-56">
                    <SearchableSelect
                      options={scriptFlows.map((f) => ({ value: f.id, label: f.name }))}
                      value={n.flowId ?? ''}
                      onChange={(v) => handleFlowChange(n, v)}
                      allLabel="Campaign default"
                      className="w-full"
                    />
                  </td>
                  <td className="px-4 py-3 w-56">
                    <SearchableSelect
                      options={inboundFlows.map((f) => ({ value: f.id, label: f.name }))}
                      value={n.telephonyFlowId ?? ''}
                      onChange={(v) => handleTelephonyFlowChange(n, v)}
                      allLabel="Campaign default"
                      className="w-full"
                    />
                  </td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${n.isActive ? STATUS_COLORS.active : STATUS_COLORS.inactive}`}>
                      {n.isActive ? 'active' : 'inactive'}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <div className="flex items-center justify-end gap-3">
                      <button
                        onClick={() => setMediaFor(n)}
                        className="text-sky-400 hover:text-sky-300 text-xs font-medium"
                        title="Media agency attribution for calls on this number"
                      >
                        Media
                      </button>
                      <button
                        onClick={() => openCallTrace({ dnis: n.number })}
                        className="text-gray-400 hover:text-gray-200 text-xs font-medium"
                      >
                        Trace
                      </button>
                      <button
                        onClick={() => handleToggle(n)}
                        className="text-indigo-400 hover:text-indigo-300 text-xs font-medium"
                      >
                        {n.isActive ? 'Deactivate' : 'Activate'}
                      </button>
                    </div>
                  </td>
                </tr>
                {editingProviderFor === n.id && (
                  <tr className="border-b border-gray-800 bg-gray-950/40">
                    <td colSpan={8} className="px-4 py-3">
                      <PhoneNumberProviderEditor
                        number={n}
                        providers={providers}
                        onSaved={(updated) => {
                          setNumbers((prev) => prev.map((x) => x.id === updated.id ? updated : x))
                          setEditingProviderFor(null)
                        }}
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
              {result?.success ? '✓ Originated successfully' : '✗ Origination failed'}
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
        {tab === 'test-call'     && <TestCallTab />}
      </div>
    </AdminShell>
  )
}
