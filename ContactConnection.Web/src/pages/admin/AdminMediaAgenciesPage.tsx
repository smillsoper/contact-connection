import { useEffect, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import { mediaApi, type MediaAgency, type MediaAgencyField } from '../../api/media'

// Media Agencies (S171, Media Agency Phase A). Each agency lists the data points it tracks per phone
// number (e.g. Cannella: PRODUCTCODE, ACCESS CODE). Defining them once here keeps every assignment —
// and later the agency's export file — using the exact same names.

const inputCls = 'w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm'

function AgencyEditor({ agency, onSave, onClose }: {
  agency: MediaAgency | null
  onSave: (name: string, fields: MediaAgencyField[], isActive: boolean) => Promise<void>
  onClose: () => void
}) {
  const [name, setName] = useState(agency?.name ?? '')
  const [fields, setFields] = useState<MediaAgencyField[]>(agency?.fields ?? [])
  const [isActive, setIsActive] = useState(agency?.isActive ?? true)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  async function save() {
    if (!name.trim()) { setError('Name is required.'); return }
    setSaving(true); setError(null)
    try {
      await onSave(name.trim(), fields.filter((f) => f.name.trim()), isActive)
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
      <div className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-lg max-h-[90vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-700">
          <h2 className="text-lg font-semibold text-white">{agency ? 'Edit Agency' : 'New Agency'}</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-white text-xl leading-none">&times;</button>
        </div>
        <div className="overflow-y-auto flex-1 px-6 py-4 space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Name</label>
            <input value={name} onChange={(e) => setName(e.target.value)} className={inputCls} placeholder="e.g. Cannella" />
          </div>
          <div>
            <p className="text-sm font-medium text-gray-300 mb-1">Fields per phone number</p>
            <p className="text-xs text-gray-500 mb-2">
              The agency's own data points for each number, spelled exactly as their reports expect (e.g. PRODUCTCODE,
              ACCESS CODE). Scripts read them as {'{{call_record.media.fields.access_code}}'} — lowercase, spaces as "_".
            </p>
            {fields.map((f, i) => (
              <div key={i} className="flex items-center gap-2 mb-2">
                <input value={f.name} placeholder="Field name"
                  onChange={(e) => setFields(fields.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)))}
                  className="flex-1 bg-gray-800 border border-gray-600 rounded-lg px-3 py-1.5 text-white text-sm font-mono" />
                <label className="flex items-center gap-1 text-xs text-gray-300 whitespace-nowrap cursor-pointer">
                  <input type="checkbox" checked={f.required} className="accent-indigo-600"
                    onChange={(e) => setFields(fields.map((x, j) => (j === i ? { ...x, required: e.target.checked } : x)))} />
                  Required
                </label>
                <button type="button" onClick={() => setFields(fields.filter((_, j) => j !== i))}
                  className="text-gray-500 hover:text-red-400 text-xs px-1">Remove</button>
              </div>
            ))}
            <button type="button" onClick={() => setFields([...fields, { name: '', required: false }])}
              className="text-indigo-400 hover:text-indigo-300 text-xs">+ Field</button>
          </div>
          {agency && (
            <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
              <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} className="accent-indigo-600" />
              Active (inactive agencies can't be chosen for new assignments)
            </label>
          )}
          {error && <p className="text-red-400 text-sm">{error}</p>}
        </div>
        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-700">
          <button onClick={onClose} className="px-4 py-2 text-sm text-gray-300 hover:text-white">Cancel</button>
          <button onClick={save} disabled={saving} className="px-5 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg disabled:opacity-50">
            {saving ? 'Saving…' : 'Save Agency'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function AdminMediaAgenciesPage() {
  const [agencies, setAgencies] = useState<MediaAgency[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<MediaAgency | 'new' | null>(null)

  const load = () => mediaApi.agencies().then(setAgencies).catch(() => setError('Failed to load agencies.'))
  useEffect(() => { load() }, [])

  return (
    <AdminShell>
      <div className="max-w-4xl mx-auto">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h1 className="text-2xl font-bold text-white">Media Agencies</h1>
            <p className="text-sm text-gray-400 mt-1">
              The agencies your clients buy airtime through. Assign them to phone numbers under Telephony → Phone Numbers → Media.
            </p>
          </div>
          <button onClick={() => setEditing('new')} className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg">
            + New Agency
          </button>
        </div>
        {error && <p className="text-red-400 text-sm mb-4">{error}</p>}
        {!agencies ? <p className="text-gray-400">Loading…</p> : agencies.length === 0 ? (
          <p className="text-gray-500 italic">No agencies yet.</p>
        ) : (
          <div className="space-y-3">
            {agencies.map((a) => (
              <div key={a.id} className="bg-gray-800 border border-gray-700 rounded-xl p-4 flex items-center justify-between gap-4">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-white font-medium">{a.name}</span>
                    {!a.isActive && <span className="text-xs bg-gray-700 text-gray-400 px-2 py-0.5 rounded-full">Inactive</span>}
                  </div>
                  <p className="text-xs text-gray-400 mt-1 font-mono truncate">
                    {a.fields.length === 0 ? 'No fields' : a.fields.map((f) => f.name + (f.required ? '*' : '')).join(' · ')}
                  </p>
                </div>
                <button onClick={() => setEditing(a)} className="px-3 py-1.5 text-sm bg-gray-700 hover:bg-gray-600 text-white rounded-lg flex-shrink-0">
                  Edit
                </button>
              </div>
            ))}
          </div>
        )}
      </div>
      {editing !== null && (
        <AgencyEditor
          agency={editing === 'new' ? null : editing}
          onSave={async (name, fields, isActive) => {
            if (editing === 'new') await mediaApi.createAgency(name, fields)
            else await mediaApi.updateAgency(editing.id, name, fields, isActive)
            await load()
          }}
          onClose={() => setEditing(null)}
        />
      )}
    </AdminShell>
  )
}
