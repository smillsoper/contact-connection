import { useEffect, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import {
  customFieldsApi,
  type CustomFieldDefinition,
  type DataType,
} from '../../api/customFields'
import { listClients, type Client } from '../../api/telephony'

// Field Name is a machine key, not free text — same normalization the backend applies
// (trim + lowercase). Client-side hint only; the backend is authoritative.
const FIELD_NAME_PATTERN = /^[a-z0-9_]+$/

function scopeLabel(def: CustomFieldDefinition, clients: Client[]): string {
  if (!def.clientId) return 'Tenant-wide'
  const client = clients.find((c) => c.id === def.clientId)
  if (!def.campaignId) return `Client: ${client?.name ?? '(unknown)'}`
  const campaign = client?.campaigns.find((c) => c.id === def.campaignId)
  return `Campaign: ${campaign?.name ?? '(unknown)'} (${client?.name ?? '(unknown)'})`
}

interface EditorState {
  fieldName: string
  displayLabel: string
  dataTypeName: string
  isRequired: boolean
  displayOrder: number
  clientId: string
  campaignId: string
  isActive: boolean
}

function DefinitionEditorModal({
  definition,
  dataTypes,
  clients,
  existing,
  onSave,
  onClose,
}: {
  definition: CustomFieldDefinition | null // null = new
  dataTypes: DataType[]
  clients: Client[]
  existing: CustomFieldDefinition[]
  onSave: (data: EditorState) => Promise<void>
  onClose: () => void
}) {
  const isEdit = definition !== null

  const [fieldName, setFieldName] = useState(definition?.fieldName ?? '')
  const [displayLabel, setDisplayLabel] = useState(definition?.displayLabel ?? '')
  const [dataTypeName, setDataTypeName] = useState(definition?.dataTypeName ?? dataTypes[0]?.typeName ?? 'string')
  const [isRequired, setIsRequired] = useState(definition?.isRequired ?? false)
  const [displayOrder, setDisplayOrder] = useState(definition?.displayOrder ?? 0)
  const [clientId, setClientId] = useState(definition?.clientId ?? '')
  const [campaignId, setCampaignId] = useState(definition?.campaignId ?? '')
  const [isActive, setIsActive] = useState(definition?.isActive ?? true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const selectedClient = clients.find((c) => c.id === clientId)

  function handleClientChange(next: string) {
    setClientId(next)
    setCampaignId('') // campaign choice is only meaningful under the client that owns it
  }

  async function handleSave() {
    setError(null)

    if (!isEdit) {
      const normalizedName = fieldName.trim().toLowerCase()
      if (!normalizedName) { setError('Field name is required.'); return }
      if (!FIELD_NAME_PATTERN.test(normalizedName)) {
        setError('Field name may only contain lowercase letters, numbers, and underscores.')
        return
      }
      if (campaignId && !clientId) { setError('A campaign-scoped field must also specify its client.'); return }

      // Pre-check against the loaded list — the backend unique index is the source of truth,
      // this just gives a fast, clear error instead of a raw 409 round-trip in the common case.
      const duplicate = existing.some((d) =>
        d.fieldName === normalizedName &&
        (d.clientId ?? null) === (clientId || null) &&
        (d.campaignId ?? null) === (campaignId || null))
      if (duplicate) {
        setError('A field with this name already exists at this scope.')
        return
      }
    }

    if (!displayLabel.trim()) { setError('Display label is required.'); return }

    setSaving(true)
    try {
      await onSave({
        fieldName: fieldName.trim().toLowerCase(),
        displayLabel: displayLabel.trim(),
        dataTypeName,
        isRequired,
        displayOrder,
        clientId,
        campaignId,
        isActive,
      })
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
          <h2 className="text-lg font-semibold text-white">{isEdit ? 'Edit Custom Field' : 'New Custom Field'}</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-white text-xl leading-none">&times;</button>
        </div>

        <div className="overflow-y-auto flex-1 px-6 py-4 space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Field Name</label>
            <input
              value={fieldName}
              onChange={(e) => setFieldName(e.target.value)}
              disabled={isEdit}
              className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm font-mono disabled:opacity-50"
              placeholder="e.g. original_ani"
            />
            <p className="text-xs text-gray-500 mt-1">Lowercase letters, numbers, underscores only — can't be changed after creation.</p>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Display Label</label>
            <input
              value={displayLabel}
              onChange={(e) => setDisplayLabel(e.target.value)}
              className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
              placeholder="e.g. Original ANI"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Data Type</label>
            <select
              value={dataTypeName}
              onChange={(e) => setDataTypeName(e.target.value)}
              disabled={isEdit}
              className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm disabled:opacity-50"
            >
              {dataTypes.map((dt) => (
                <option key={dt.typeName} value={dt.typeName}>{dt.typeName}</option>
              ))}
            </select>
            {isEdit && <p className="text-xs text-gray-500 mt-1">Data type can't be changed after creation.</p>}
          </div>

          {!isEdit && (
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-1">Scope</label>
              <div className="grid grid-cols-2 gap-2">
                <select
                  value={clientId}
                  onChange={(e) => handleClientChange(e.target.value)}
                  className="bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
                >
                  <option value="">Tenant-wide</option>
                  {clients.map((c) => (
                    <option key={c.id} value={c.id}>{c.name}</option>
                  ))}
                </select>
                <select
                  value={campaignId}
                  onChange={(e) => setCampaignId(e.target.value)}
                  disabled={!selectedClient}
                  className="bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm disabled:opacity-50"
                >
                  <option value="">All campaigns</option>
                  {selectedClient?.campaigns.map((c) => (
                    <option key={c.id} value={c.id}>{c.name}</option>
                  ))}
                </select>
              </div>
              <p className="text-xs text-gray-500 mt-1">A more specific scope wins when the same field name exists at multiple scopes.</p>
            </div>
          )}

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Display Order</label>
            <input
              type="number"
              value={displayOrder}
              onChange={(e) => setDisplayOrder(Number(e.target.value) || 0)}
              className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
            />
          </div>

          <label className="flex items-center gap-3 cursor-pointer">
            <button
              type="button"
              onClick={() => setIsRequired((v) => !v)}
              className={`inline-flex h-5 w-10 flex-shrink-0 cursor-pointer rounded-full p-0.5 transition-colors duration-200 ${isRequired ? 'bg-indigo-600' : 'bg-gray-600'}`}
            >
              <span className={`block h-4 w-4 rounded-full bg-white shadow-sm transition-transform duration-200 ${isRequired ? 'translate-x-5' : 'translate-x-0'}`} />
            </button>
            <span className="text-sm text-gray-300">Required</span>
          </label>

          {isEdit && (
            <label className="flex items-center gap-3 cursor-pointer">
              <button
                type="button"
                onClick={() => setIsActive((v) => !v)}
                className={`inline-flex h-5 w-10 flex-shrink-0 cursor-pointer rounded-full p-0.5 transition-colors duration-200 ${isActive ? 'bg-indigo-600' : 'bg-gray-600'}`}
              >
                <span className={`block h-4 w-4 rounded-full bg-white shadow-sm transition-transform duration-200 ${isActive ? 'translate-x-5' : 'translate-x-0'}`} />
              </button>
              <span className="text-sm text-gray-300">Active (visible in flow designer pickers)</span>
            </label>
          )}

          {error && <p className="text-red-400 text-sm">{error}</p>}
        </div>

        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-700">
          <button onClick={onClose} className="px-4 py-2 text-sm text-gray-300 hover:text-white">Cancel</button>
          <button
            onClick={handleSave}
            disabled={saving}
            className="px-5 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg disabled:opacity-50"
          >
            {saving ? 'Saving…' : 'Save Field'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function AdminCustomFieldDefinitionsPage() {
  const [definitions, setDefinitions] = useState<CustomFieldDefinition[]>([])
  const [dataTypes, setDataTypes] = useState<DataType[]>([])
  const [clients, setClients] = useState<Client[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<CustomFieldDefinition | null | 'new'>(null)

  async function load() {
    try {
      const [defs, types, clientList] = await Promise.all([
        customFieldsApi.listDefinitions(),
        customFieldsApi.listDataTypes(),
        listClients(),
      ])
      setDefinitions(defs)
      setDataTypes(types)
      setClients(clientList)
    } catch {
      setError('Failed to load custom fields.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  async function handleSave(data: EditorState) {
    if (editing === 'new') {
      await customFieldsApi.createDefinition({
        fieldName: data.fieldName,
        displayLabel: data.displayLabel,
        dataTypeName: data.dataTypeName,
        isRequired: data.isRequired,
        displayOrder: data.displayOrder,
        clientId: data.clientId || null,
        campaignId: data.campaignId || null,
      })
    } else if (editing) {
      await customFieldsApi.updateDefinition(editing.id, {
        displayLabel: data.displayLabel,
        displayOrder: data.displayOrder,
        isRequired: data.isRequired,
        isActive: data.isActive,
      })
    }
    await load()
  }

  const sorted = [...definitions].sort((a, b) => a.displayOrder - b.displayOrder || a.fieldName.localeCompare(b.fieldName))

  return (
    <AdminShell>
      <div className="max-w-4xl mx-auto">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h1 className="text-2xl font-bold text-white">Custom Fields</h1>
            <p className="text-sm text-gray-400 mt-1">
              Define fields flows can save call-record data into and read back out of — feeds
              reporting the same way as any other structured call data.
            </p>
          </div>
          <button
            onClick={() => setEditing('new')}
            className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg"
          >
            + New Field
          </button>
        </div>

        {error && <p className="text-red-400 text-sm mb-4">{error}</p>}

        {loading ? (
          <p className="text-gray-400">Loading…</p>
        ) : sorted.length === 0 ? (
          <p className="text-gray-500 italic">No custom fields defined yet.</p>
        ) : (
          <div className="space-y-3">
            {sorted.map((def) => (
              <div key={def.id} className="bg-gray-800 border border-gray-700 rounded-xl p-4 flex items-center justify-between gap-4">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-white font-medium">{def.displayLabel}</span>
                    <span className="text-xs text-gray-500 font-mono">{def.fieldName}</span>
                    {!def.isActive && (
                      <span className="text-xs bg-gray-700 text-gray-400 px-2 py-0.5 rounded-full">Inactive</span>
                    )}
                    {def.isRequired && (
                      <span className="text-xs bg-amber-900/40 text-amber-300 px-2 py-0.5 rounded-full">Required</span>
                    )}
                  </div>
                  <div className="flex items-center gap-4 mt-1">
                    <span className="text-xs text-gray-400">
                      Type: <span className="text-gray-300">{def.dataTypeName}</span>
                    </span>
                    <span className="text-xs text-gray-400">{scopeLabel(def, clients)}</span>
                  </div>
                </div>
                <button
                  onClick={() => setEditing(def)}
                  className="px-3 py-1.5 text-sm bg-gray-700 hover:bg-gray-600 text-white rounded-lg flex-shrink-0"
                >
                  Edit
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      {editing !== null && (
        <DefinitionEditorModal
          definition={editing === 'new' ? null : editing}
          dataTypes={dataTypes}
          clients={clients}
          existing={definitions}
          onSave={handleSave}
          onClose={() => setEditing(null)}
        />
      )}
    </AdminShell>
  )
}
