import { useEffect, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import { useAuthStore } from '../../stores/authStore'
import { blockListApi, type BlockListEntry } from '../../api/blockList'

const MATCH_TYPE_OPTIONS = ['exact', 'prefix']

export default function AdminBlockListPage() {
  const canManage = useAuthStore((s) => s.hasPermission('blocklist.manage'))

  const [entries, setEntries] = useState<BlockListEntry[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')

  const [showCreate, setShowCreate] = useState(false)
  const [form, setForm] = useState({
    phoneNumber: '',
    matchType: 'exact',
    reason: '',
    expiresAt: '',
  })
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  useEffect(() => {
    blockListApi.getAll()
      .then(setEntries)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  function setField<K extends keyof typeof form>(key: K, value: typeof form[K]) {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  function resetForm() {
    setForm({ phoneNumber: '', matchType: 'exact', reason: '', expiresAt: '' })
    setCreateError(null)
  }

  async function handleCreate() {
    if (!form.phoneNumber.trim()) return
    setCreating(true)
    setCreateError(null)
    try {
      const entry = await blockListApi.add({
        phoneNumber: form.phoneNumber.trim(),
        matchType: form.matchType,
        reason: form.reason.trim() || undefined,
        expiresAt: form.expiresAt ? new Date(form.expiresAt).toISOString() : undefined,
      })
      setEntries((prev) => [...prev, entry].sort((a, b) => a.phoneNumber.localeCompare(b.phoneNumber)))
      resetForm()
      setShowCreate(false)
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : 'Add failed.')
    } finally {
      setCreating(false)
    }
  }

  async function handleRemove(entry: BlockListEntry) {
    if (!window.confirm(`Remove ${entry.phoneNumber} from the block list?`)) return
    try {
      await blockListApi.remove(entry.id)
      setEntries((prev) => prev.filter((e) => e.id !== entry.id))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Remove failed.')
    }
  }

  const q = search.trim().toLowerCase()
  const filtered = q
    ? entries.filter((e) =>
        e.phoneNumber.toLowerCase().includes(q) || (e.reason ?? '').toLowerCase().includes(q))
    : entries

  return (
    <AdminShell>
      <div className="p-6 max-w-5xl">
        <div className="flex items-start justify-between mb-6">
          <div>
            <h1 className="text-white text-xl font-semibold">Block List</h1>
            <p className="text-gray-500 text-sm mt-0.5">
              Phone numbers blocked from reaching your call flows. Checked by the "Check Block List"
              telephony node against a caller's ANI or a flow variable.
            </p>
          </div>
          {canManage && (
            <button
              onClick={() => { setShowCreate((v) => !v); resetForm() }}
              className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors shrink-0 ml-4"
            >
              Block a number
            </button>
          )}
        </div>

        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search by phone number or reason…"
          className="bg-gray-900 border border-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 mb-4 w-full max-w-sm"
        />

        {showCreate && canManage && (
          <div className="bg-gray-900 border border-gray-800 rounded-xl p-5 mb-6">
            <p className="text-gray-300 text-sm font-medium mb-4">Block a phone number</p>
            <div className="grid grid-cols-2 gap-3 mb-4">
              <div className="flex flex-col gap-1">
                <label className="text-gray-500 text-xs">Phone number *</label>
                <input
                  autoFocus
                  value={form.phoneNumber}
                  onChange={(e) => setField('phoneNumber', e.target.value)}
                  placeholder="+15551234567"
                  className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-gray-500 text-xs">Match type</label>
                <select
                  value={form.matchType}
                  onChange={(e) => setField('matchType', e.target.value)}
                  className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                >
                  {MATCH_TYPE_OPTIONS.map((t) => <option key={t} value={t}>{t}</option>)}
                </select>
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-gray-500 text-xs">Reason (optional)</label>
                <input
                  value={form.reason}
                  onChange={(e) => setField('reason', e.target.value)}
                  placeholder="e.g. Repeated spam calls"
                  className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-gray-500 text-xs">Expires (optional — blank blocks indefinitely)</label>
                <input
                  type="date"
                  value={form.expiresAt}
                  onChange={(e) => setField('expiresAt', e.target.value)}
                  className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>
            </div>

            <div className="flex items-center gap-3">
              <button
                onClick={handleCreate}
                disabled={creating || !form.phoneNumber.trim()}
                className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
              >
                {creating ? 'Adding…' : 'Add to block list'}
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
        {!loading && !error && filtered.length === 0 && (
          <p className="text-gray-500 text-sm">
            {entries.length === 0 ? 'No numbers are blocked yet.' : 'No entries match your search.'}
          </p>
        )}

        {filtered.length > 0 && (
          <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 text-gray-400 text-left">
                  <th className="px-4 py-3 font-medium">Phone Number</th>
                  <th className="px-4 py-3 font-medium">Match Type</th>
                  <th className="px-4 py-3 font-medium">Reason</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="px-4 py-3 font-medium">Expires</th>
                  {canManage && <th className="px-4 py-3 font-medium"></th>}
                </tr>
              </thead>
              <tbody>
                {filtered.map((entry) => (
                  <tr key={entry.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30">
                    <td className="px-4 py-3 text-white font-medium font-mono text-xs">{entry.phoneNumber}</td>
                    <td className="px-4 py-3">
                      <span className="inline-flex px-2 py-0.5 rounded text-xs font-medium bg-gray-700/60 text-gray-300 uppercase">
                        {entry.matchType}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-gray-400 text-xs">{entry.reason ?? '—'}</td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${entry.isActive ? 'bg-emerald-900/50 text-emerald-400' : 'bg-gray-700/60 text-gray-400'}`}>
                        {entry.isActive ? 'Active' : 'Expired'}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-gray-400 text-xs">
                      {entry.expiresAt ? new Date(entry.expiresAt).toLocaleDateString() : 'Never'}
                    </td>
                    {canManage && (
                      <td className="px-4 py-3 text-right">
                        <button
                          onClick={() => handleRemove(entry)}
                          className="text-red-400 hover:text-red-300 text-xs font-medium"
                        >
                          Remove
                        </button>
                      </td>
                    )}
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
