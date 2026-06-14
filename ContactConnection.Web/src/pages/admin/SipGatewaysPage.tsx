import { useEffect, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import { api } from '../../api/client'
import {
  listSipGateways, createSipGateway, activateSipGateway, deactivateSipGateway,
  type SipGateway,
} from '../../api/telephony'

const TRANSPORT_OPTIONS = ['udp', 'tcp', 'tls']

export default function SipGatewaysPage() {
  const [tenantId, setTenantId] = useState<string | null>(null)
  const [gateways, setGateways] = useState<SipGateway[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [showCreate, setShowCreate] = useState(false)
  const [form, setForm] = useState({
    name: '',
    proxy: '',
    username: '',
    password: '',
    fromDomain: '',
    register: true,
    transport: 'udp',
  })
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  useEffect(() => {
    api.get<{ id: string }>('/api/v1/tenants/me')
      .then((t) => {
        setTenantId(t.id)
        return listSipGateways(t.id)
      })
      .then(setGateways)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  function setField<K extends keyof typeof form>(key: K, value: typeof form[K]) {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  function resetForm() {
    setForm({ name: '', proxy: '', username: '', password: '', fromDomain: '', register: true, transport: 'udp' })
    setCreateError(null)
  }

  async function handleCreate() {
    if (!tenantId || !form.name.trim() || !form.proxy.trim() || !form.username.trim() || !form.password) return
    setCreating(true)
    setCreateError(null)
    try {
      const gw = await createSipGateway({
        tenantId,
        name: form.name.trim(),
        proxy: form.proxy.trim(),
        username: form.username.trim(),
        password: form.password,
        register: form.register,
        transport: form.transport,
        fromDomain: form.fromDomain.trim() || undefined,
      })
      setGateways((prev) => [...prev, gw])
      resetForm()
      setShowCreate(false)
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : 'Create failed.')
    } finally {
      setCreating(false)
    }
  }

  async function handleToggle(gw: SipGateway) {
    try {
      const updated = gw.isActive
        ? await deactivateSipGateway(gw.id)
        : await activateSipGateway(gw.id)
      setGateways((prev) => prev.map((g) => g.id === gw.id ? updated : g))
    } catch {}
  }

  return (
    <AdminShell>
      <div className="p-6 max-w-5xl">
        <div className="flex items-start justify-between mb-6">
          <div>
            <h1 className="text-white text-xl font-semibold">SIP Gateways</h1>
            <p className="text-gray-500 text-sm mt-0.5">
              FreeSWITCH SIP trunk configurations. Each gateway maps to a specific SIP provider
              and provides network-layer tenant identity on inbound calls.
            </p>
          </div>
          <button
            onClick={() => { setShowCreate((v) => !v); resetForm() }}
            className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors shrink-0 ml-4"
          >
            Add gateway
          </button>
        </div>

        {showCreate && (
          <div className="bg-gray-900 border border-gray-800 rounded-xl p-5 mb-6">
            <p className="text-gray-300 text-sm font-medium mb-4">New SIP gateway</p>
            <div className="grid grid-cols-2 gap-3 mb-4">
              <div className="flex flex-col gap-1">
                <label className="text-gray-500 text-xs">Gateway name *</label>
                <input
                  autoFocus
                  value={form.name}
                  onChange={(e) => setField('name', e.target.value)}
                  placeholder="e.g. twilio-tms"
                  className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-gray-500 text-xs">SIP proxy / host *</label>
                <input
                  value={form.proxy}
                  onChange={(e) => setField('proxy', e.target.value)}
                  placeholder="sip.twilio.com"
                  className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-gray-500 text-xs">Username *</label>
                <input
                  value={form.username}
                  onChange={(e) => setField('username', e.target.value)}
                  className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-gray-500 text-xs">Password *</label>
                <input
                  type="password"
                  value={form.password}
                  onChange={(e) => setField('password', e.target.value)}
                  className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-gray-500 text-xs">From domain (optional)</label>
                <input
                  value={form.fromDomain}
                  onChange={(e) => setField('fromDomain', e.target.value)}
                  placeholder="Defaults to proxy"
                  className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-gray-500 text-xs">Transport</label>
                <select
                  value={form.transport}
                  onChange={(e) => setField('transport', e.target.value)}
                  className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                >
                  {TRANSPORT_OPTIONS.map((t) => <option key={t} value={t}>{t.toUpperCase()}</option>)}
                </select>
              </div>
            </div>

            <label className="flex items-center gap-2 mb-4 cursor-pointer">
              <input
                type="checkbox"
                checked={form.register}
                onChange={(e) => setField('register', e.target.checked)}
                className="rounded border-gray-600 bg-gray-800 text-indigo-500 focus:ring-indigo-500"
              />
              <span className="text-gray-300 text-sm">Register with SIP provider</span>
            </label>

            <div className="flex items-center gap-3">
              <button
                onClick={handleCreate}
                disabled={creating || !form.name.trim() || !form.proxy.trim() || !form.username.trim() || !form.password}
                className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
              >
                {creating ? 'Creating…' : 'Create gateway'}
              </button>
              <button
                onClick={() => { setShowCreate(false); resetForm() }}
                className="text-gray-500 hover:text-white text-sm"
              >
                Cancel
              </button>
            </div>
            {createError && <p className="text-red-400 text-xs mt-2">{createError}</p>}
          </div>
        )}

        {loading && <p className="text-gray-400 text-sm">Loading…</p>}
        {error && <p className="text-red-400 text-sm">{error}</p>}
        {!loading && !error && gateways.length === 0 && (
          <p className="text-gray-500 text-sm">No SIP gateways configured yet.</p>
        )}

        {gateways.length > 0 && (
          <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 text-gray-400 text-left">
                  <th className="px-4 py-3 font-medium">Name</th>
                  <th className="px-4 py-3 font-medium">Proxy</th>
                  <th className="px-4 py-3 font-medium">Username</th>
                  <th className="px-4 py-3 font-medium">Transport</th>
                  <th className="px-4 py-3 font-medium">Register</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="px-4 py-3 font-medium"></th>
                </tr>
              </thead>
              <tbody>
                {gateways.map((gw) => (
                  <tr key={gw.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30">
                    <td className="px-4 py-3 text-white font-medium font-mono text-xs">{gw.name}</td>
                    <td className="px-4 py-3 text-gray-300 text-xs">{gw.proxy}</td>
                    <td className="px-4 py-3 text-gray-400 text-xs">{gw.username}</td>
                    <td className="px-4 py-3 text-gray-400 text-xs uppercase">{gw.transport}</td>
                    <td className="px-4 py-3 text-gray-400 text-xs">{gw.register ? 'Yes' : 'No'}</td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${gw.isActive ? 'bg-emerald-900/50 text-emerald-400' : 'bg-gray-700/60 text-gray-400'}`}>
                        {gw.isActive ? 'Active' : 'Inactive'}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-right">
                      <button
                        onClick={() => handleToggle(gw)}
                        className="text-indigo-400 hover:text-indigo-300 text-xs font-medium"
                      >
                        {gw.isActive ? 'Deactivate' : 'Activate'}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </AdminShell>
  )
}
