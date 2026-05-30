import { useEffect, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import {
  listAdminCredentials,
  setAdminCredential,
  deleteAdminCredential,
  type CredentialSummary,
} from '../../api/adminCredentials'

type ModalMode = 'add' | 'edit' | null

export default function AdminCredentialsPage() {
  const [items, setItems] = useState<CredentialSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [modalMode, setModalMode] = useState<ModalMode>(null)
  const [editKey, setEditKey] = useState('')
  const [formKey, setFormKey] = useState('')
  const [formValue, setFormValue] = useState('')
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const [confirmDelete, setConfirmDelete] = useState<string | null>(null)
  const [deleting, setDeleting] = useState(false)

  async function load() {
    setLoading(true)
    setError(null)
    try {
      setItems(await listAdminCredentials())
    } catch (e) {
      setError(String(e))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  function openAdd() {
    setFormKey('')
    setFormValue('')
    setFormError(null)
    setModalMode('add')
  }

  function openEdit(keyName: string) {
    setEditKey(keyName)
    setFormValue('')
    setFormError(null)
    setModalMode('edit')
  }

  async function handleSave() {
    const key = modalMode === 'edit' ? editKey : formKey.trim()
    if (!key) { setFormError('Key name is required.'); return }
    if (!formValue) { setFormError('Value is required.'); return }

    setSaving(true)
    setFormError(null)
    try {
      await setAdminCredential(key, formValue)
      setModalMode(null)
      await load()
    } catch (e) {
      setFormError(String(e))
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!confirmDelete) return
    setDeleting(true)
    try {
      await deleteAdminCredential(confirmDelete)
      setConfirmDelete(null)
      await load()
    } catch (e) {
      setError(String(e))
    } finally {
      setDeleting(false)
    }
  }

  return (
    <AdminShell>
      <div className="p-6 max-w-4xl mx-auto">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h1 className="text-xl font-semibold text-white">Credentials</h1>
            <p className="text-sm text-gray-400 mt-1">
              Secrets stored in Azure Key Vault. Key names are visible; values are write-only.
            </p>
          </div>
          <button
            onClick={openAdd}
            className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded transition-colors"
          >
            Add Credential
          </button>
        </div>

        {error && (
          <div className="mb-4 p-3 bg-red-900/40 border border-red-700 text-red-300 rounded text-sm">
            {error}
          </div>
        )}

        {loading ? (
          <div className="text-gray-400 text-sm py-8 text-center">Loading…</div>
        ) : items.length === 0 ? (
          <div className="text-gray-500 text-sm py-12 text-center border border-dashed border-gray-700 rounded-lg">
            No credentials stored. Add one to get started.
          </div>
        ) : (
          <div className="border border-gray-800 rounded-lg overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-gray-900 text-gray-400 text-left">
                  <th className="px-4 py-3 font-medium">Key Name</th>
                  <th className="px-4 py-3 font-medium">Value</th>
                  <th className="px-4 py-3 font-medium">Last Updated</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {items.map((item, i) => (
                  <tr
                    key={item.keyName}
                    className={`border-t border-gray-800 ${i % 2 === 0 ? 'bg-gray-950' : 'bg-gray-900/30'}`}
                  >
                    <td className="px-4 py-3 text-white font-mono">{item.keyName}</td>
                    <td className="px-4 py-3 text-gray-500 font-mono tracking-widest">••••••••</td>
                    <td className="px-4 py-3 text-gray-400">
                      {item.updatedOn ? new Date(item.updatedOn).toLocaleString() : '—'}
                    </td>
                    <td className="px-4 py-3 text-right">
                      <button
                        onClick={() => openEdit(item.keyName)}
                        className="text-indigo-400 hover:text-indigo-300 text-xs mr-4 transition-colors"
                      >
                        Update
                      </button>
                      <button
                        onClick={() => setConfirmDelete(item.keyName)}
                        className="text-red-400 hover:text-red-300 text-xs transition-colors"
                      >
                        Delete
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Add / Edit modal */}
      {modalMode !== null && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50">
          <div className="bg-gray-900 border border-gray-700 rounded-lg w-full max-w-md p-6">
            <h2 className="text-lg font-semibold text-white mb-4">
              {modalMode === 'add' ? 'Add Credential' : 'Update Credential'}
            </h2>

            {formError && (
              <div className="mb-3 p-2 bg-red-900/40 border border-red-700 text-red-300 rounded text-sm">
                {formError}
              </div>
            )}

            {modalMode === 'add' ? (
              <div className="mb-4">
                <label className="block text-sm text-gray-300 mb-1">Key Name</label>
                <input
                  type="text"
                  value={formKey}
                  onChange={(e) => setFormKey(e.target.value)}
                  placeholder="e.g. usps_client_id"
                  className="w-full bg-gray-800 border border-gray-700 text-white rounded px-3 py-2 text-sm font-mono focus:outline-none focus:border-indigo-500"
                />
                <p className="text-xs text-gray-500 mt-1">
                  Use lowercase letters, numbers, and underscores. Hyphens are also accepted.
                </p>
              </div>
            ) : (
              <div className="mb-4">
                <label className="block text-sm text-gray-300 mb-1">Key Name</label>
                <div className="bg-gray-800/60 border border-gray-700 text-gray-400 rounded px-3 py-2 text-sm font-mono">
                  {editKey}
                </div>
              </div>
            )}

            <div className="mb-6">
              <label className="block text-sm text-gray-300 mb-1">Value</label>
              <input
                type="password"
                value={formValue}
                onChange={(e) => setFormValue(e.target.value)}
                placeholder={modalMode === 'edit' ? 'Enter new value to overwrite' : 'Secret value'}
                autoComplete="new-password"
                className="w-full bg-gray-800 border border-gray-700 text-white rounded px-3 py-2 text-sm font-mono focus:outline-none focus:border-indigo-500"
              />
              <p className="text-xs text-gray-500 mt-1">
                Values are stored in Azure Key Vault and never returned by the API.
              </p>
            </div>

            <div className="flex justify-end gap-3">
              <button
                onClick={() => setModalMode(null)}
                className="px-4 py-2 text-sm text-gray-400 hover:text-white transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSave}
                disabled={saving}
                className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm rounded transition-colors"
              >
                {saving ? 'Saving…' : 'Save'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete confirm */}
      {confirmDelete !== null && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50">
          <div className="bg-gray-900 border border-gray-700 rounded-lg w-full max-w-sm p-6">
            <h2 className="text-lg font-semibold text-white mb-2">Delete Credential</h2>
            <p className="text-sm text-gray-400 mb-6">
              Permanently delete <span className="font-mono text-white">{confirmDelete}</span> from
              Key Vault? This cannot be undone.
            </p>
            <div className="flex justify-end gap-3">
              <button
                onClick={() => setConfirmDelete(null)}
                className="px-4 py-2 text-sm text-gray-400 hover:text-white transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleDelete}
                disabled={deleting}
                className="px-4 py-2 bg-red-700 hover:bg-red-600 disabled:opacity-50 text-white text-sm rounded transition-colors"
              >
                {deleting ? 'Deleting…' : 'Delete'}
              </button>
            </div>
          </div>
        </div>
      )}
    </AdminShell>
  )
}
