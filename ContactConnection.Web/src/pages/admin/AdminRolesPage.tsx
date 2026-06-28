import { useEffect, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import { rolesApi, PERMISSION_GROUPS, PERMISSION_LABELS, LANDING_PAGE_LABELS, type Role } from '../../api/roles'

const LANDING_PAGE_KEYS = Object.keys(LANDING_PAGE_LABELS)

interface EditorState {
  name: string
  permissions: string[]
  defaultLandingPage: string
}

function RoleEditorModal({
  role,
  onSave,
  onClose,
}: {
  role: Role | null  // null = new role
  onSave: (data: EditorState) => Promise<void>
  onClose: () => void
}) {
  const [name, setName] = useState(role?.name ?? '')
  const [permissions, setPermissions] = useState<string[]>(role?.permissions ?? [])
  const [landingPage, setLandingPage] = useState(role?.defaultLandingPage ?? 'agent_portal')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function togglePermission(perm: string) {
    setPermissions(prev =>
      prev.includes(perm) ? prev.filter(p => p !== perm) : [...prev, perm]
    )
  }

  function toggleGroup(perms: string[]) {
    const allOn = perms.every(p => permissions.includes(p))
    setPermissions(prev =>
      allOn ? prev.filter(p => !perms.includes(p)) : [...new Set([...prev, ...perms])]
    )
  }

  async function handleSave() {
    if (!name.trim()) { setError('Name is required.'); return }
    setSaving(true)
    setError(null)
    try {
      await onSave({ name: name.trim(), permissions, defaultLandingPage: landingPage })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
      <div className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-2xl max-h-[90vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-700">
          <h2 className="text-lg font-semibold text-white">{role ? (role.isBuiltIn ? role.name : 'Edit Role') : 'New Role'}</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-white text-xl leading-none">&times;</button>
        </div>

        {/* Body */}
        <div className="overflow-y-auto flex-1 px-6 py-4 space-y-6">
          {/* Name */}
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Role Name</label>
            <input
              value={name}
              onChange={e => setName(e.target.value)}
              disabled={role?.isBuiltIn}
              className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm disabled:opacity-50"
              placeholder="e.g. Floor Manager"
            />
          </div>

          {/* Landing page */}
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Landing Page After Login</label>
            <select
              value={landingPage}
              onChange={e => setLandingPage(e.target.value)}
              className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
            >
              {LANDING_PAGE_KEYS.map(key => (
                <option key={key} value={key}>{LANDING_PAGE_LABELS[key]}</option>
              ))}
            </select>
          </div>

          {/* Permissions */}
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-3">Permissions</label>
            <div className="space-y-4">
              {Object.entries(PERMISSION_GROUPS).map(([group, perms]) => {
                const allOn = perms.every(p => permissions.includes(p))
                const someOn = perms.some(p => permissions.includes(p))
                return (
                  <div key={group} className="bg-gray-800/60 rounded-lg p-4">
                    <div className="flex items-center justify-between mb-3">
                      <span className="text-sm font-semibold text-gray-200">{group}</span>
                      <button
                        onClick={() => toggleGroup(perms)}
                        className={`text-xs px-2 py-0.5 rounded ${allOn ? 'bg-indigo-600 text-white' : someOn ? 'bg-gray-600 text-gray-300' : 'bg-gray-700 text-gray-400'} hover:opacity-80`}
                      >
                        {allOn ? 'All on' : someOn ? 'Some on' : 'All off'}
                      </button>
                    </div>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                      {perms.map(perm => (
                        <label key={perm} className="flex items-center gap-3 cursor-pointer group">
                          <button
                            type="button"
                            onClick={() => togglePermission(perm)}
                            className={`inline-flex h-5 w-10 flex-shrink-0 cursor-pointer rounded-full p-0.5 transition-colors duration-200 ${permissions.includes(perm) ? 'bg-indigo-600' : 'bg-gray-600'}`}
                          >
                            <span className={`block h-4 w-4 rounded-full bg-white shadow-sm transition-transform duration-200 ${permissions.includes(perm) ? 'translate-x-5' : 'translate-x-0'}`} />
                          </button>
                          <span className="text-sm text-gray-300 group-hover:text-white">{PERMISSION_LABELS[perm] ?? perm}</span>
                        </label>
                      ))}
                    </div>
                  </div>
                )
              })}
            </div>
          </div>

          {error && <p className="text-red-400 text-sm">{error}</p>}
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-700">
          <button onClick={onClose} className="px-4 py-2 text-sm text-gray-300 hover:text-white">Cancel</button>
          <button
            onClick={handleSave}
            disabled={saving}
            className="px-5 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg disabled:opacity-50"
          >
            {saving ? 'Saving…' : 'Save Role'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function AdminRolesPage() {
  const [roles, setRoles] = useState<Role[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<Role | null | 'new'>(null)
  const [deleting, setDeleting] = useState<string | null>(null)

  async function load() {
    try {
      const data = await rolesApi.getAll()
      setRoles(data)
    } catch {
      setError('Failed to load roles.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  async function handleSave(data: EditorState) {
    if (editing === 'new') {
      await rolesApi.create(data)
    } else if (editing) {
      await rolesApi.update(editing.id, data)
    }
    await load()
  }

  async function handleDelete(id: string) {
    setDeleting(id)
    try {
      await rolesApi.delete(id)
      setRoles(prev => prev.filter(r => r.id !== id))
    } catch {
      setError('Failed to delete role.')
    } finally {
      setDeleting(null)
    }
  }

  return (
    <AdminShell>
      <div className="max-w-4xl mx-auto">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h1 className="text-2xl font-bold text-white">Roles</h1>
            <p className="text-sm text-gray-400 mt-1">Define roles and the permissions each one grants.</p>
          </div>
          <button
            onClick={() => setEditing('new')}
            className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg"
          >
            + New Role
          </button>
        </div>

        {error && <p className="text-red-400 text-sm mb-4">{error}</p>}

        {loading ? (
          <p className="text-gray-400">Loading…</p>
        ) : (
          <div className="space-y-3">
            {roles.map(role => (
              <div key={role.id} className="bg-gray-800 border border-gray-700 rounded-xl p-4 flex items-center justify-between gap-4">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-white font-medium">{role.name}</span>
                    {role.isBuiltIn && (
                      <span className="text-xs bg-gray-700 text-gray-400 px-2 py-0.5 rounded-full">Built-in</span>
                    )}
                  </div>
                  <div className="flex items-center gap-4 mt-1">
                    <span className="text-xs text-gray-400">
                      Lands on: <span className="text-gray-300">{LANDING_PAGE_LABELS[role.defaultLandingPage] ?? role.defaultLandingPage}</span>
                    </span>
                    <span className="text-xs text-gray-400">
                      {role.permissions.length} permission{role.permissions.length !== 1 ? 's' : ''}
                    </span>
                  </div>
                  <div className="flex flex-wrap gap-1 mt-2">
                    {role.permissions.slice(0, 8).map(p => (
                      <span key={p} className="text-xs bg-indigo-900/40 text-indigo-300 px-1.5 py-0.5 rounded">
                        {PERMISSION_LABELS[p] ?? p}
                      </span>
                    ))}
                    {role.permissions.length > 8 && (
                      <span className="text-xs text-gray-500">+{role.permissions.length - 8} more</span>
                    )}
                  </div>
                </div>
                <div className="flex items-center gap-2 flex-shrink-0">
                  <button
                    onClick={() => setEditing(role)}
                    className="px-3 py-1.5 text-sm bg-gray-700 hover:bg-gray-600 text-white rounded-lg"
                  >
                    {role.isBuiltIn ? 'View' : 'Edit'}
                  </button>
                  {!role.isBuiltIn && (
                    <button
                      onClick={() => handleDelete(role.id)}
                      disabled={deleting === role.id}
                      className="px-3 py-1.5 text-sm bg-red-900/50 hover:bg-red-800 text-red-300 rounded-lg disabled:opacity-50"
                    >
                      {deleting === role.id ? '…' : 'Delete'}
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {editing !== null && (
        <RoleEditorModal
          role={editing === 'new' ? null : editing}
          onSave={handleSave}
          onClose={() => setEditing(null)}
        />
      )}
    </AdminShell>
  )
}
