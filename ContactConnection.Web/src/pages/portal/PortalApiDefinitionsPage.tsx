import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import PortalShell from '../../components/portal/PortalShell'
import AuthConfigForm, {
  type AuthFormState,
  BLANK_AUTH_STATE,
  serializeAuthConfig,
} from '../../components/apiDefinitions/AuthConfigForm'
import {
  listPortalApiDefinitions,
  createPortalApiDefinition,
  updatePortalApiDefinition,
  activatePortalApiDefinition,
  deactivatePortalApiDefinition,
  deletePortalApiDefinition,
  listPortalCredentials,
  setPortalCredential,
  testPortalAuth,
  type ApiDefinitionRecord,
} from '../../api/portal'

const API_CATEGORIES = [
  { value: 'address',     label: 'Address' },
  { value: 'order',       label: 'Order' },
  { value: 'fulfillment', label: 'Fulfillment' },
  { value: 'media',       label: 'Media' },
  { value: 'general',     label: 'General' },
]

const HTTP_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']

const API_CATEGORY_LABELS: Record<string, string> = Object.fromEntries(
  API_CATEGORIES.map((c) => [c.value, c.label])
)

const AUTH_TYPE_LABELS: Record<string, string> = {
  none: 'None',
  api_key: 'API Key',
  basic: 'Basic Auth',
  bearer: 'Bearer Token',
  oauth2: 'OAuth2',
  hmac: 'HMAC',
}

function authTypeBadge(authJson: string) {
  try {
    const cfg = JSON.parse(authJson) as { type: string }
    if (cfg.type === 'none') return null
    const colors: Record<string, string> = {
      api_key: 'bg-sky-900/40 text-sky-300',
      basic: 'bg-gray-700 text-gray-300',
      bearer: 'bg-indigo-900/40 text-indigo-300',
      oauth2: 'bg-emerald-900/40 text-emerald-300',
      hmac: 'bg-amber-900/40 text-amber-300',
    }
    return { label: AUTH_TYPE_LABELS[cfg.type] ?? cfg.type, color: colors[cfg.type] ?? 'bg-gray-700 text-gray-300' }
  } catch {
    return null
  }
}

function apiCategoryBadgeColor(category: string) {
  const colors: Record<string, string> = {
    address:     'bg-sky-900/50 text-sky-300',
    order:       'bg-emerald-900/50 text-emerald-300',
    fulfillment: 'bg-amber-900/50 text-amber-300',
    media:       'bg-violet-900/50 text-violet-300',
    general:     'bg-indigo-900/50 text-indigo-300',
  }
  return colors[category] ?? 'bg-gray-800 text-gray-300'
}

interface FormState {
  apiCategory: string
  name: string
  httpMethod: string
  baseUrl: string
  description: string
  provider: string
  timeoutSeconds: string
  auth: AuthFormState
}

const BLANK_FORM: FormState = {
  apiCategory: 'address',
  name: '',
  httpMethod: 'POST',
  baseUrl: '',
  description: '',
  provider: '',
  timeoutSeconds: '30',
  auth: { ...BLANK_AUTH_STATE },
}

