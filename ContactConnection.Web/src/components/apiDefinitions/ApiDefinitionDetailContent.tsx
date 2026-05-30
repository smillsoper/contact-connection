import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import AuthConfigForm, {
  type AuthFormState,
  authStateFromConfig,
  serializeAuthConfig,
} from './AuthConfigForm'
import type { AuthTestResult } from '../../api/adminApiDefinitions'

const HTTP_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']

const API_TYPES = [
  { value: 'address_validation', label: 'Address Validation' },
  { value: 'realtime_address_autocomplete', label: 'Realtime Address Autocomplete' },
  { value: 'zip_code_lookup', label: 'ZIP Code Lookup' },
  { value: 'fulfillment', label: 'Fulfillment' },
]

const API_TYPE_LABELS: Record<string, string> = Object.fromEntries(
  API_TYPES.map((t) => [t.value, t.label])
)

const AUTH_TYPE_LABELS: Record<string, string> = {
  none: 'None',
  api_key: 'API Key',
  basic: 'Basic Auth',
  bearer: 'Bearer Token',
  oauth2: 'OAuth2',
  hmac: 'HMAC',
}

const AUTH_BADGE_COLORS: Record<string, string> = {
  api_key: 'bg-sky-900/40 text-sky-300',
  basic: 'bg-gray-700 text-gray-300',
  bearer: 'bg-indigo-900/40 text-indigo-300',
  oauth2: 'bg-emerald-900/40 text-emerald-300',
  hmac: 'bg-amber-900/40 text-amber-300',
}

const API_TYPE_BADGE_COLORS: Record<string, string> = {
  address_validation: 'bg-sky-900/50 text-sky-300',
  realtime_address_autocomplete: 'bg-violet-900/50 text-violet-300',
  zip_code_lookup: 'bg-indigo-900/50 text-indigo-300',
  fulfillment: 'bg-amber-900/50 text-amber-300',
}

function authBadge(authJson: string) {
  try {
    const cfg = JSON.parse(authJson) as { type: string }
    if (cfg.type === 'none') return null
    return { label: AUTH_TYPE_LABELS[cfg.type] ?? cfg.type, color: AUTH_BADGE_COLORS[cfg.type] ?? 'bg-gray-700 text-gray-300' }
  } catch {
    return null
  }
}

export interface ApiDefinitionRecord {
  id: string
  apiType: string
  provider: string | null
  name: string
  description: string | null
  httpMethod: string
  baseUrl: string
  timeoutSeconds: number
  headers: string
  queryParams: string
  requestBodyTemplate: string | null
  responseMapping: string
  authConfig: string
  isActive: boolean
  createdAt: string
  updatedAt: string | null
}

export interface ApiEndpointRecord {
  id: string
  definitionId: string
  name: string
  description: string | null
  path: string
  httpMethod: string | null
  requestBodyTemplate: string | null
  queryParams: string
  headers: string
  responseMapping: string
  sortOrder: number
  isActive: boolean
  createdAt: string
  updatedAt: string | null
}

export interface EndpointTestPayload {
  path: string
  httpMethod?: string
  queryParams?: string
  headers?: string
  requestBodyTemplate?: string
  namespace: string
  testData: Record<string, string>
}

export interface EndpointTestResult {
  success: boolean
  statusCode: number | null
  body: string | null
  responseHeaders: Record<string, string> | null
  resolvedUrl: string | null
  error: string | null
}

export interface DetailApi {
  getDefinition(id: string): Promise<ApiDefinitionRecord>
  updateDefinition(id: string, data: {
    name: string
    httpMethod: string
    baseUrl: string
    description?: string
    provider?: string
    timeoutSeconds?: number
    authConfig?: string
  }): Promise<ApiDefinitionRecord>
  activateDefinition(id: string): Promise<ApiDefinitionRecord>
  deactivateDefinition(id: string): Promise<ApiDefinitionRecord>
  listEndpoints(definitionId: string): Promise<ApiEndpointRecord[]>
  createEndpoint(definitionId: string, data: EndpointFormData): Promise<ApiEndpointRecord>
  updateEndpoint(definitionId: string, endpointId: string, data: EndpointFormData): Promise<ApiEndpointRecord>
  deleteEndpoint(definitionId: string, endpointId: string): Promise<void>
  listCredentials(): Promise<string[]>
  setCredential(keyName: string, value: string): Promise<void>
  testAuth(authConfig: string): Promise<AuthTestResult>
  testEndpoint(definitionId: string, payload: EndpointTestPayload): Promise<EndpointTestResult>
  listPagePath: string
}

interface EndpointFormData {
  name: string
  path: string
  httpMethod?: string
  description?: string
  sortOrder?: number
  requestBodyTemplate?: string
  queryParams?: string
  headers?: string
}

interface DefFormState {
  name: string
  httpMethod: string
  baseUrl: string
  description: string
  provider: string
  timeoutSeconds: string
  auth: AuthFormState
}

interface KVRow { key: string; value: string; skipIfEmpty?: boolean }

interface EndpointForm {
  name: string
  path: string
  httpMethod: string
  description: string
  params: KVRow[]
  headers: KVRow[]
  requestBodyTemplate: string
  testData: Record<string, string>
}

const BLANK_KV: KVRow[] = [{ key: '', value: '' }]

const BLANK_ENDPOINT_FORM: EndpointForm = {
  name: '',
  path: '',
  httpMethod: '',
  description: '',
  params: BLANK_KV,
  headers: BLANK_KV,
  requestBodyTemplate: '',
  testData: {},
}

