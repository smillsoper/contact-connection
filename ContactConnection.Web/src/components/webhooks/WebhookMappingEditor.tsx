import { useEffect, useMemo, useState } from 'react'
import JsonTree from '../shared/JsonTree'
import {
  createAdminWebhook,
  getAdminWebhook,
  updateAdminWebhook,
  regenerateAdminWebhookSecret,
  regenerateAdminWebhookToken,
  deleteAdminWebhook,
  listAdminWebhookEvents,
  type AdminWebhook,
  type WebhookEventRecord,
} from '../../api/adminApiDefinitions'
import { api } from '../../api/client'
import {
  CANONICAL_WEBHOOK_TYPES,
  ROOT_MATCH_FIELDS_BY_TYPE,
  ORDER_LINE_ITEM_MATCH_FIELDS,
  OPERATIONS_BY_TYPE,
  ORDER_ITEMS_ARRAY_OPERATIONS,
  NO_MATCH_POLICIES,
  MULTIPLE_MATCH_POLICIES,
  type OperationOption,
} from '../../constants/canonicalWebhookTypes'

interface CustomFieldDefinitionOption {
  id: string
  fieldName: string
  displayLabel: string
}

interface MatchRuleForm { sourcePath: string; matchField: string }
interface ItemsArrayForm { arrayPath: string; itemMatch: MatchRuleForm; onNoMatch: string; onMultipleMatches: string }
interface OperationForm { name: string; params: Record<string, string> }

interface MappingConfigShape {
  canonicalType: string
  rootMatch: MatchRuleForm
  itemsArray: ItemsArrayForm | null
  operation: OperationForm
  onNoMatch: string
}

function blankMapping(): MappingConfigShape {
  return {
    canonicalType: 'order_line',
    rootMatch: { sourcePath: '', matchField: 'Id' },
    itemsArray: null,
    operation: { name: 'Ship', params: {} },
    onNoMatch: 'skip_and_log',
  }
}

function parseMappingConfig(json: string): MappingConfigShape {
  try {
    const parsed = JSON.parse(json)
    if (!parsed?.canonicalType) return blankMapping()
    return {
      canonicalType: parsed.canonicalType,
      rootMatch: parsed.rootMatch ?? { sourcePath: '', matchField: '' },
      itemsArray: parsed.itemsArray ?? null,
      operation: parsed.operation ?? { name: '', params: {} },
      onNoMatch: parsed.onNoMatch ?? 'skip_and_log',
    }
  } catch {
    return blankMapping()
  }
}

const STATUS_COLOR: Record<string, string> = {
  received: 'bg-gray-800 text-gray-300',
  processed: 'bg-emerald-900/50 text-emerald-400',
  duplicate: 'bg-amber-900/50 text-amber-400',
  rejected: 'bg-red-900/50 text-red-400',
  failed: 'bg-red-900/50 text-red-400',
}

const ALGORITHMS = ['SHA256', 'SHA512', 'SHA1', 'MD5']

interface Props {
  webhookId: string | null // null = creating a new webhook
  onClose: () => void // dismiss the modal — the editor itself decides when to call this
  onSaved: () => void // a create/update/delete completed — refresh the list; does NOT dismiss
}