export default function PortalApiDefinitionsPage() {
  const navigate = useNavigate()
  const [defs, setDefs] = useState<ApiDefinitionRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [showForm, setShowForm] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<FormState>(BLANK_FORM)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [togglingId, setTogglingId] = useState<string | null>(null)
  const [knownCreds, setKnownCreds] = useState<string[]>([])

  useEffect(() => {
    load()
    listPortalCredentials()
      .then((list) => setKnownCreds(list.map((c) => c.keyName)))
      .catch(() => {})
  }, [])

  async function load() {
    setLoading(true)
    setError(null)
    try {
      setDefs(await listPortalApiDefinitions())
    } catch (e: unknown) {
      setError((e as Error).message)
    } finally {
      setLoading(false)
    }
  }

  function openCreate() {
    setForm(BLANK_FORM)
    setEditingId(null)
    setFormError(null)
    setShowForm(true)
  }

  function closeForm() {
    setShowForm(false)
    setEditingId(null)
  }

  function patchAuth(patch: Partial<AuthFormState>) {
    setForm((f) => ({ ...f, auth: { ...f.auth, ...patch } }))
  }

  async function handleSave() {
    if (!form.name.trim() || !form.baseUrl.trim()) {
      setFormError('Name and base URL are required.')
      return
    }
    setSaving(true)
    setFormError(null)
    try {
      const timeout = parseInt(form.timeoutSeconds) || 30
      const authConfig = serializeAuthConfig(form.auth)
      if (editingId) {
        const updated = await updatePortalApiDefinition(editingId, {
          name: form.name.trim(),
          httpMethod: form.httpMethod,
          baseUrl: form.baseUrl.trim(),
          description: form.description.trim() || undefined,
          provider: form.provider.trim() || undefined,
          timeoutSeconds: timeout,
          authConfig,
        })
        setDefs((prev) => prev.map((d) => (d.id === editingId ? updated : d)))
      } else {
        const created = await createPortalApiDefinition({
          apiCategory: form.apiCategory,
          name: form.name.trim(),
          httpMethod: form.httpMethod,
          baseUrl: form.baseUrl.trim(),
          description: form.description.trim() || undefined,
          provider: form.provider.trim() || undefined,
          timeoutSeconds: timeout,
          authConfig,
        })
        setDefs((prev) => [...prev, created])
      }
      closeForm()
    } catch (e: unknown) {
      setFormError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  async function handleToggleActive(def: ApiDefinitionRecord) {
    setTogglingId(def.id)
    try {
      const updated = def.isActive
        ? await deactivatePortalApiDefinition(def.id)
        : await activatePortalApiDefinition(def.id)
      setDefs((prev) => prev.map((d) => (d.id === def.id ? updated : d)))
    } catch { /* ignore */ } finally {
      setTogglingId(null)
    }
  }

  async function handleDelete(id: string) {
    if (!confirm('Delete this API definition? This cannot be undone.')) return
    setDeletingId(id)
    try {
      await deletePortalApiDefinition(id)
      setDefs((prev) => prev.filter((d) => d.id !== id))
    } catch { /* ignore */ } finally {
      setDeletingId(null)
    }
  }

  return (
    <PortalShell>
      <div className="p-6">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h1 className="text-white text-xl font-semibold">Platform API Definitions</h1>
            <p className="text-gray-400 text-sm mt-0.5">
              Built-in fallback APIs used by all tenants unless overridden at the tenant level.
            </p>
          </div>
          <button
            onClick={openCreate}
            className="bg-indigo-600 hover:bg-indigo-500 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors"
          >
            + Add definition
          </button>
        </div>

        {loading && <p className="text-gray-400 text-sm">Loading…</p>}
        {error && <p className="text-red-400 text-sm">{error}</p>}
        {!loading && !error && defs.length === 0 && (
          <p className="text-gray-500 text-sm">No platform API definitions yet.</p>
        )}

        {defs.length > 0 && (
          <div className="bg-gray-900 rounded-xl overflow-hidden border border-gray-800">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 text-gray-400 text-left">
                  <th className="px-4 py-3 font-medium">Type</th>
                  <th className="px-4 py-3 font-medium">Name</th>
                  <th className="px-4 py-3 font-medium">Provider</th>
                  <th className="px-4 py-3 font-medium">Auth</th>
                  <th className="px-4 py-3 font-medium">Base URL</th>
                  <th className="px-4 py-3 font-medium">Timeout</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="px-4 py-3 font-medium"></th>
                </tr>
              </thead>
              <tbody>
                {defs.map((d) => {
                  const auth = authTypeBadge(d.authConfig)
                  return (
                    <tr key={d.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30 transition-colors">
                      <td className="px-4 py-3">
                        <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${apiCategoryBadgeColor(d.apiCategory)}`}>
                          {API_CATEGORY_LABELS[d.apiCategory] ?? d.apiCategory}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-white font-medium">{d.name}</td>
                      <td className="px-4 py-3 text-gray-300">{d.provider ?? <span className="text-gray-600">—</span>}</td>
                      <td className="px-4 py-3">
                        {auth
                          ? <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${auth.color}`}>{auth.label}</span>
                          : <span className="text-gray-600 text-xs">None</span>
                        }
                      </td>
                      <td className="px-4 py-3 text-gray-400 max-w-xs truncate" title={d.baseUrl}>{d.baseUrl}</td>
                      <td className="px-4 py-3 text-gray-400">{d.timeoutSeconds}s</td>
                      <td className="px-4 py-3">
                        <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${d.isActive ? 'bg-emerald-900/50 text-emerald-400' : 'bg-gray-800 text-gray-500'}`}>
                          {d.isActive ? 'Active' : 'Inactive'}
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end gap-3">
                          <button
                            onClick={() => handleToggleActive(d)}
                            disabled={togglingId === d.id}
                            className={`text-xs font-medium transition-colors disabled:opacity-50 ${d.isActive ? 'text-amber-400 hover:text-amber-300' : 'text-emerald-400 hover:text-emerald-300'}`}
                          >
                            {togglingId === d.id ? '…' : d.isActive ? 'Deactivate' : 'Activate'}
                          </button>
                          <button onClick={() => navigate(`/portal/api-definitions/${d.id}`)} className="text-indigo-400 hover:text-indigo-300 text-xs font-medium transition-colors">Edit</button>
                          <button
                            onClick={() => handleDelete(d.id)}
                            disabled={deletingId === d.id}
                            className="text-red-400 hover:text-red-300 text-xs font-medium transition-colors disabled:opacity-50"
                          >
                            {deletingId === d.id ? '…' : 'Delete'}
                          </button>
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* ── Create / Edit Modal ── */}
      {showForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
          <div className="bg-gray-900 border border-gray-700 rounded-2xl w-full max-w-2xl shadow-2xl flex flex-col max-h-[90vh]">
            {/* Header */}
            <div className="px-6 pt-6 pb-4 border-b border-gray-800 shrink-0">
              <h2 className="text-white text-lg font-semibold">
                {editingId ? 'Edit API Definition' : 'Add API Definition'}
              </h2>
            </div>

            {/* Scrollable body */}
            <div className="overflow-y-auto flex-1 px-6 py-4 space-y-4">
              {/* API Type (create only) */}
              {!editingId && (
                <div>
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">API Category</label>
                  <select
                    value={form.apiCategory}
                    onChange={(e) => setForm((f) => ({ ...f, apiCategory: e.target.value }))}
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
                  >
                    {API_CATEGORIES.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
                  </select>
                </div>
              )}

              {/* Name + Provider */}
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">Name *</label>
                  <input
                    type="text"
                    value={form.name}
                    onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
                    placeholder="USPS Address V3"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                </div>
                <div>
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">Provider</label>
                  <input
                    type="text"
                    value={form.provider}
                    onChange={(e) => setForm((f) => ({ ...f, provider: e.target.value }))}
                    placeholder="USPS, Google Places…"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                </div>
              </div>

              {/* Description */}
              <div>
                <label className="block text-gray-400 text-xs font-medium mb-1.5">Description</label>
                <input
                  type="text"
                  value={form.description}
                  onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
                  placeholder="Optional description"
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                />
              </div>

              {/* Method + Base URL + Timeout */}
              <div className="grid grid-cols-5 gap-3">
                <div>
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">Method</label>
                  <select
                    value={form.httpMethod}
                    onChange={(e) => setForm((f) => ({ ...f, httpMethod: e.target.value }))}
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
                  >
                    {HTTP_METHODS.map((m) => <option key={m} value={m}>{m}</option>)}
                  </select>
                </div>
                <div className="col-span-3">
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">Base URL *</label>
                  <input
                    type="text"
                    value={form.baseUrl}
                    onChange={(e) => setForm((f) => ({ ...f, baseUrl: e.target.value }))}
                    placeholder="https://api.example.com/v3"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                </div>
                <div>
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">Timeout (s)</label>
                  <input
                    type="number"
                    min="1"
                    max="300"
                    value={form.timeoutSeconds}
                    onChange={(e) => setForm((f) => ({ ...f, timeoutSeconds: e.target.value }))}
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
                  />
                </div>
              </div>

              {/* Auth Config */}
              <div>
                <p className="text-gray-400 text-xs font-medium uppercase tracking-wide mb-2">Authentication</p>
                <AuthConfigForm
                  state={form.auth}
                  onChange={patchAuth}
                  knownCredentials={knownCreds}
                  onAddCredential={async (keyName, value) => {
                    await setPortalCredential(keyName, value)
                    setKnownCreds((prev) => [...prev, keyName])
                  }}
                  onTestAuth={() => testPortalAuth(serializeAuthConfig(form.auth))}
                />
              </div>
            </div>

            {/* Footer */}
            <div className="px-6 py-4 border-t border-gray-800 shrink-0">
              {formError && <p className="text-red-400 text-sm mb-3">{formError}</p>}
              <div className="flex justify-end gap-3">
                <button
                  onClick={closeForm}
                  disabled={saving}
                  className="px-4 py-2 text-sm text-gray-400 hover:text-white transition-colors disabled:opacity-50"
                >
                  Cancel
                </button>
                <button
                  onClick={handleSave}
                  disabled={saving}
                  className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm font-medium px-5 py-2 rounded-lg transition-colors"
                >
                  {saving ? 'Saving…' : editingId ? 'Save changes' : 'Create'}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </PortalShell>
  )
}