function kvToJson(rows: KVRow[]): string {
  const filled = rows.filter((r) => r.key.trim())
  const obj: Record<string, unknown> = {}
  const skipIfEmpty: string[] = []
  filled.forEach((r) => {
    obj[r.key.trim()] = r.value
    if (r.skipIfEmpty) skipIfEmpty.push(r.key.trim())
  })
  if (skipIfEmpty.length > 0) obj._skipIfEmpty = skipIfEmpty
  return JSON.stringify(obj)
}

function jsonToKv(json: string): KVRow[] {
  try {
    const obj = JSON.parse(json) as Record<string, unknown>
    const skipSet = new Set<string>(Array.isArray(obj._skipIfEmpty) ? (obj._skipIfEmpty as string[]) : [])
    const rows = Object.entries(obj)
      .filter(([key]) => !key.startsWith('_'))
      .map(([key, value]) => ({ key, value: String(value), skipIfEmpty: skipSet.has(key) }))
    return rows.length > 0 ? [...rows, { key: '', value: '' }] : [{ key: '', value: '' }]
  } catch {
    return [{ key: '', value: '' }]
  }
}

const HEADER_SUGGESTIONS = [
  'Accept', 'Accept-Charset', 'Accept-Encoding', 'Accept-Language',
  'Authorization', 'Cache-Control', 'Connection', 'Content-Length',
  'Content-Type', 'Cookie', 'Host', 'If-Match', 'If-Modified-Since',
  'If-None-Match', 'Origin', 'Pragma', 'Referer', 'User-Agent',
  'X-API-Key', 'X-Auth-Token', 'X-Correlation-ID', 'X-Forwarded-For',
  'X-Request-ID',
]

// ── Source context: variable reference data keyed by API type ──────────────

interface SourceField { key: string; label: string; example?: string }
interface SourceGroup { label: string; fields: SourceField[] }
interface SourceContext {
  label: string
  description: string
  namespace: string
  groups: SourceGroup[]
}

const ADDRESS_GROUPS: SourceGroup[] = [
  {
    label: 'Name',
    fields: [
      { key: 'firstName',     label: 'First Name',     example: 'John' },
      { key: 'lastName',      label: 'Last Name',      example: 'Smith' },
      { key: 'middleInitial', label: 'Middle Initial', example: 'A' },
      { key: 'company',       label: 'Company',        example: 'Acme Corp' },
    ],
  },
  {
    label: 'Address',
    fields: [
      { key: 'address1', label: 'Address Line 1', example: '123 Main St' },
      { key: 'address2', label: 'Address Line 2', example: 'Apt 4B' },
      { key: 'city',     label: 'City',           example: 'Springfield' },
      { key: 'state',    label: 'State',          example: 'IL' },
      { key: 'zip',      label: 'ZIP',            example: '62701' },
      { key: 'zip4',     label: 'ZIP+4',          example: '1234' },
    ],
  },
]

const API_TYPE_SOURCE_CONTEXT: Record<string, SourceContext> = {
  address_validation: {
    label: 'Address Node',
    description: 'Fields captured by the Address entry form',
    namespace: 'address',
    groups: ADDRESS_GROUPS,
  },
  zip_code_lookup: {
    label: 'Address Node',
    description: 'Fields captured by the Address entry form',
    namespace: 'address',
    groups: ADDRESS_GROUPS,
  },
}

interface KVEditorProps {
  rows: KVRow[]
  onChange: (rows: KVRow[]) => void
  keyPlaceholder?: string
  valuePlaceholder?: string
  datalistId?: string
  showSkipToggle?: boolean
}

function KVEditor({ rows, onChange, keyPlaceholder = 'Key', valuePlaceholder = 'Value', datalistId, showSkipToggle }: KVEditorProps) {
  function updateRow(i: number, field: 'key' | 'value', val: string) {
    const next = rows.map((r, idx) => idx === i ? { ...r, [field]: val } : r)
    if (i === rows.length - 1 && val.trim()) next.push({ key: '', value: '' })
    onChange(next)
  }

  function toggleSkip(i: number) {
    onChange(rows.map((r, idx) => idx === i ? { ...r, skipIfEmpty: !r.skipIfEmpty } : r))
  }

  function removeRow(i: number) {
    const next = rows.filter((_, idx) => idx !== i)
    if (next.length === 0) next.push({ key: '', value: '' })
    onChange(next)
  }

  return (
    <div className="space-y-1">
      {rows.map((row, i) => (
        <div key={i} className="flex items-center gap-1.5">
          <input
            type="text"
            list={datalistId}
            value={row.key}
            onChange={(e) => updateRow(i, 'key', e.target.value)}
            placeholder={keyPlaceholder}
            className="w-2/5 bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-white text-xs font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
          />
          <input
            type="text"
            value={row.value}
            onChange={(e) => updateRow(i, 'value', e.target.value)}
            placeholder={valuePlaceholder}
            className="flex-1 bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-white text-xs font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
          />
          {showSkipToggle && row.key.trim() && (
            <button
              onClick={() => toggleSkip(i)}
              title={row.skipIfEmpty ? 'Excluded from request if empty (click to always include)' : 'Always included (click to exclude if empty)'}
              className={`shrink-0 text-xs px-1.5 py-0.5 rounded border transition-colors ${
                row.skipIfEmpty
                  ? 'border-amber-600 text-amber-400 bg-amber-900/20'
                  : 'border-gray-700 text-gray-600 hover:border-gray-500 hover:text-gray-400'
              }`}
            >
              ∅
            </button>
          )}
          {showSkipToggle && !row.key.trim() && <span className="shrink-0 w-[28px]" />}
          <button
            onClick={() => removeRow(i)}
            className="text-gray-600 hover:text-red-400 transition-colors px-1 text-sm shrink-0"
            title="Remove row"
          >
            ×
          </button>
        </div>
      ))}
    </div>
  )
}