// Standalone webhook creation/edit flow: name a webhook, paste a sample payload to see it as a
// browsable tree, pick a canonical domain object to map onto, and build the match/operation rule
// visually — which payload field identifies the target record, which fields set which properties,
// and how to handle no-match/multiple-match ambiguity. Replaces the old per-API-endpoint
// "Webhook" button/panel entirely — a webhook here is its own standalone resource, not an
// operation attached to an outbound API connection. See API_HARDENING_CHECKLIST.md Tier 2.
export default function WebhookMappingEditor({ webhookId, onClose, onSaved }: Props) {
  // Local, not derived from the prop: after a successful create, the modal switches itself into
  // edit mode (currentId becomes the new webhook's id) instead of closing, so the reveal-once
  // secret stays on screen until the admin dismisses it themselves — closing immediately on
  // create was the bug (onSaved used to double as "close", so the secret never rendered).
  const [currentId, setCurrentId] = useState<string | null>(webhookId)
  const isEdit = currentId !== null

  const [loading, setLoading] = useState(isEdit)
  const [webhook, setWebhook] = useState<AdminWebhook | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [revealedSecret, setRevealedSecret] = useState<string | null>(null)

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [mapping, setMapping] = useState<MappingConfigShape>(blankMapping())

  const [signatureHeaderName, setSignatureHeaderName] = useState('X-Signature')
  const [signatureAlgorithm, setSignatureAlgorithm] = useState('SHA256')
  const [includeTimestamp, setIncludeTimestamp] = useState(true)
  const [timestampTolerance, setTimestampTolerance] = useState(300)
  const [isActive, setIsActive] = useState(true)

  const [samplePayloadText, setSamplePayloadText] = useState('')
  const [sampleBody, setSampleBody] = useState<unknown>(null)
  const [sampleError, setSampleError] = useState<string | null>(null)
  const [pickingField, setPickingField] = useState<string | null>(null)
  const [copiedPath, setCopiedPath] = useState<string | null>(null)

  const [customFieldDefs, setCustomFieldDefs] = useState<CustomFieldDefinitionOption[]>([])
  const [events, setEvents] = useState<WebhookEventRecord[] | null>(null)

  useEffect(() => {
    api.get<CustomFieldDefinitionOption[]>('/api/v1/custom-field-definitions').then(setCustomFieldDefs).catch(() => {})
  }, [])

  useEffect(() => {
    if (!isEdit || !webhookId) return
    setLoading(true)
    getAdminWebhook(webhookId)
      .then((w) => {
        setWebhook(w)
        setName(w.name)
        setDescription(w.description ?? '')
        setMapping(parseMappingConfig(w.mappingConfig))
        setSignatureHeaderName(w.signatureHeaderName)
        setSignatureAlgorithm(w.signatureAlgorithm)
        setIncludeTimestamp(w.includeTimestamp)
        setTimestampTolerance(w.timestampToleranceSeconds)
        setIsActive(w.isActive)
        return listAdminWebhookEvents(webhookId)
      })
      .then(setEvents)
      .catch((e: unknown) => setError((e as Error).message))
      .finally(() => setLoading(false))
  }, [isEdit, webhookId])

  const itemsArrayEnabled = mapping.itemsArray !== null

  const operationsForType: OperationOption[] = useMemo(() => {
    if (mapping.canonicalType === 'order' && itemsArrayEnabled) return ORDER_ITEMS_ARRAY_OPERATIONS
    return OPERATIONS_BY_TYPE[mapping.canonicalType] ?? []
  }, [mapping.canonicalType, itemsArrayEnabled])

  const activeOperation = operationsForType.find((o) => o.value === mapping.operation.name) ?? operationsForType[0]

  function parseSamplePayload() {
    try {
      setSampleBody(JSON.parse(samplePayloadText))
      setSampleError(null)
    } catch (e) {
      setSampleError((e as Error).message)
      setSampleBody(null)
    }
  }

  // "Pick from tree" — clicking a JsonTree leaf while a field is active writes the path there
  // instead of just copying to the clipboard (JsonTree's default behavior, still used as a
  // fallback when no field is active).
  function handleTreeCopy(path: string) {
    setCopiedPath(path)
    setTimeout(() => setCopiedPath(null), 1200)
    navigator.clipboard.writeText(path).catch(() => {})
    if (!pickingField) return

    if (pickingField === 'rootMatch.sourcePath') {
      setMapping((m) => ({ ...m, rootMatch: { ...m.rootMatch, sourcePath: path } }))
    } else if (pickingField === 'itemsArray.arrayPath') {
      setMapping((m) => ({ ...m, itemsArray: m.itemsArray ? { ...m.itemsArray, arrayPath: path } : m.itemsArray }))
    } else if (pickingField === 'itemsArray.itemMatch.sourcePath') {
      setMapping((m) => ({
        ...m,
        itemsArray: m.itemsArray ? { ...m.itemsArray, itemMatch: { ...m.itemsArray.itemMatch, sourcePath: path } } : m.itemsArray,
      }))
    } else if (pickingField.startsWith('operation.param.')) {
      const key = pickingField.slice('operation.param.'.length)
      setMapping((m) => ({ ...m, operation: { ...m.operation, params: { ...m.operation.params, [key]: path } } }))
    }
    setPickingField(null)
  }

  // The array element (if any) whose fields itemMatch.sourcePath and operation params resolve
  // against, when itemsArray is enabled — lets the user pick item-relative paths from a real
  // sample element instead of guessing.
  const sampleArrayItem = useMemo(() => {
    if (!mapping.itemsArray?.arrayPath || sampleBody == null) return null
    let current: unknown = sampleBody
    for (const segment of mapping.itemsArray.arrayPath.split('.')) {
      const arrMatch = segment.match(/^(\w*)\[(\d+)\]$/)
      if (arrMatch) {
        const [, key, idx] = arrMatch
        if (key) current = (current as Record<string, unknown>)?.[key]
        current = Array.isArray(current) ? current[parseInt(idx)] : undefined
      } else {
        current = (current as Record<string, unknown>)?.[segment]
      }
    }
    return Array.isArray(current) ? current[0] : null
  }, [mapping.itemsArray?.arrayPath, sampleBody])

  function toggleItemsArray(enabled: boolean) {
    setMapping((m) => ({
      ...m,
      itemsArray: enabled
        ? { arrayPath: '', itemMatch: { sourcePath: '', matchField: 'Sku' }, onNoMatch: 'skip_and_log', onMultipleMatches: 'skip_and_log' }
        : null,
      operation: { name: '', params: {} }, // operation catalog changes when this toggles — clear the pick
    }))
  }

  async function handleSave() {
    if (!name.trim()) { setError('Name is required.'); return }
    if (!mapping.operation.name) { setError('Select an operation.'); return }

    setSaving(true)
    setError(null)
    try {
      const mappingConfigJson = JSON.stringify(mapping)
      if (isEdit && currentId) {
        await updateAdminWebhook(currentId, {
          name, description: description || undefined, mappingConfig: mappingConfigJson,
          signatureHeaderName, signatureAlgorithm, includeTimestamp, timestampToleranceSeconds: timestampTolerance, isActive,
        })
        // Editing an existing webhook never reveals a new secret, so it's safe to close.
        onSaved()
        onClose()
      } else {
        const created = await createAdminWebhook({
          name, canonicalType: mapping.canonicalType, description: description || undefined, mappingConfig: mappingConfigJson,
          signatureHeaderName, signatureAlgorithm, includeTimestamp, timestampToleranceSeconds: timestampTolerance,
        })
        if (created.secret) setRevealedSecret(created.secret)
        setWebhook(created)
        setCurrentId(created.id) // switch into edit mode in place — do NOT close, the secret above is reveal-once
        setEvents([]) // a brand new webhook has no events yet; skip the network round-trip
        onSaved() // refresh the list in the background without dismissing this modal
      }
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  async function handleRegenerateSecret() {
    if (!currentId) return
    setSaving(true)
    try {
      const updated = await regenerateAdminWebhookSecret(currentId)
      if (updated.secret) setRevealedSecret(updated.secret)
    } catch (e) { setError((e as Error).message) } finally { setSaving(false) }
  }

  async function handleRegenerateToken() {
    if (!currentId) return
    setSaving(true)
    try {
      const updated = await regenerateAdminWebhookToken(currentId)
      setWebhook(updated)
    } catch (e) { setError((e as Error).message) } finally { setSaving(false) }
  }

  async function handleDelete() {
    if (!currentId || !confirm(`Delete webhook "${name}"? This cannot be undone.`)) return
    setSaving(true)
    try {
      await deleteAdminWebhook(currentId)
      onSaved()
      onClose()
    } catch (e) { setError((e as Error).message) } finally { setSaving(false) }
  }

  // Platform domain is whatever this admin UI is served from, minus its own subdomain
  // (admin.contactconnection.io -> contactconnection.io). Falls back to the raw host on localhost.
  const platformDomain = window.location.hostname.split('.').slice(1).join('.') || window.location.hostname
  const fullUrl = webhook ? `https://${webhook.tenantSubdomain}.${platformDomain}${webhook.path}` : ''

  function PickButton({ field }: { field: string }) {
    return (
      <button
        type="button"
        onClick={() => setPickingField(pickingField === field ? null : field)}
        disabled={!sampleBody}
        className={`px-2 py-1 text-[11px] rounded border transition-colors disabled:opacity-40 ${
          pickingField === field ? 'bg-indigo-600 border-indigo-500 text-white' : 'bg-gray-800 border-gray-700 text-gray-400 hover:text-white'
        }`}
      >
        {pickingField === field ? 'Click a field…' : 'Pick from payload'}
      </button>
    )
  }

  if (loading) {
    return (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
        <p className="text-gray-400 text-sm">Loading…</p>
      </div>
    )
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4" onClick={onClose}>
      <div
        className="bg-gray-900 border border-gray-700 rounded-2xl w-full max-w-6xl shadow-2xl flex flex-col max-h-[90vh]"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="px-5 py-4 border-b border-gray-800 flex items-center justify-between shrink-0">
          <h2 className="text-white text-sm font-semibold">{isEdit ? 'Edit Webhook' : 'New Webhook'}</h2>
          <button onClick={onClose} className="text-gray-500 hover:text-white text-lg leading-none px-1">×</button>
        </div>

        <div className="flex-1 overflow-y-auto px-5 py-4">
          {error && <p className="text-red-400 text-xs mb-3">{error}</p>}

          {revealedSecret && (
            <div className="bg-amber-950/40 border border-amber-800/60 rounded-lg px-3 py-2.5 mb-4">
              <p className="text-amber-400 text-xs font-medium mb-1">Secret — copy this now, it won't be shown again</p>
              <p className="text-gray-200 text-xs font-mono break-all">{revealedSecret}</p>
            </div>
          )}

          {webhook && (
            <div className="mb-4">
              <label className="text-gray-500 text-xs">Webhook URL</label>
              <div className="flex items-center gap-2 mt-1">
                <input readOnly value={fullUrl} className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-300 font-mono" />
                <button onClick={() => navigator.clipboard.writeText(fullUrl)} className="px-2.5 py-1.5 text-xs rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 shrink-0">
                  Copy
                </button>
              </div>
            </div>
          )}

          <div className="grid grid-cols-2 gap-4 mb-4">
            <div>
              <label className="block text-gray-400 text-xs font-medium mb-1">Name</label>
              <input value={name} onChange={(e) => setName(e.target.value)}
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500" />
            </div>
            <div>
              <label className="block text-gray-400 text-xs font-medium mb-1">Description (optional)</label>
              <input value={description} onChange={(e) => setDescription(e.target.value)}
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500" />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            {/* ── Left: sample payload + tree ── */}
            <div>
              <label className="block text-gray-400 text-xs font-medium mb-1">Sample Payload</label>
              <textarea
                value={samplePayloadText}
                onChange={(e) => setSamplePayloadText(e.target.value)}
                placeholder={'{\n  "orderLineId": "...",\n  "trackingNumber": "1Z999"\n}'}
                rows={6}
                spellCheck={false}
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-xs font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500 resize-none"
              />
              <button onClick={parseSamplePayload} className="mt-2 px-3 py-1.5 text-xs rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300">
                Parse Payload
              </button>
              {sampleError && <p className="text-red-400 text-xs mt-1">{sampleError}</p>}
              <p className="text-gray-600 text-[11px] mt-2">
                No network call — inbound webhooks have no vendor URL of ours to test against. Paste an
                example of what the vendor will actually send.
              </p>

              {sampleBody != null && (
                <div className="mt-3 border border-gray-800 rounded-lg bg-gray-950/50 p-2 max-h-64 overflow-auto">
                  <JsonTree name="" value={sampleBody} path="" depth={0} copiedPath={copiedPath} onCopy={handleTreeCopy} />
                </div>
              )}

              {pickingField && (
                <p className="text-indigo-400 text-[11px] mt-2">Click a field in the payload tree above to fill "{pickingField}".</p>
              )}
            </div>

            {/* ── Right: mapping config ── */}
            <div className="space-y-3">
              <div>
                <label className="block text-gray-400 text-xs font-medium mb-1">Canonical Object</label>
                <select
                  value={mapping.canonicalType}
                  disabled={isEdit}
                  onChange={(e) => setMapping({ ...blankMapping(), canonicalType: e.target.value })}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500 disabled:opacity-60"
                >
                  {CANONICAL_WEBHOOK_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
                </select>
                {isEdit && <p className="text-gray-600 text-[11px] mt-1">Canonical type can't change after creation — delete and recreate instead.</p>}
              </div>

              <div className="border border-gray-700 rounded-lg p-3 bg-gray-800/30 space-y-2">
                <p className="text-gray-500 text-xs font-medium uppercase tracking-wide">
                  {mapping.canonicalType === 'order' && itemsArrayEnabled ? 'Find the Order' : 'Find the Record'}
                </p>
                <div className="grid grid-cols-2 gap-2">
                  <div>
                    <label className="text-gray-500 text-xs">Match Field</label>
                    <select
                      value={mapping.rootMatch.matchField}
                      onChange={(e) => setMapping((m) => ({ ...m, rootMatch: { ...m.rootMatch, matchField: e.target.value } }))}
                      className="w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200"
                    >
                      <option value="">Select…</option>
                      {(ROOT_MATCH_FIELDS_BY_TYPE[mapping.canonicalType] ?? []).map((f) => (
                        <option key={f.value} value={f.value}>{f.label}</option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="text-gray-500 text-xs">Payload Field</label>
                    <div className="flex items-center gap-1.5 mt-1">
                      <input
                        value={mapping.rootMatch.sourcePath}
                        onChange={(e) => setMapping((m) => ({ ...m, rootMatch: { ...m.rootMatch, sourcePath: e.target.value } }))}
                        placeholder="orderLineId"
                        className="flex-1 min-w-0 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200 font-mono"
                      />
                      <PickButton field="rootMatch.sourcePath" />
                    </div>
                  </div>
                </div>
              </div>

              {mapping.canonicalType === 'order' && (
                <label className="flex items-center gap-2 text-xs text-gray-300 cursor-pointer">
                  <input type="checkbox" checked={itemsArrayEnabled} onChange={(e) => toggleItemsArray(e.target.checked)} />
                  This payload updates multiple line items
                </label>
              )}

              {itemsArrayEnabled && mapping.itemsArray && (
                <div className="border border-gray-700 rounded-lg p-3 bg-gray-800/30 space-y-2">
                  <p className="text-gray-500 text-xs font-medium uppercase tracking-wide">Line Items</p>
                  <div>
                    <label className="text-gray-500 text-xs">Items Array Path</label>
                    <div className="flex items-center gap-1.5 mt-1">
                      <input
                        value={mapping.itemsArray.arrayPath}
                        onChange={(e) => setMapping((m) => ({ ...m, itemsArray: m.itemsArray && { ...m.itemsArray, arrayPath: e.target.value } }))}
                        placeholder="items"
                        className="flex-1 min-w-0 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200 font-mono"
                      />
                      <PickButton field="itemsArray.arrayPath" />
                    </div>
                  </div>

                  <div className="grid grid-cols-2 gap-2">
                    <div>
                      <label className="text-gray-500 text-xs">Match Line By</label>
                      <select
                        value={mapping.itemsArray.itemMatch.matchField}
                        onChange={(e) => setMapping((m) => ({
                          ...m, itemsArray: m.itemsArray && { ...m.itemsArray, itemMatch: { ...m.itemsArray.itemMatch, matchField: e.target.value } },
                        }))}
                        className="w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200"
                      >
                        {ORDER_LINE_ITEM_MATCH_FIELDS.map((f) => <option key={f.value} value={f.value}>{f.label}</option>)}
                      </select>
                    </div>
                    <div>
                      <label className="text-gray-500 text-xs">Item Field (within each array element)</label>
                      <div className="flex items-center gap-1.5 mt-1">
                        <input
                          value={mapping.itemsArray.itemMatch.sourcePath}
                          onChange={(e) => setMapping((m) => ({
                            ...m, itemsArray: m.itemsArray && { ...m.itemsArray, itemMatch: { ...m.itemsArray.itemMatch, sourcePath: e.target.value } },
                          }))}
                          placeholder="sku"
                          className="flex-1 min-w-0 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200 font-mono"
                        />
                        <PickButton field="itemsArray.itemMatch.sourcePath" />
                      </div>
                    </div>
                  </div>

                  {sampleArrayItem != null && (
                    <div className="border border-gray-800 rounded-lg bg-gray-950/50 p-2 max-h-40 overflow-auto">
                      <p className="text-gray-600 text-[10px] uppercase tracking-wide mb-1">First array item (fields below resolve relative to each item)</p>
                      <JsonTree name="" value={sampleArrayItem} path="" depth={0} copiedPath={copiedPath} onCopy={handleTreeCopy} />
                    </div>
                  )}

                  <div className="grid grid-cols-2 gap-2">
                    <div>
                      <label className="text-gray-500 text-xs">If no line matches</label>
                      <select
                        value={mapping.itemsArray.onNoMatch}
                        onChange={(e) => setMapping((m) => ({ ...m, itemsArray: m.itemsArray && { ...m.itemsArray, onNoMatch: e.target.value } }))}
                        className="w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200"
                      >
                        {NO_MATCH_POLICIES.map((p) => <option key={p.value} value={p.value}>{p.label}</option>)}
                      </select>
                    </div>
                    <div>
                      <label className="text-gray-500 text-xs">If multiple lines match</label>
                      <select
                        value={mapping.itemsArray.onMultipleMatches}
                        onChange={(e) => setMapping((m) => ({ ...m, itemsArray: m.itemsArray && { ...m.itemsArray, onMultipleMatches: e.target.value } }))}
                        className="w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200"
                      >
                        {MULTIPLE_MATCH_POLICIES.map((p) => <option key={p.value} value={p.value}>{p.label}</option>)}
                      </select>
                    </div>
                  </div>
                </div>
              )}

              <div className="border border-gray-700 rounded-lg p-3 bg-gray-800/30 space-y-2">
                <p className="text-gray-500 text-xs font-medium uppercase tracking-wide">Operation</p>
                <select
                  value={mapping.operation.name}
                  onChange={(e) => setMapping((m) => ({ ...m, operation: { name: e.target.value, params: {} } }))}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm"
                >
                  <option value="">Select an operation…</option>
                  {operationsForType.map((op) => <option key={op.value} value={op.value}>{op.label}</option>)}
                </select>

                {activeOperation?.params.map((param) => (
                  <div key={param.key}>
                    <label className="text-gray-500 text-xs">{param.label}</label>
                    {param.kind === 'customFieldDefinition' ? (
                      <select
                        value={mapping.operation.params[param.key] ?? ''}
                        onChange={(e) => setMapping((m) => ({ ...m, operation: { ...m.operation, params: { ...m.operation.params, [param.key]: e.target.value } } }))}
                        className="w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200"
                      >
                        <option value="">Select…</option>
                        {customFieldDefs.map((d) => <option key={d.id} value={d.id}>{d.displayLabel} ({d.fieldName})</option>)}
                      </select>
                    ) : (
                      <div className="flex items-center gap-1.5 mt-1">
                        <input
                          value={mapping.operation.params[param.key] ?? ''}
                          onChange={(e) => setMapping((m) => ({ ...m, operation: { ...m.operation, params: { ...m.operation.params, [param.key]: e.target.value } } }))}
                          placeholder={itemsArrayEnabled ? 'field within each item' : 'field in payload'}
                          className="flex-1 min-w-0 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200 font-mono"
                        />
                        <PickButton field={`operation.param.${param.key}`} />
                      </div>
                    )}
                  </div>
                ))}
              </div>

              <div>
                <label className="text-gray-500 text-xs">If the record isn't found</label>
                <select
                  value={mapping.onNoMatch}
                  onChange={(e) => setMapping((m) => ({ ...m, onNoMatch: e.target.value }))}
                  className="w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200"
                >
                  {NO_MATCH_POLICIES.map((p) => <option key={p.value} value={p.value}>{p.label}</option>)}
                </select>
              </div>
            </div>
          </div>

          <div className="border-t border-gray-800 mt-5 pt-4 grid grid-cols-3 gap-3">
            <div>
              <label className="text-gray-500 text-xs">Signature Header</label>
              <input value={signatureHeaderName} onChange={(e) => setSignatureHeaderName(e.target.value)}
                className="w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200" />
            </div>
            <div>
              <label className="text-gray-500 text-xs">Algorithm</label>
              <select value={signatureAlgorithm} onChange={(e) => setSignatureAlgorithm(e.target.value)}
                className="w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200">
                {ALGORITHMS.map((a) => <option key={a} value={a}>{a}</option>)}
              </select>
            </div>
            <div>
              <label className="text-gray-500 text-xs">Timestamp Tolerance (s)</label>
              <input type="number" value={timestampTolerance} onChange={(e) => setTimestampTolerance(Number(e.target.value))} disabled={!includeTimestamp}
                className="w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200 disabled:opacity-50" />
            </div>
          </div>
          <div className="flex items-center gap-4 mt-2">
            <label className="flex items-center gap-2 text-xs text-gray-300 cursor-pointer">
              <input type="checkbox" checked={includeTimestamp} onChange={(e) => setIncludeTimestamp(e.target.checked)} />
              Timestamped signature (replay protection)
            </label>
            {isEdit && (
              <label className="flex items-center gap-2 text-xs text-gray-300 cursor-pointer">
                <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
                Active
              </label>
            )}
          </div>

          {isEdit && events !== null && (
            <div className="pt-4 mt-4 border-t border-gray-800">
              <h3 className="text-gray-400 text-xs font-medium mb-2">Recent Events</h3>
              {events.length === 0 && <p className="text-gray-500 text-xs">No events received yet.</p>}
              <div className="space-y-1.5">
                {events.map((e) => (
                  <div key={e.id} className="bg-gray-800/40 border border-gray-700/50 rounded-lg px-3 py-2 flex items-center justify-between gap-2">
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <span className={`text-[11px] px-1.5 py-0.5 rounded font-medium shrink-0 ${STATUS_COLOR[e.processingStatus] ?? 'bg-gray-800 text-gray-300'}`}>
                          {e.processingStatus}
                        </span>
                        {e.outcomeKey && <span className="text-gray-400 text-[11px] truncate">{e.outcomeKey}</span>}
                        {!e.signatureValid && <span className="text-red-400 text-[11px]">bad signature</span>}
                      </div>
                      {e.processingError && <p className="text-red-400/80 text-[11px] mt-0.5 truncate">{e.processingError}</p>}
                    </div>
                    <span className="text-gray-600 text-[11px] shrink-0">{new Date(e.receivedAt).toLocaleString()}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>

        <div className="px-5 py-4 border-t border-gray-800 flex items-center justify-between shrink-0">
          <div className="flex gap-2">
            {isEdit && (
              <>
                <button disabled={saving} onClick={handleRegenerateSecret} className="px-2.5 py-1.5 text-xs rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 disabled:opacity-50">
                  Regenerate Secret
                </button>
                <button disabled={saving} onClick={handleRegenerateToken} className="px-2.5 py-1.5 text-xs rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 disabled:opacity-50">
                  Regenerate URL
                </button>
                <button disabled={saving} onClick={handleDelete} className="px-2.5 py-1.5 text-xs rounded-lg bg-red-900/50 hover:bg-red-900 text-red-300 disabled:opacity-50">
                  Delete
                </button>
              </>
            )}
          </div>
          <div className="flex gap-3">
            <button onClick={onClose} className="px-4 py-2 text-sm text-gray-400 hover:text-white transition-colors">Cancel</button>
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
    </div>
  )
}