// ── VarChip ────────────────────────────────────────────────────────────────

interface VarChipProps { tag: string; label: string; example?: string; copied: boolean; onCopy: (tag: string) => void }

function VarChip({ tag, label, example, copied, onCopy }: VarChipProps) {
  return (
    <button
      type="button"
      onClick={() => onCopy(tag)}
      title={`${label}${example ? ` — e.g. "${example}"` : ''} — click to copy`}
      className="group flex items-start gap-1.5 w-full px-2 py-1.5 rounded-lg hover:bg-orange-900/20 transition-colors text-left"
    >
      <span className="font-mono text-xs text-orange-300 flex-1 min-w-0 break-all leading-relaxed">{tag}</span>
      {copied
        ? <span className="text-emerald-400 text-xs shrink-0 mt-0.5">✓</span>
        : <span className="text-gray-600 group-hover:text-gray-400 text-xs shrink-0 mt-0.5">⎘</span>
      }
    </button>
  )
}

interface Props {
  definitionId: string
  api: DetailApi
}

export default function ApiDefinitionDetailContent({ definitionId, api }: Props) {
  const navigate = useNavigate()

  const [def, setDef] = useState<ApiDefinitionRecord | null>(null)
  const [loadingDef, setLoadingDef] = useState(true)
  const [defError, setDefError] = useState<string | null>(null)

  const [endpoints, setEndpoints] = useState<ApiEndpointRecord[]>([])
  const [loadingEndpoints, setLoadingEndpoints] = useState(true)

  const [knownCreds, setKnownCreds] = useState<string[]>([])

  // Definition edit modal
  const [showDefModal, setShowDefModal] = useState(false)
  const [defForm, setDefForm] = useState<DefFormState | null>(null)
  const [defSaving, setDefSaving] = useState(false)
  const [defFormError, setDefFormError] = useState<string | null>(null)
  const [togglingActive, setTogglingActive] = useState(false)

  // Endpoint modal
  const [endpointModal, setEndpointModal] = useState<'create' | 'edit' | null>(null)
  const [editingEndpointId, setEditingEndpointId] = useState<string | null>(null)
  const [endpointForm, setEndpointForm] = useState<EndpointForm>(BLANK_ENDPOINT_FORM)
  const [endpointTab, setEndpointTab] = useState<'params' | 'headers' | 'body' | 'test'>('params')
  const [endpointSaving, setEndpointSaving] = useState(false)
  const [endpointFormError, setEndpointFormError] = useState<string | null>(null)
  const [deletingEndpointId, setDeletingEndpointId] = useState<string | null>(null)

  // Variable chip clipboard
  const [copiedVar, setCopiedVar] = useState<string | null>(null)
  function handleCopyVar(tag: string) {
    navigator.clipboard.writeText(tag).then(() => {
      setCopiedVar(tag)
      setTimeout(() => setCopiedVar(null), 1500)
    })
  }

  // Endpoint test execution
  const [testRunning, setTestRunning] = useState(false)
  const [testResult, setTestResult] = useState<EndpointTestResult | null>(null)

  async function runEndpointTest() {
    if (!endpointSourceContext) return
    setTestRunning(true)
    setTestResult(null)
    try {
      const result = await api.testEndpoint(definitionId, {
        path: endpointForm.path,
        httpMethod: endpointForm.httpMethod || undefined,
        queryParams: kvToJson(endpointForm.params),
        headers: kvToJson(endpointForm.headers),
        requestBodyTemplate: endpointForm.requestBodyTemplate || undefined,
        namespace: endpointSourceContext.namespace,
        testData: endpointForm.testData,
      })
      setTestResult(result)
    } catch (e: unknown) {
      setTestResult({ success: false, statusCode: null, body: null, responseHeaders: null, resolvedUrl: null, error: (e as Error).message })
    } finally {
      setTestRunning(false)
    }
  }

  useEffect(() => {
    loadDefinition()
    loadEndpoints()
    api.listCredentials()
      .then(setKnownCreds)
      .catch(() => {})
  }, [definitionId])

  async function loadDefinition() {
    setLoadingDef(true)
    setDefError(null)
    try {
      setDef(await api.getDefinition(definitionId))
    } catch (e: unknown) {
      setDefError((e as Error).message)
    } finally {
      setLoadingDef(false)
    }
  }

  async function loadEndpoints() {
    setLoadingEndpoints(true)
    try {
      setEndpoints(await api.listEndpoints(definitionId))
    } catch {
      // silently fail — endpoints section shows empty
    } finally {
      setLoadingEndpoints(false)
    }
  }

  function openEditDef() {
    if (!def) return
    setDefForm({
      name: def.name,
      httpMethod: def.httpMethod,
      baseUrl: def.baseUrl,
      description: def.description ?? '',
      provider: def.provider ?? '',
      timeoutSeconds: String(def.timeoutSeconds),
      auth: authStateFromConfig(def.authConfig),
    })
    setDefFormError(null)
    setShowDefModal(true)
  }

  async function handleDefSave() {
    if (!defForm) return
    if (!defForm.name.trim() || !defForm.baseUrl.trim()) {
      setDefFormError('Name and base URL are required.')
      return
    }
    setDefSaving(true)
    setDefFormError(null)
    try {
      const updated = await api.updateDefinition(definitionId, {
        name: defForm.name.trim(),
        httpMethod: defForm.httpMethod,
        baseUrl: defForm.baseUrl.trim(),
        description: defForm.description.trim() || undefined,
        provider: defForm.provider.trim() || undefined,
        timeoutSeconds: parseInt(defForm.timeoutSeconds) || 30,
        authConfig: serializeAuthConfig(defForm.auth),
      })
      setDef(updated)
      setShowDefModal(false)
    } catch (e: unknown) {
      setDefFormError((e as Error).message)
    } finally {
      setDefSaving(false)
    }
  }

  async function handleToggleActive() {
    if (!def) return
    setTogglingActive(true)
    try {
      const updated = def.isActive
        ? await api.deactivateDefinition(definitionId)
        : await api.activateDefinition(definitionId)
      setDef(updated)
    } catch { /* ignore */ } finally {
      setTogglingActive(false)
    }
  }

  function openCreateEndpoint() {
    setEndpointForm({ ...BLANK_ENDPOINT_FORM, params: [{ key: '', value: '' }], headers: [{ key: '', value: '' }] })
    setEditingEndpointId(null)
    setEndpointFormError(null)
    setEndpointTab('params')
    setEndpointModal('create')
  }

  function openEditEndpoint(ep: ApiEndpointRecord) {
    setEndpointForm({
      name: ep.name,
      path: ep.path,
      httpMethod: ep.httpMethod ?? '',
      description: ep.description ?? '',
      params: jsonToKv(ep.queryParams),
      headers: jsonToKv(ep.headers),
      requestBodyTemplate: ep.requestBodyTemplate ?? '',
      testData: {},
    })
    setEditingEndpointId(ep.id)
    setEndpointFormError(null)
    setEndpointTab('params')
    setEndpointModal('edit')
  }

  async function handleEndpointSave() {
    if (!endpointForm.name.trim() || !endpointForm.path.trim()) {
      setEndpointFormError('Name and path are required.')
      return
    }
    setEndpointSaving(true)
    setEndpointFormError(null)
    try {
      const data: EndpointFormData = {
        name: endpointForm.name.trim(),
        path: endpointForm.path.trim(),
        httpMethod: endpointForm.httpMethod.trim() || undefined,
        description: endpointForm.description.trim() || undefined,
        requestBodyTemplate: endpointForm.requestBodyTemplate.trim() || undefined,
        queryParams: kvToJson(endpointForm.params),
        headers: kvToJson(endpointForm.headers),
      }
      if (endpointModal === 'edit' && editingEndpointId) {
        const updated = await api.updateEndpoint(definitionId, editingEndpointId, data)
        setEndpoints((prev) => prev.map((ep) => (ep.id === editingEndpointId ? updated : ep)))
      } else {
        const created = await api.createEndpoint(definitionId, data)
        setEndpoints((prev) => [...prev, created])
      }
      setEndpointModal(null)
    } catch (e: unknown) {
      setEndpointFormError((e as Error).message)
    } finally {
      setEndpointSaving(false)
    }
  }

  async function handleDeleteEndpoint(id: string) {
    if (!confirm('Delete this endpoint? This cannot be undone.')) return
    setDeletingEndpointId(id)
    try {
      await api.deleteEndpoint(definitionId, id)
      setEndpoints((prev) => prev.filter((ep) => ep.id !== id))
    } catch { /* ignore */ } finally {
      setDeletingEndpointId(null)
    }
  }

  if (loadingDef) {
    return <div className="p-6 text-gray-400 text-sm">Loading…</div>
  }
  if (defError || !def) {
    return (
      <div className="p-6">
        <p className="text-red-400 text-sm mb-4">{defError ?? 'Definition not found.'}</p>
        <button onClick={() => navigate(api.listPagePath)} className="text-indigo-400 hover:text-indigo-300 text-sm">
          ← Back to API Definitions
        </button>
      </div>
    )
  }

  const authBadgeInfo = authBadge(def.authConfig)
  const endpointSourceContext = API_TYPE_SOURCE_CONTEXT[def.apiType]

  return (
    <div className="p-6">
      {/* Back nav */}
      <button
        onClick={() => navigate(api.listPagePath)}
        className="text-gray-400 hover:text-white text-sm mb-5 flex items-center gap-1.5 transition-colors"
      >
        ← API Definitions
      </button>

      {/* Definition header card */}
      <div className="bg-gray-900 border border-gray-800 rounded-xl p-5 mb-6">
        <div className="flex items-start justify-between gap-4">
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap mb-1">
              <h1 className="text-white text-xl font-semibold">{def.name}</h1>
              {def.provider && (
                <span className="bg-gray-700 text-gray-300 text-xs px-2 py-0.5 rounded font-medium">{def.provider}</span>
              )}
              <span className={`text-xs px-2 py-0.5 rounded font-medium ${API_TYPE_BADGE_COLORS[def.apiType] ?? 'bg-gray-800 text-gray-300'}`}>
                {API_TYPE_LABELS[def.apiType] ?? def.apiType}
              </span>
              <span className={`text-xs px-2 py-0.5 rounded font-medium ${def.isActive ? 'bg-emerald-900/50 text-emerald-400' : 'bg-gray-800 text-gray-500'}`}>
                {def.isActive ? 'Active' : 'Inactive'}
              </span>
            </div>
            {def.description && (
              <p className="text-gray-400 text-sm mb-2">{def.description}</p>
            )}
            <div className="flex items-center gap-3 text-sm text-gray-400 flex-wrap">
              <span className="bg-gray-800 text-gray-300 text-xs px-2 py-0.5 rounded font-mono font-medium">{def.httpMethod}</span>
              <span className="font-mono text-gray-300 truncate max-w-md" title={def.baseUrl}>{def.baseUrl}</span>
              <span className="text-gray-600">·</span>
              {authBadgeInfo
                ? <span className={`text-xs px-2 py-0.5 rounded font-medium ${authBadgeInfo.color}`}>{authBadgeInfo.label}</span>
                : <span className="text-gray-500 text-xs">No auth</span>
              }
              <span className="text-gray-600">·</span>
              <span className="text-gray-500 text-xs">{def.timeoutSeconds}s timeout</span>
            </div>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <button
              onClick={handleToggleActive}
              disabled={togglingActive}
              className={`text-xs font-medium px-3 py-1.5 rounded-lg border transition-colors disabled:opacity-50 ${
                def.isActive
                  ? 'border-amber-700 text-amber-400 hover:bg-amber-900/20'
                  : 'border-emerald-700 text-emerald-400 hover:bg-emerald-900/20'
              }`}
            >
              {togglingActive ? '…' : def.isActive ? 'Deactivate' : 'Activate'}
            </button>
            <button
              onClick={openEditDef}
              className="bg-indigo-600 hover:bg-indigo-500 text-white text-sm font-medium px-4 py-1.5 rounded-lg transition-colors"
            >
              Edit Definition
            </button>
          </div>
        </div>
      </div>

      {/* Endpoints section */}
      <div>
        <div className="flex items-center justify-between mb-3">
          <div>
            <h2 className="text-white font-medium">Endpoints</h2>
            <p className="text-gray-500 text-xs mt-0.5">Individual API operations for this definition</p>
          </div>
          <button
            onClick={openCreateEndpoint}
            className="bg-gray-800 hover:bg-gray-700 border border-gray-700 text-white text-sm font-medium px-3 py-1.5 rounded-lg transition-colors"
          >
            + Add Endpoint
          </button>
        </div>

        {loadingEndpoints && <p className="text-gray-400 text-sm">Loading endpoints…</p>}

        {!loadingEndpoints && endpoints.length === 0 && (
          <div className="bg-gray-900 border border-gray-800 border-dashed rounded-xl p-8 text-center">
            <p className="text-gray-500 text-sm">No endpoints defined yet.</p>
            <p className="text-gray-600 text-xs mt-1">Add the specific API paths that calls will use (e.g., <code className="text-gray-500">/v3/addresses</code>).</p>
          </div>
        )}

        {!loadingEndpoints && endpoints.length > 0 && (
          <div className="bg-gray-900 rounded-xl overflow-hidden border border-gray-800">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 text-gray-400 text-left">
                  <th className="px-4 py-3 font-medium">Name</th>
                  <th className="px-4 py-3 font-medium">Method</th>
                  <th className="px-4 py-3 font-medium">Path</th>
                  <th className="px-4 py-3 font-medium">Description</th>
                  <th className="px-4 py-3 font-medium"></th>
                </tr>
              </thead>
              <tbody>
                {endpoints.map((ep) => (
                  <tr key={ep.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30 transition-colors">
                    <td className="px-4 py-3 text-white font-medium">{ep.name}</td>
                    <td className="px-4 py-3">
                      <span className="bg-gray-800 text-gray-300 text-xs px-2 py-0.5 rounded font-mono">
                        {ep.httpMethod ?? 'inherit'}
                      </span>
                    </td>
                    <td className="px-4 py-3 font-mono text-gray-300 text-xs">{ep.path}</td>
                    <td className="px-4 py-3 text-gray-400 text-xs max-w-xs truncate">
                      {ep.description ?? <span className="text-gray-600">—</span>}
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-3">
                        <button
                          onClick={() => openEditEndpoint(ep)}
                          className="text-indigo-400 hover:text-indigo-300 text-xs font-medium transition-colors"
                        >
                          Edit
                        </button>
                        <button
                          onClick={() => handleDeleteEndpoint(ep.id)}
                          disabled={deletingEndpointId === ep.id}
                          className="text-red-400 hover:text-red-300 text-xs font-medium transition-colors disabled:opacity-50"
                        >
                          {deletingEndpointId === ep.id ? '…' : 'Delete'}
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

      {/* ── Edit Definition Modal ── */}
      {showDefModal && defForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
          <div className="bg-gray-900 border border-gray-700 rounded-2xl w-full max-w-2xl shadow-2xl flex flex-col max-h-[90vh]">
            <div className="px-6 pt-6 pb-4 border-b border-gray-800 shrink-0">
              <h2 className="text-white text-lg font-semibold">Edit API Definition</h2>
            </div>
            <div className="overflow-y-auto flex-1 px-6 py-4 space-y-4">
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">Name *</label>
                  <input
                    type="text"
                    value={defForm.name}
                    onChange={(e) => setDefForm((f) => f ? { ...f, name: e.target.value } : f)}
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                </div>
                <div>
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">Provider</label>
                  <input
                    type="text"
                    value={defForm.provider}
                    onChange={(e) => setDefForm((f) => f ? { ...f, provider: e.target.value } : f)}
                    placeholder="USPS, Google Places…"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                </div>
              </div>
              <div>
                <label className="block text-gray-400 text-xs font-medium mb-1.5">Description</label>
                <input
                  type="text"
                  value={defForm.description}
                  onChange={(e) => setDefForm((f) => f ? { ...f, description: e.target.value } : f)}
                  placeholder="Optional description"
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                />
              </div>
              <div className="grid grid-cols-5 gap-3">
                <div>
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">Method</label>
                  <select
                    value={defForm.httpMethod}
                    onChange={(e) => setDefForm((f) => f ? { ...f, httpMethod: e.target.value } : f)}
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
                  >
                    {HTTP_METHODS.map((m) => <option key={m} value={m}>{m}</option>)}
                  </select>
                </div>
                <div className="col-span-3">
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">Base URL *</label>
                  <input
                    type="text"
                    value={defForm.baseUrl}
                    onChange={(e) => setDefForm((f) => f ? { ...f, baseUrl: e.target.value } : f)}
                    placeholder="https://api.example.com"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                </div>
                <div>
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">Timeout (s)</label>
                  <input
                    type="number"
                    min="1"
                    max="300"
                    value={defForm.timeoutSeconds}
                    onChange={(e) => setDefForm((f) => f ? { ...f, timeoutSeconds: e.target.value } : f)}
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
                  />
                </div>
              </div>
              <div>
                <p className="text-gray-400 text-xs font-medium uppercase tracking-wide mb-2">Authentication</p>
                <AuthConfigForm
                  state={defForm.auth}
                  onChange={(patch) => setDefForm((f) => f ? { ...f, auth: { ...f.auth, ...patch } } : f)}
                  knownCredentials={knownCreds}
                  onAddCredential={async (keyName, value) => {
                    await api.setCredential(keyName, value)
                    setKnownCreds((prev) => [...prev, keyName])
                  }}
                  onTestAuth={() => api.testAuth(serializeAuthConfig(defForm.auth))}
                />
              </div>
            </div>
            <div className="px-6 py-4 border-t border-gray-800 shrink-0">
              {defFormError && <p className="text-red-400 text-sm mb-3">{defFormError}</p>}
              <div className="flex justify-end gap-3">
                <button
                  onClick={() => setShowDefModal(false)}
                  disabled={defSaving}
                  className="px-4 py-2 text-sm text-gray-400 hover:text-white transition-colors disabled:opacity-50"
                >
                  Cancel
                </button>
                <button
                  onClick={handleDefSave}
                  disabled={defSaving}
                  className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm font-medium px-5 py-2 rounded-lg transition-colors"
                >
                  {defSaving ? 'Saving…' : 'Save changes'}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ── Add / Edit Endpoint Modal ── */}
      {endpointModal && (
        <>
          {/* datalist for header name autocomplete */}
          <datalist id="header-key-suggestions">
            {HEADER_SUGGESTIONS.map((h) => <option key={h} value={h} />)}
          </datalist>

          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
            <div className={`bg-gray-900 border border-gray-700 rounded-2xl w-full shadow-2xl flex flex-col max-h-[90vh] ${endpointSourceContext ? 'max-w-4xl' : 'max-w-2xl'}`}>
              <div className="px-6 pt-6 pb-4 border-b border-gray-800 shrink-0">
                <h2 className="text-white text-lg font-semibold">
                  {endpointModal === 'edit' ? 'Edit Endpoint' : 'Add Endpoint'}
                </h2>
              </div>
              <div className="flex flex-1 min-h-0">
              <div className="overflow-y-auto flex-1 px-6 py-4 space-y-4">

                {/* Name + Method */}
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="block text-gray-400 text-xs font-medium mb-1.5">Name *</label>
                    <input
                      type="text"
                      value={endpointForm.name}
                      onChange={(e) => setEndpointForm((f) => ({ ...f, name: e.target.value }))}
                      placeholder="Address Validate"
                      className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                    />
                  </div>
                  <div>
                    <label className="block text-gray-400 text-xs font-medium mb-1.5">HTTP Method</label>
                    <select
                      value={endpointForm.httpMethod}
                      onChange={(e) => setEndpointForm((f) => ({ ...f, httpMethod: e.target.value }))}
                      className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
                    >
                      <option value="">Inherit from definition</option>
                      {HTTP_METHODS.map((m) => <option key={m} value={m}>{m}</option>)}
                    </select>
                  </div>
                </div>

                {/* Path */}
                <div>
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">Path *</label>
                  <input
                    type="text"
                    value={endpointForm.path}
                    onChange={(e) => setEndpointForm((f) => ({ ...f, path: e.target.value }))}
                    placeholder="/v3/address"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 font-mono focus:outline-none focus:border-indigo-500"
                  />
                  <p className="text-gray-600 text-xs mt-1">Relative path appended to the base URL. Start with /</p>
                </div>

                {/* Description */}
                <div>
                  <label className="block text-gray-400 text-xs font-medium mb-1.5">Description</label>
                  <input
                    type="text"
                    value={endpointForm.description}
                    onChange={(e) => setEndpointForm((f) => ({ ...f, description: e.target.value }))}
                    placeholder="Optional description"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                </div>

                {/* Tabs: Params | Headers | Body */}
                <div>
                  {/* Tab bar */}
                  <div className="flex border-b border-gray-700 mb-3">
                    {(endpointSourceContext
                      ? ['params', 'headers', 'body', 'test'] as const
                      : ['params', 'headers', 'body'] as const
                    ).map((tab) => {
                      const counts: Record<string, number> = {
                        params: endpointForm.params.filter(r => r.key.trim()).length,
                        headers: endpointForm.headers.filter(r => r.key.trim()).length,
                        body: endpointForm.requestBodyTemplate.trim() ? 1 : 0,
                        test: 0,
                      }
                      const labels: Record<string, string> = { params: 'Query Params', headers: 'Headers', body: 'Body', test: 'Test' }
                      const count = counts[tab]
                      return (
                        <button
                          key={tab}
                          onClick={() => { setEndpointTab(tab); setTestResult(null) }}
                          className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors flex items-center gap-1.5 -mb-px ${
                            endpointTab === tab
                              ? tab === 'test' ? 'border-emerald-500 text-emerald-400' : 'border-indigo-500 text-white'
                              : 'border-transparent text-gray-500 hover:text-gray-300'
                          }`}
                        >
                          {labels[tab]}
                          {count > 0 && (
                            <span className="bg-indigo-600 text-white text-xs rounded-full w-4 h-4 flex items-center justify-center leading-none">
                              {count}
                            </span>
                          )}
                        </button>
                      )
                    })}
                  </div>

                  {/* Query Params tab */}
                  {endpointTab === 'params' && (
                    <div>
                      <div className="flex items-center gap-1.5 mb-1.5 px-0.5">
                        <span className="w-2/5 text-gray-500 text-xs font-medium">Key</span>
                        <span className="flex-1 text-gray-500 text-xs font-medium">Value</span>
                        <span className="w-[28px]" />
                        <span className="w-5" />
                      </div>
                      <KVEditor
                        rows={endpointForm.params}
                        onChange={(rows) => setEndpointForm((f) => ({ ...f, params: rows }))}
                        keyPlaceholder="param"
                        valuePlaceholder="{{input.field}}"
                        showSkipToggle
                      />
                      <p className="text-gray-600 text-xs mt-2">
                        Parameters appended to the URL as <span className="font-mono">?key=value</span>. Use {'{{namespace.field}}'} for dynamic values.
                        Toggle <span className="font-mono text-amber-500">∅</span> on optional params to exclude them from the request when their value resolves to empty.
                      </p>
                    </div>
                  )}

                  {/* Headers tab */}
                  {endpointTab === 'headers' && (
                    <div>
                      <div className="flex items-center gap-1.5 mb-1.5 px-0.5">
                        <span className="w-2/5 text-gray-500 text-xs font-medium">Header</span>
                        <span className="flex-1 text-gray-500 text-xs font-medium">Value</span>
                        <span className="w-5" />
                      </div>
                      <KVEditor
                        rows={endpointForm.headers}
                        onChange={(rows) => setEndpointForm((f) => ({ ...f, headers: rows }))}
                        keyPlaceholder="Header name"
                        valuePlaceholder="application/json"
                        datalistId="header-key-suggestions"
                      />
                      <p className="text-gray-600 text-xs mt-2">
                        Request headers for this endpoint. These are merged with any headers on the definition.
                      </p>
                    </div>
                  )}

                  {/* Body tab */}
                  {endpointTab === 'body' && (
                    <div>
                      <textarea
                        rows={8}
                        value={endpointForm.requestBodyTemplate}
                        onChange={(e) => setEndpointForm((f) => ({ ...f, requestBodyTemplate: e.target.value }))}
                        placeholder={
                          endpointSourceContext?.namespace === 'address'
                            ? '{\n  "streetAddress": "{{address.address1}}",\n  "city": "{{address.city}}",\n  "state": "{{address.state}}",\n  "ZIPCode": "{{address.zip}}"\n}'
                            : '{\n  "field": "{{input.field}}"\n}'
                        }
                        className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-xs placeholder-gray-600 font-mono focus:outline-none focus:border-indigo-500 resize-y"
                      />
                      <p className="text-gray-600 text-xs mt-1.5">
                        Use {'{{namespace.field}}'} variable syntax. Leave blank to inherit from the definition.
                      </p>
                    </div>
                  )}

                  {/* Test tab */}
                  {endpointTab === 'test' && endpointSourceContext?.namespace === 'address' && (
                    <div className="space-y-3">
                      {/* Name row */}
                      <div className="grid grid-cols-[1fr_56px_1fr] gap-2">
                        <div>
                          <label className="block text-gray-400 text-xs font-medium mb-1">First Name</label>
                          <input type="text" value={endpointForm.testData.firstName ?? ''} onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, firstName: e.target.value } }))} placeholder="John" className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500" />
                        </div>
                        <div>
                          <label className="block text-gray-400 text-xs font-medium mb-1">MI</label>
                          <input type="text" maxLength={2} value={endpointForm.testData.middleInitial ?? ''} onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, middleInitial: e.target.value } }))} placeholder="A" className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500" />
                        </div>
                        <div>
                          <label className="block text-gray-400 text-xs font-medium mb-1">Last Name</label>
                          <input type="text" value={endpointForm.testData.lastName ?? ''} onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, lastName: e.target.value } }))} placeholder="Smith" className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500" />
                        </div>
                      </div>

                      {/* Company */}
                      <div>
                        <label className="block text-gray-400 text-xs font-medium mb-1">Company</label>
                        <input type="text" value={endpointForm.testData.company ?? ''} onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, company: e.target.value } }))} placeholder="Acme Corp" className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500" />
                      </div>

                      {/* Address lines */}
                      <div>
                        <label className="block text-gray-400 text-xs font-medium mb-1">Address Line 1</label>
                        <input type="text" value={endpointForm.testData.address1 ?? ''} onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, address1: e.target.value } }))} placeholder="123 Main St" className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500" />
                      </div>
                      <div>
                        <label className="block text-gray-400 text-xs font-medium mb-1">Address Line 2</label>
                        <input type="text" value={endpointForm.testData.address2 ?? ''} onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, address2: e.target.value } }))} placeholder="Apt 4B, Suite 200…" className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500" />
                      </div>

                      {/* City / State / ZIP / ZIP+4 */}
                      <div className="grid grid-cols-[1fr_64px_96px_72px] gap-2">
                        <div>
                          <label className="block text-gray-400 text-xs font-medium mb-1">City</label>
                          <input type="text" value={endpointForm.testData.city ?? ''} onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, city: e.target.value } }))} placeholder="Springfield" className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500" />
                        </div>
                        <div>
                          <label className="block text-gray-400 text-xs font-medium mb-1">State</label>
                          <input type="text" maxLength={2} value={endpointForm.testData.state ?? ''} onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, state: e.target.value.toUpperCase() } }))} placeholder="IL" className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500 uppercase" />
                        </div>
                        <div>
                          <label className="block text-gray-400 text-xs font-medium mb-1">ZIP Code</label>
                          <input type="text" maxLength={5} value={endpointForm.testData.zip ?? ''} onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, zip: e.target.value.replace(/\D/g, '') } }))} placeholder="62701" className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500" />
                        </div>
                        <div>
                          <label className="block text-gray-400 text-xs font-medium mb-1">ZIP+4</label>
                          <input type="text" maxLength={4} value={endpointForm.testData.zip4 ?? ''} onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, zip4: e.target.value.replace(/\D/g, '') } }))} placeholder="1234" className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500" />
                        </div>
                      </div>

                      {/* Run Test */}
                      <div className="pt-3 border-t border-gray-800 flex items-center gap-3">
                        <button
                          onClick={runEndpointTest}
                          disabled={testRunning || !endpointForm.path.trim()}
                          className="bg-emerald-700 hover:bg-emerald-600 disabled:opacity-50 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors"
                        >
                          {testRunning ? 'Running…' : 'Run Test'}
                        </button>
                        {!endpointForm.path.trim() && (
                          <span className="text-gray-500 text-xs">Enter a path on the main form first.</span>
                        )}
                      </div>

                      {/* Test result */}
                      {testResult && (
                        <div className="space-y-2">
                          {testResult.resolvedUrl && (
                            <p className="font-mono text-xs text-gray-500 truncate" title={testResult.resolvedUrl}>
                              → {testResult.resolvedUrl}
                            </p>
                          )}
                          <div className="flex items-center gap-2">
                            {testResult.statusCode != null ? (
                              <span className={`text-sm font-mono font-bold ${testResult.statusCode < 300 ? 'text-emerald-400' : testResult.statusCode < 500 ? 'text-amber-400' : 'text-red-400'}`}>
                                {testResult.statusCode}
                              </span>
                            ) : null}
                            <span className={`text-xs font-medium ${testResult.success ? 'text-emerald-400' : 'text-red-400'}`}>
                              {testResult.success ? 'Success' : 'Failed'}
                            </span>
                          </div>
                          {testResult.error && (
                            <p className="text-red-400 text-xs bg-red-900/20 rounded-lg px-3 py-2">{testResult.error}</p>
                          )}
                          {testResult.body && (
                            <pre className="bg-gray-950 border border-gray-800 rounded-lg p-3 text-xs text-gray-300 font-mono overflow-x-auto max-h-52 whitespace-pre-wrap break-all">
                              {testResult.body}
                            </pre>
                          )}
                        </div>
                      )}
                    </div>
                  )}
                </div>

              </div>

              {/* ── Variables reference panel ── */}
              {endpointSourceContext && (
                <div className="w-56 shrink-0 border-l border-gray-800 px-4 py-4 overflow-y-auto">
                  <p className="text-white text-xs font-semibold mb-0.5">Variables</p>
                  <p className="text-gray-500 text-xs mb-3">{endpointSourceContext.description}</p>
                  {endpointSourceContext.groups.map((group) => (
                    <div key={group.label} className="mb-4">
                      <p className="text-gray-500 text-[10px] font-semibold uppercase tracking-wider mb-1.5">
                        {group.label}
                      </p>
                      <div className="space-y-0.5">
                        {group.fields.map((field) => {
                          const tag = `{{${endpointSourceContext.namespace}.${field.key}}}`
                          return (
                            <VarChip
                              key={field.key}
                              tag={tag}
                              label={field.label}
                              example={field.example}
                              copied={copiedVar === tag}
                              onCopy={handleCopyVar}
                            />
                          )
                        })}
                      </div>
                    </div>
                  ))}
                  <p className="text-gray-600 text-xs mt-1 leading-relaxed">
                    Click any variable to copy it to the clipboard.
                  </p>
                </div>
              )}

              </div>{/* end flex row */}
              <div className="px-6 py-4 border-t border-gray-800 shrink-0">
                {endpointFormError && <p className="text-red-400 text-sm mb-3">{endpointFormError}</p>}
                <div className="flex justify-end gap-3">
                  <button
                    onClick={() => setEndpointModal(null)}
                    disabled={endpointSaving}
                    className="px-4 py-2 text-sm text-gray-400 hover:text-white transition-colors disabled:opacity-50"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={handleEndpointSave}
                    disabled={endpointSaving}
                    className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm font-medium px-5 py-2 rounded-lg transition-colors"
                  >
                    {endpointSaving ? 'Saving…' : endpointModal === 'edit' ? 'Save changes' : 'Add endpoint'}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </>
      )}
    </div>
  )
}
