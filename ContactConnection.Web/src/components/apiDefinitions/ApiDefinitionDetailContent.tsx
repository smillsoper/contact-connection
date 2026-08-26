import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import AuthConfigForm, {
  type AuthFormState,
  authStateFromConfig,
  serializeAuthConfig,
} from './AuthConfigForm'
import type { AuthTestResult } from '../../api/adminApiDefinitions'
import type { EntityVersionSummary } from '../../api/versioning'
import VersionHistoryPanel from '../versioning/VersionHistoryPanel'
import JsonTree from '../shared/JsonTree'
import {
  API_CATEGORIES,
  API_CATEGORY_LABELS,
  API_SUB_TYPES,
  API_SUB_TYPE_LABELS,
  API_CATEGORY_BADGE_COLORS,
  API_SUB_TYPE_BADGE_COLORS,
  TTS_PROVIDER_LABELS,
  authTypeBadge,
} from '../../constants/apiTypes'

const HTTP_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']

export interface ApiDefinitionRecord {
  id: string
  apiCategory: string
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
  /** Outbound requests/minute allowed against this definition, or null for unlimited. A Portal
   *  definition's limit is shared across every tenant using it — see
   *  API_HARDENING_CHECKLIST.md Tier 2. */
  rateLimitPerMinute: number | null
  createdAt: string
  updatedAt: string | null
}

export interface ApiEndpointRecord {
  id: string
  definitionId: string
  apiSubType: string
  name: string
  description: string | null
  path: string
  httpMethod: string | null
  requestBodyTemplate: string | null
  queryParams: string
  headers: string
  responseMapping: string
  sortOrder: number
  isPreferred: boolean
  isActive: boolean
  isRetrySafe: boolean
  /** JSON array of dot-separated response field paths (e.g. ["ssn","customer.dob"]) redacted
   *  before the response is shown in the Test panel or persisted by a live api_call node. See
   *  API_HARDENING_CHECKLIST.md Tier 3. */
  sensitiveResponseFields: string
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
  sensitiveResponseFields?: string[]
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
    apiCategory?: string
    description?: string
    provider?: string
    timeoutSeconds?: number
    authConfig?: string
    /** Omit to leave unchanged, 0 to clear back to unlimited, or a positive number to set a new
     *  limit. */
    rateLimitPerMinute?: number
  }): Promise<ApiDefinitionRecord>
  activateDefinition(id: string): Promise<ApiDefinitionRecord>
  deactivateDefinition(id: string): Promise<ApiDefinitionRecord>
  listEndpoints(definitionId: string): Promise<ApiEndpointRecord[]>
  createEndpoint(definitionId: string, data: EndpointFormData): Promise<ApiEndpointRecord>
  updateEndpoint(definitionId: string, endpointId: string, data: EndpointFormData): Promise<ApiEndpointRecord>
  setPreferred(definitionId: string, endpointId: string): Promise<ApiEndpointRecord>
  deleteEndpoint(definitionId: string, endpointId: string): Promise<void>
  listCredentials(): Promise<string[]>
  setCredential(keyName: string, value: string): Promise<void>
  testAuth(authConfig: string): Promise<AuthTestResult>
  testEndpoint(definitionId: string, payload: EndpointTestPayload): Promise<EndpointTestResult>
  /** Live-registered TTS streaming provider keys (see TtsProviderValidation) — wired on both
   *  Admin and Portal, since a tenant registering their own TTS vendor account is subject to the
   *  same constraint as the platform catalog. Optional only because it's resolved via a
   *  useEffect that tolerates a missing implementation, not because either side omits it. */
  listTtsProviders?(): Promise<string[]>
  listPagePath: string
  // Version history — every write is retained forever; revert applies a past snapshot and
  // records it as a brand-new version (see API_HARDENING_CHECKLIST.md Tier 1).
  listDefinitionVersions(id: string): Promise<EntityVersionSummary[]>
  revertDefinition(id: string, versionNumber: number): Promise<ApiDefinitionRecord>
  listEndpointVersions(definitionId: string, endpointId: string): Promise<EntityVersionSummary[]>
  revertEndpoint(definitionId: string, endpointId: string, versionNumber: number): Promise<ApiEndpointRecord>
}

interface EndpointFormData {
  apiSubType: string
  name: string
  path: string
  httpMethod?: string
  description?: string
  sortOrder?: number
  requestBodyTemplate?: string
  queryParams?: string
  headers?: string
  responseMapping?: string
  isRetrySafe?: boolean
  sensitiveResponseFields?: string
}

interface DefFormState {
  apiCategory: string
  name: string
  httpMethod: string
  baseUrl: string
  description: string
  provider: string
  timeoutSeconds: string
  rateLimitPerMinute: string
  auth: AuthFormState
}

interface KVRow { key: string; value: string; skipIfEmpty?: boolean }

// ── Response mapping types ─────────────────────────────────────────────────

type ConditionOp =
  | 'eq' | 'neq' | 'gt' | 'lt' | 'gte' | 'lte'
  | 'contains' | 'not_contains'
  | 'exists' | 'not_exists'
  | 'length_gt' | 'length_eq' | 'length_lt'

interface OutcomeCondition {
  path: string
  op: ConditionOp
  value?: string
}

interface OutcomeFieldMapping {
  from: string
  to: string
}

interface CapturedResponse {
  capturedAt: string
  statusCode: number
  resolvedUrl: string | null
  body: unknown
}

interface MultipleMatchesConfig {
  arrayPath: string
  itemMappings: OutcomeFieldMapping[]
}

interface ResponseOutcome {
  id: string
  name: string
  label: string
  conditions: OutcomeCondition[]
  conditionLogic?: 'and' | 'or'
  messagePath?: string
  multipleMatchesConfig?: MultipleMatchesConfig
  fieldMappings: OutcomeFieldMapping[]
  capturedResponse: CapturedResponse | null
  // ZIP code lookup dedicated path fields (zip_code_lookup sub-type only)
  mainCityPath?: string
  cityAliasesPath?: string
  statePath?: string
  latitudePath?: string
  longitudePath?: string
}

interface AutocompleteSuggestionsConfig {
  arrayPath: string
  placeIdPath: string
  displayTextPath: string
}

interface AutocompleteDetailsConfig {
  path: string
  method: string
  headers: Record<string, string>
  fieldMappings: OutcomeFieldMapping[]
}

interface AutocompleteConfig {
  suggestions: AutocompleteSuggestionsConfig
  details: AutocompleteDetailsConfig
}

function defaultAutocompleteConfig(): AutocompleteConfig {
  return {
    suggestions: { arrayPath: '', placeIdPath: '', displayTextPath: '' },
    details: { path: '', method: 'GET', headers: {}, fieldMappings: [] },
  }
}

interface ResponseMappingConfig {
  outcomes: ResponseOutcome[]
  autocompleteConfig?: AutocompleteConfig
}

type RawOutcome = Omit<ResponseOutcome, 'conditions'> & { condition?: OutcomeCondition; conditions?: OutcomeCondition[] }

function walkPath(val: unknown, segments: string[]): unknown {
  for (const segment of segments) {
    if (val == null || typeof val !== 'object') return undefined
    const arrMatch = segment.match(/^(\w*)\[(\d+)\]$/)
    if (arrMatch) {
      const [, key, idx] = arrMatch
      if (key) val = (val as Record<string, unknown>)[key]
      val = Array.isArray(val) ? val[parseInt(idx)] : undefined
    } else {
      val = (val as Record<string, unknown>)[segment]
    }
  }
  return val
}

function resolveFromTemplate(body: unknown, template: string): string | null {
  if (!template.includes('{{')) return null
  const result = template.replace(/\{\{([^}]+)\}\}/g, (_, path: string) => {
    const val = walkPath(body, path.trim().split('.'))
    if (val == null || typeof val === 'object') return ''
    return String(val)
  }).trim().replace(/\s{2,}/g, ' ')
  return result || null
}

function resolveMessagePreview(body: unknown, messagePath: string): string | null {
  const wildcardIdx = messagePath.indexOf('[*]')
  if (wildcardIdx >= 0) {
    const arrayPath = messagePath.slice(0, wildcardIdx).replace(/\.$/, '')
    const fieldPath = messagePath.slice(wildcardIdx + 3).replace(/^\./, '')
    const arrayVal  = arrayPath ? walkPath(body, arrayPath.split('.')) : body
    if (!Array.isArray(arrayVal)) return null
    const parts = arrayVal
      .map(item => fieldPath ? walkPath(item, fieldPath.split('.')) : item)
      .filter(v => v != null && typeof v !== 'object')
      .map(v => String(v))
    return parts.length > 0 ? parts.join('\n') : null
  }
  const val = walkPath(body, messagePath.split('.'))
  if (val == null || typeof val === 'object') return null
  return String(val)
}

function parseResponseMapping(json: string): ResponseMappingConfig {
  try {
    const obj = JSON.parse(json) as { outcomes?: RawOutcome[]; autocompleteConfig?: AutocompleteConfig }
    const autocompleteConfig = obj.autocompleteConfig
    if (!Array.isArray(obj?.outcomes)) return { outcomes: [], autocompleteConfig }
    const outcomes: ResponseOutcome[] = obj.outcomes.map(o => {
      if (Array.isArray(o.conditions) && o.conditions.length > 0) {
        const { condition: _legacy, ...rest } = o
        return { ...rest, conditions: o.conditions }
      }
      if (o.condition) {
        const { condition, ...rest } = o
        return { ...rest, conditions: [condition], conditionLogic: 'and' as const }
      }
      const { condition: _c, ...rest } = o
      return { ...rest, conditions: [{ path: '', op: 'eq' as ConditionOp, value: '' }] }
    })
    return { outcomes, autocompleteConfig }
  } catch {
    return { outcomes: [] }
  }
}

interface EndpointForm {
  apiSubType: string
  name: string
  path: string
  httpMethod: string
  description: string
  params: KVRow[]
  headers: KVRow[]
  requestBodyTemplate: string
  testData: Record<string, string>
  payloadBody: string
  responseMapping: ResponseMappingConfig
  isRetrySafe: boolean
  /** One dot-separated field path per line (e.g. "ssn", "customer.dob") — see
   *  pathsToJson/jsonToPaths for the JSON array round-trip. */
  sensitiveResponseFields: string
}

const BLANK_KV: KVRow[] = [{ key: '', value: '' }]

const BLANK_ENDPOINT_FORM: EndpointForm = {
  apiSubType: '',
  name: '',
  path: '',
  httpMethod: '',
  description: '',
  params: BLANK_KV,
  headers: BLANK_KV,
  requestBodyTemplate: '',
  testData: {},
  payloadBody: '',
  responseMapping: { outcomes: [] },
  isRetrySafe: false,
  sensitiveResponseFields: '',
}

/** One path per line (blank lines ignored) <-> JSON array of dot-separated paths. */
function pathsToArray(text: string): string[] {
  return text.split('\n').map((l) => l.trim()).filter(Boolean)
}
function pathsToJson(text: string): string {
  return JSON.stringify(pathsToArray(text))
}
function jsonToPaths(json: string): string {
  try {
    const arr = JSON.parse(json)
    return Array.isArray(arr) ? arr.join('\n') : ''
  } catch {
    return ''
  }
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

// Finds every {{...}} tag across a set of template strings (path, query param/header values,
// body template) and returns the unique, trimmed tag contents — used to build the General API
// Test tab's prompt-for-each-variable form, since general endpoints aren't scoped to any single
// fixed namespace the way address/order/fulfillment/media sub-types are.
function extractVarTags(sources: (string | undefined)[]): string[] {
  const tagPattern = /\{\{([^}]+)\}\}/g
  const found = new Set<string>()
  for (const src of sources) {
    if (!src) continue
    let m: RegExpExecArray | null
    while ((m = tagPattern.exec(src)) !== null) found.add(m[1].trim())
  }
  return Array.from(found).sort()
}

function substituteVarTags(template: string, values: Record<string, string>): string {
  return template.replace(/\{\{([^}]+)\}\}/g, (_match, tag: string) => values[tag.trim()] ?? '')
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
      { key: 'address1',  label: 'Address Line 1', example: '123 Main St' },
      { key: 'address2',  label: 'Address Line 2', example: 'Apt 4B' },
      { key: 'city',      label: 'City',           example: 'Springfield' },
      { key: 'state',     label: 'State',          example: 'IL' },
      { key: 'zip',       label: 'ZIP',            example: '62701' },
      { key: 'zip4',      label: 'ZIP+4',          example: '1234' },
      { key: 'latitude',  label: 'Latitude',       example: '39.7817' },
      { key: 'longitude', label: 'Longitude',      example: '-89.6501' },
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
  realtime_address_autocomplete: {
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

// ── ConditionBuilder ────────────────────────────────────────────────────────

const CONDITION_OPS: { value: ConditionOp; label: string }[] = [
  { value: 'eq',           label: '= equals' },
  { value: 'neq',          label: '≠ not equals' },
  { value: 'gt',           label: '> greater than' },
  { value: 'lt',           label: '< less than' },
  { value: 'gte',          label: '≥ or equal' },
  { value: 'lte',          label: '≤ or equal' },
  { value: 'contains',     label: 'contains' },
  { value: 'not_contains', label: 'not contains' },
  { value: 'exists',       label: 'field exists' },
  { value: 'not_exists',   label: 'field absent' },
  { value: 'length_gt',    label: 'length >' },
  { value: 'length_eq',    label: 'length =' },
  { value: 'length_lt',    label: 'length <' },
]

function ConditionRow({ condition, onChange, onRemove, showRemove }: {
  condition: OutcomeCondition
  onChange: (c: OutcomeCondition) => void
  onRemove: () => void
  showRemove: boolean
}) {
  const noValue = condition.op === 'exists' || condition.op === 'not_exists'
  return (
    <div className="flex items-end gap-2">
      <div className="flex-1">
        <label className="block text-gray-500 text-[10px] font-medium mb-1 uppercase tracking-wider">Response path</label>
        <input
          type="text"
          value={condition.path}
          onChange={(e) => onChange({ ...condition, path: e.target.value })}
          placeholder="results.0.data.details.record_type.code"
          className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-white text-xs font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
        />
      </div>
      <div className="shrink-0">
        <label className="block text-gray-500 text-[10px] font-medium mb-1 uppercase tracking-wider">Operator</label>
        <select
          value={condition.op}
          onChange={(e) => onChange({ ...condition, op: e.target.value as ConditionOp })}
          className="bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-white text-xs focus:outline-none focus:border-indigo-500"
        >
          {CONDITION_OPS.map(op => <option key={op.value} value={op.value}>{op.label}</option>)}
        </select>
      </div>
      {!noValue && (
        <div className="flex-1">
          <label className="block text-gray-500 text-[10px] font-medium mb-1 uppercase tracking-wider">Value</label>
          <input
            type="text"
            value={condition.value ?? ''}
            onChange={(e) => onChange({ ...condition, value: e.target.value })}
            placeholder="0"
            className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-white text-xs font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
          />
        </div>
      )}
      {showRemove && (
        <button
          onClick={onRemove}
          className="shrink-0 mb-0.5 text-gray-600 hover:text-red-400 text-lg leading-none px-1"
          title="Remove condition"
        >×</button>
      )}
    </div>
  )
}

function ConditionListBuilder({ conditions, conditionLogic, onChange }: {
  conditions: OutcomeCondition[]
  conditionLogic: 'and' | 'or'
  onChange: (conditions: OutcomeCondition[], conditionLogic: 'and' | 'or') => void
}) {
  function updateCondition(i: number, c: OutcomeCondition) {
    const next = conditions.map((existing, idx) => idx === i ? c : existing)
    onChange(next, conditionLogic)
  }
  function removeCondition(i: number) {
    onChange(conditions.filter((_, idx) => idx !== i), conditionLogic)
  }
  function addCondition() {
    onChange([...conditions, { path: '', op: 'eq', value: '' }], conditionLogic)
  }

  return (
    <div className="space-y-2">
      {conditions.map((cond, i) => (
        <div key={i}>
          {i > 0 && (
            <div className="flex items-center gap-2 my-1.5">
              <div className="flex-1 border-t border-gray-700/50" />
              <button
                onClick={() => onChange(conditions, conditionLogic === 'and' ? 'or' : 'and')}
                className="text-[10px] font-semibold px-2 py-0.5 rounded border border-gray-600 text-gray-400 hover:border-indigo-500 hover:text-indigo-400 uppercase tracking-wider"
                title="Click to toggle AND / OR"
              >{conditionLogic}</button>
              <div className="flex-1 border-t border-gray-700/50" />
            </div>
          )}
          <ConditionRow
            condition={cond}
            onChange={(c) => updateCondition(i, c)}
            onRemove={() => removeCondition(i)}
            showRemove={conditions.length > 1}
          />
        </div>
      ))}
      <div className="flex items-center justify-between pt-1">
        <p className="text-gray-600 text-[10px]">
          Dot notation + array index (e.g. <span className="font-mono">results.0.data.details.record_type.code</span>)
        </p>
        <button
          onClick={addCondition}
          className="text-[10px] text-indigo-400 hover:text-indigo-300 font-medium"
        >+ Add condition</button>
      </div>
    </div>
  )
}

// ── FieldMappingEditor ──────────────────────────────────────────────────────

function FieldMappingEditor({ mappings, onChange, sourceContext, capturedResponse }: {
  mappings: OutcomeFieldMapping[]
  onChange: (m: OutcomeFieldMapping[]) => void
  sourceContext: SourceContext | null
  capturedResponse: CapturedResponse | null
}) {
  const targetOptions = sourceContext
    ? sourceContext.groups.flatMap(g => g.fields.map(f => ({
        value: `${sourceContext.namespace}.${f.key}`,
        label: `${sourceContext.namespace}.${f.key} — ${f.label}`,
      })))
    : []

  function update(i: number, field: 'from' | 'to', val: string) {
    onChange(mappings.map((m, idx) => idx === i ? { ...m, [field]: val } : m))
  }

  return (
    <div className="space-y-2">
      {mappings.length > 0 && (
        <div className="flex items-center gap-2 px-0.5">
          <span className="flex-1 text-gray-500 text-[10px] font-medium uppercase tracking-wider">From (path or template)</span>
          <span className="text-gray-600 text-[10px] shrink-0 w-4" />
          <span className="flex-1 text-gray-500 text-[10px] font-medium uppercase tracking-wider">To (flow variable)</span>
          <span className="w-5 shrink-0" />
        </div>
      )}
      {mappings.map((mapping, i) => {
        const isTemplate = mapping.from.includes('{{')
        const preview = isTemplate && capturedResponse?.body != null
          ? resolveFromTemplate(capturedResponse.body, mapping.from)
          : null
        return (
          <div key={i} className="space-y-1">
            <div className="flex items-center gap-2">
              <input
                type="text"
                value={mapping.from}
                onChange={(e) => update(i, 'from', e.target.value)}
                placeholder="results[0].data.city  or  {{path1}} {{path2}}"
                className="flex-1 bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-white text-xs font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
              />
              <span className="text-gray-600 text-xs shrink-0">→</span>
              {targetOptions.length > 0 ? (
                <select
                  value={mapping.to}
                  onChange={(e) => update(i, 'to', e.target.value)}
                  className="flex-1 bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-white text-xs focus:outline-none focus:border-indigo-500"
                >
                  <option value="">Select target…</option>
                  {targetOptions.map(opt => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                  ))}
                </select>
              ) : (
                <input
                  type="text"
                  value={mapping.to}
                  onChange={(e) => update(i, 'to', e.target.value)}
                  placeholder="address.city"
                  className="flex-1 bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-white text-xs font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                />
              )}
              <button
                onClick={() => onChange(mappings.filter((_, idx) => idx !== i))}
                className="text-gray-600 hover:text-red-400 transition-colors px-0.5 text-sm shrink-0"
              >×</button>
            </div>
            {preview != null && (
              <p className="text-emerald-400 text-[10px] font-mono bg-gray-900/60 rounded px-2 py-1 truncate">
                Preview: {preview}
              </p>
            )}
          </div>
        )
      })}
      {mappings.length === 0 && (
        <p className="text-gray-600 text-xs italic">No mappings yet — add one to write response values into flow variables.</p>
      )}
      <button
        onClick={() => onChange([...mappings, { from: '', to: '' }])}
        className="text-indigo-400 hover:text-indigo-300 text-xs transition-colors"
      >
        + Add mapping
      </button>
    </div>
  )
}

// ── ResponseMappingPanel ────────────────────────────────────────────────────

interface ResponseMappingPanelProps {
  config: ResponseMappingConfig
  onChange: (c: ResponseMappingConfig) => void
  sourceContext: SourceContext | null
  subType?: string
  testRunning: boolean
  onRunAndCapture: (outcomeId: string) => void
  onRunAndCaptureFromPayload: (outcomeId: string) => void
  hasTestData: boolean
  hasPayload: boolean
  onGoToTest: () => void
  onGoToPayload: () => void
}

function ResponseMappingPanel({ config, onChange, sourceContext, subType, testRunning, onRunAndCapture, onRunAndCaptureFromPayload, hasTestData, hasPayload, onGoToTest, onGoToPayload }: ResponseMappingPanelProps) {
  const [activeId, setActiveId] = useState<string | null>(config.outcomes[0]?.id ?? null)
  const [copiedPath, setCopiedPath] = useState<string | null>(null)

  const activeOutcome = config.outcomes.find(o => o.id === activeId) ?? null

  function addOutcome() {
    const id = crypto.randomUUID()
    const next: ResponseOutcome = {
      id,
      name: '',
      label: 'New Outcome',
      conditions: [{ path: '', op: 'eq', value: '' }],
      conditionLogic: 'and',
      messagePath: '',
      fieldMappings: [],
      capturedResponse: null,
    }
    onChange({ ...config, outcomes: [...config.outcomes, next] })
    setActiveId(id)
  }

  function updateOutcome(patch: Partial<ResponseOutcome>) {
    if (!activeId) return
    onChange({ ...config, outcomes: config.outcomes.map(o => o.id === activeId ? { ...o, ...patch } : o) })
  }

  function removeOutcome(id: string) {
    const next = config.outcomes.filter(o => o.id !== id)
    onChange({ ...config, outcomes: next })
    if (activeId === id) setActiveId(next[0]?.id ?? null)
  }

  function handleCopyPath(path: string) {
    navigator.clipboard.writeText(path).then(() => {
      setCopiedPath(path)
      setTimeout(() => setCopiedPath(null), 1500)
    })
  }

  function updateAutocomplete(patch: Partial<AutocompleteConfig>) {
    const current = config.autocompleteConfig ?? defaultAutocompleteConfig()
    onChange({ ...config, autocompleteConfig: { ...current, ...patch } })
  }

  function updateAutocompleteSuggestions(patch: Partial<AutocompleteSuggestionsConfig>) {
    const current = config.autocompleteConfig ?? defaultAutocompleteConfig()
    updateAutocomplete({ suggestions: { ...current.suggestions, ...patch } })
  }

  function updateAutocompleteDetails(patch: Partial<AutocompleteDetailsConfig>) {
    const current = config.autocompleteConfig ?? defaultAutocompleteConfig()
    updateAutocomplete({ details: { ...current.details, ...patch } })
  }

  // ── Realtime Address Autocomplete — dedicated two-section config panel ──────
  if (subType === 'realtime_address_autocomplete') {
    const ac = config.autocompleteConfig ?? defaultAutocompleteConfig()
    const capturedBody = config.outcomes[0]?.capturedResponse?.body ?? null
    return (
      <div className="flex w-full min-h-0 overflow-hidden">
        {/* Config panels */}
        <div className="flex-1 overflow-y-auto px-4 py-4 space-y-5 min-w-0">

          {/* ── Suggestions ─────────────────────────────── */}
          <div>
            <p className="text-white text-xs font-semibold mb-3">Suggestions</p>
            <p className="text-gray-500 text-xs mb-3">
              Configure how to extract the suggestions list from the autocomplete API response (the call made while the agent types).
            </p>
            <div className="bg-gray-800/40 border border-gray-700/50 rounded-lg px-3 py-3 space-y-3">
              <div>
                <label className="block text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">Suggestions Array Path</label>
                <input type="text" value={ac.suggestions.arrayPath}
                  onChange={(e) => updateAutocompleteSuggestions({ arrayPath: e.target.value })}
                  placeholder="e.g. suggestions"
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                />
                <p className="text-gray-600 text-xs mt-1">Path to the array of suggestion items in the response.</p>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">Place ID Field</label>
                  <input type="text" value={ac.suggestions.placeIdPath}
                    onChange={(e) => updateAutocompleteSuggestions({ placeIdPath: e.target.value })}
                    placeholder="e.g. placePrediction.placeId"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                  <p className="text-gray-600 text-xs mt-1">Path within each item to the unique place identifier.</p>
                </div>
                <div>
                  <label className="block text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">Display Text Field</label>
                  <input type="text" value={ac.suggestions.displayTextPath}
                    onChange={(e) => updateAutocompleteSuggestions({ displayTextPath: e.target.value })}
                    placeholder="e.g. placePrediction.text.text"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                  <p className="text-gray-600 text-xs mt-1">Path within each item to the display text shown in the dropdown.</p>
                </div>
              </div>
            </div>
          </div>

          {/* ── Details ─────────────────────────────────── */}
          <div>
            <p className="text-white text-xs font-semibold mb-3">Details</p>
            <p className="text-gray-500 text-xs mb-3">
              Configure the second API call made when the agent selects a suggestion. Use <span className="font-mono text-gray-400">{"{{address.placeId}}"}</span> and <span className="font-mono text-gray-400">{"{{address.sessionToken}}"}</span> in the path and headers.
            </p>
            <div className="bg-gray-800/40 border border-gray-700/50 rounded-lg px-3 py-3 space-y-3">
              <div className="grid grid-cols-3 gap-3">
                <div className="col-span-2">
                  <label className="block text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">Details Path</label>
                  <input type="text" value={ac.details.path}
                    onChange={(e) => updateAutocompleteDetails({ path: e.target.value })}
                    placeholder="e.g. /v1/places/{{address.placeId}}"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                </div>
                <div>
                  <label className="block text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">Method</label>
                  <select value={ac.details.method}
                    onChange={(e) => updateAutocompleteDetails({ method: e.target.value })}
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm focus:outline-none focus:border-indigo-500"
                  >
                    {['GET', 'POST', 'PUT', 'PATCH'].map(m => <option key={m}>{m}</option>)}
                  </select>
                </div>
              </div>

              {/* Details headers */}
              <div>
                <p className="text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">Additional Headers</p>
                <p className="text-gray-600 text-xs mb-2">
                  Headers specific to the details call — e.g. <span className="font-mono text-gray-500">X-Goog-SessionToken: {"{{address.sessionToken}}"}</span>.
                  Auth credentials are applied automatically from the definition's auth config.
                </p>
                <div className="space-y-1.5">
                  {Object.entries(ac.details.headers).map(([k, v], i) => (
                    <div key={i} className="flex gap-2 items-center">
                      <input type="text" value={k}
                        onChange={(e) => {
                          const entries = Object.entries(ac.details.headers)
                          entries[i] = [e.target.value, v]
                          updateAutocompleteDetails({ headers: Object.fromEntries(entries) })
                        }}
                        placeholder="Header name"
                        className="flex-1 bg-gray-800 border border-gray-700 rounded px-2 py-1 text-white text-xs font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                      />
                      <input type="text" value={v}
                        onChange={(e) => {
                          const entries = Object.entries(ac.details.headers)
                          entries[i] = [k, e.target.value]
                          updateAutocompleteDetails({ headers: Object.fromEntries(entries) })
                        }}
                        placeholder="Value or {{address.sessionToken}}"
                        className="flex-1 bg-gray-800 border border-gray-700 rounded px-2 py-1 text-white text-xs font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                      />
                      <button onClick={() => {
                        const entries = Object.entries(ac.details.headers).filter((_, j) => j !== i)
                        updateAutocompleteDetails({ headers: Object.fromEntries(entries) })
                      }} className="text-gray-600 hover:text-red-400 text-sm px-1">×</button>
                    </div>
                  ))}
                  <button onClick={() => updateAutocompleteDetails({ headers: { ...ac.details.headers, '': '' } })}
                    className="text-indigo-400 hover:text-indigo-300 text-xs mt-1">
                    + Add header
                  </button>
                </div>
              </div>

              {/* Field mappings */}
              <div>
                <p className="text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">Field Mappings</p>
                <FieldMappingEditor
                  mappings={ac.details.fieldMappings}
                  onChange={(fieldMappings) => updateAutocompleteDetails({ fieldMappings })}
                  sourceContext={sourceContext}
                  capturedResponse={config.outcomes[0]?.capturedResponse ?? null}
                />
                <p className="text-gray-600 text-[10px] mt-1.5">
                  Map details response fields to address form fields. For Google Places use <span className="font-mono text-gray-500">addressComponents[type=locality].longText</span> to extract by component type,
                  or <span className="font-mono text-gray-500">formattedAddress</span> for the full address string.
                </p>
              </div>
            </div>
          </div>

          {/* Test capture — reuses first outcome slot to store captured response */}
          <div>
            <p className="text-white text-xs font-semibold mb-2">Captured Response</p>
            <p className="text-gray-500 text-xs mb-3">
              Run a test against the suggestions endpoint to capture a sample response for path reference.
            </p>
            <div className="flex gap-2">
              <button onClick={() => {
                let outcomeId = config.outcomes[0]?.id
                if (!outcomeId) {
                  outcomeId = crypto.randomUUID()
                  onChange({ ...config, outcomes: [{ id: outcomeId, name: 'capture', label: 'Capture', conditions: [], fieldMappings: [], capturedResponse: null }] })
                }
                onRunAndCapture(outcomeId)
              }}
                disabled={testRunning || !hasTestData}
                className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded-lg px-3 py-1.5 text-xs font-medium transition-colors flex items-center gap-1.5"
              >
                {testRunning ? <span className="inline-block w-3 h-3 border border-white border-t-transparent rounded-full animate-spin" /> : '▶'}
                {hasTestData ? 'Run & Capture' : 'Add test data first'}
              </button>
              <button onClick={() => {
                let outcomeId = config.outcomes[0]?.id
                if (!outcomeId) {
                  outcomeId = crypto.randomUUID()
                  onChange({ ...config, outcomes: [{ id: outcomeId, name: 'capture', label: 'Capture', conditions: [], fieldMappings: [], capturedResponse: null }] })
                }
                onRunAndCaptureFromPayload(outcomeId)
              }}
                disabled={testRunning || !hasPayload}
                className="bg-gray-700 hover:bg-gray-600 disabled:opacity-40 text-white rounded-lg px-3 py-1.5 text-xs font-medium transition-colors"
              >
                {hasPayload ? '↑ Payload' : 'No payload'}
              </button>
            </div>
          </div>
        </div>

        {/* Captured response tree */}
        {capturedBody != null && (
          <div className="w-72 shrink-0 border-l border-gray-800 flex flex-col min-h-0">
            <div className="px-3 py-2.5 border-b border-gray-800 shrink-0 flex items-center justify-between">
              <div>
                <p className="text-white text-xs font-semibold">Captured Response</p>
                {config.outcomes[0]?.capturedResponse && (
                  <p className="text-gray-500 text-[10px] mt-0.5">
                    {new Date(config.outcomes[0].capturedResponse.capturedAt).toLocaleDateString()} · {config.outcomes[0].capturedResponse.statusCode}
                  </p>
                )}
              </div>
            </div>
            <div className="flex-1 overflow-y-auto px-2 py-2">
              <JsonTree name="" value={capturedBody} path="" depth={0} copiedPath={copiedPath} onCopy={handleCopyPath} />
            </div>
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="flex w-full min-h-0 overflow-hidden">
      {/* Outcomes sidebar */}
      <div className="w-44 shrink-0 border-r border-gray-800 flex flex-col min-h-0">
        <div className="px-3 py-3 border-b border-gray-800 shrink-0">
          <p className="text-white text-xs font-semibold">Outcomes</p>
          <p className="text-gray-500 text-[10px] mt-0.5">Evaluated top-to-bottom; first match wins</p>
        </div>
        <div className="flex-1 overflow-y-auto py-2 px-2 space-y-1">
          {config.outcomes.length === 0 && (
            <p className="text-gray-600 text-xs px-1 py-2">No outcomes defined yet.</p>
          )}
          {config.outcomes.map((outcome) => (
            <button
              key={outcome.id}
              onClick={() => setActiveId(outcome.id)}
              className={`w-full text-left px-3 py-2 rounded-lg transition-colors ${
                activeId === outcome.id
                  ? 'bg-indigo-600 text-white'
                  : 'text-gray-300 hover:bg-gray-800/60'
              }`}
            >
              <div className="text-xs font-medium truncate">
                {outcome.label || <span className="italic text-gray-500">Unnamed</span>}
              </div>
              {outcome.name && (
                <div className={`text-[10px] font-mono mt-0.5 truncate ${activeId === outcome.id ? 'text-indigo-200' : 'text-gray-600'}`}>
                  {outcome.name}
                </div>
              )}
              {outcome.capturedResponse && (
                <div className={`text-[10px] mt-0.5 ${activeId === outcome.id ? 'text-indigo-200' : 'text-emerald-500'}`}>
                  ● captured
                </div>
              )}
            </button>
          ))}
        </div>
        <div className="px-2 py-2 border-t border-gray-800 shrink-0">
          <button
            onClick={addOutcome}
            className="w-full text-xs text-indigo-400 hover:text-indigo-300 py-1.5 transition-colors text-center"
          >
            + Add outcome
          </button>
        </div>
      </div>

      {/* Outcome editor */}
      {activeOutcome ? (
        <div className="flex-1 overflow-y-auto px-5 py-4 space-y-5 min-h-0">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-gray-400 text-xs font-medium mb-1.5">Label</label>
              <input
                type="text"
                value={activeOutcome.label}
                onChange={(e) => updateOutcome({ label: e.target.value })}
                placeholder="Exact Match"
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
              />
            </div>
            <div>
              <label className="block text-gray-400 text-xs font-medium mb-1.5">
                Key <span className="text-gray-600 font-normal">(used in flow branching)</span>
              </label>
              <input
                type="text"
                list="outcome-key-suggestions"
                value={activeOutcome.name}
                onChange={(e) => updateOutcome({ name: e.target.value.toLowerCase().replace(/\s+/g, '_').replace(/[^a-z0-9_]/g, '') })}
                placeholder="exact_match"
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
              />
              {sourceContext && (
                <datalist id="outcome-key-suggestions">
                  {(sourceContext.namespace === 'address'
                    ? ['exact_match', 'multiple_matches', 'no_match', 'corrected', 'error']
                    : ['success', 'no_match', 'error']
                  ).map(k => <option key={k} value={k} />)}
                </datalist>
              )}
            </div>
          </div>

          <div>
            <p className="text-gray-400 text-xs font-medium mb-2">Condition</p>
            <div className="bg-gray-800/40 border border-gray-700/50 rounded-lg px-3 py-3">
              <ConditionListBuilder
                conditions={activeOutcome.conditions}
                conditionLogic={activeOutcome.conditionLogic ?? 'and'}
                onChange={(conditions, conditionLogic) => updateOutcome({ conditions, conditionLogic })}
              />
            </div>
          </div>

          <div>
            <p className="text-gray-400 text-xs font-medium mb-1.5">Message to Display</p>
            <div className="bg-gray-800/40 border border-gray-700/50 rounded-lg px-3 py-3 space-y-2">
              <input
                type="text"
                value={activeOutcome.messagePath ?? ''}
                onChange={(e) => updateOutcome({ messagePath: e.target.value })}
                placeholder="e.g. error.message or results.0.notices[*].message"
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
              />
              <p className="text-gray-600 text-xs">
                Response path whose value is shown when this outcome occurs. Use <span className="font-mono text-gray-500">[*]</span> to collect and join all values from an array — e.g. <span className="font-mono text-gray-500">results.0.notices[*].message</span> joins every notice message. Click a leaf in the captured response tree to copy its path.
              </p>
              {activeOutcome.messagePath && activeOutcome.capturedResponse?.body != null && (() => {
                const preview = resolveMessagePreview(activeOutcome.capturedResponse.body, activeOutcome.messagePath)
                if (preview == null) return null
                return (
                  <p className="text-emerald-400 text-xs font-mono bg-gray-900/60 rounded px-2 py-1 whitespace-pre-line">
                    Preview: {preview}
                  </p>
                )
              })()}
            </div>
          </div>

          {/* ZIP Code Lookup — dedicated field path inputs */}
          {subType === 'zip_code_lookup' ? (
            <div>
              <p className="text-gray-400 text-xs font-medium mb-2">Field Paths</p>
              <div className="bg-gray-800/40 border border-gray-700/50 rounded-lg px-3 py-3 space-y-3">
                <p className="text-gray-500 text-xs">
                  Click any leaf in the captured response tree to copy its path, then paste it here.
                </p>
                <div>
                  <label className="block text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">Main City</label>
                  <input
                    type="text"
                    value={activeOutcome.mainCityPath ?? ''}
                    onChange={(e) => updateOutcome({ mainCityPath: e.target.value })}
                    placeholder="e.g. results[0].city"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                  <p className="text-gray-600 text-xs mt-1">The primary/default city name returned by the API for this ZIP code.</p>
                </div>
                <div>
                  <label className="block text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">City Aliases</label>
                  <input
                    type="text"
                    value={activeOutcome.cityAliasesPath ?? ''}
                    onChange={(e) => updateOutcome({ cityAliasesPath: e.target.value })}
                    placeholder="e.g. results[0].city_aliases[*].city"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                  <p className="text-gray-600 text-xs mt-1">
                    Path to the array of alternate city names. Use <span className="font-mono text-gray-500">[*]</span> to collect all values — e.g. <span className="font-mono text-gray-500">results[0].city_aliases[*].city</span>.
                    Main City is automatically included if not already in this list.
                  </p>
                </div>
                <div>
                  <label className="block text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">State</label>
                  <input
                    type="text"
                    value={activeOutcome.statePath ?? ''}
                    onChange={(e) => updateOutcome({ statePath: e.target.value })}
                    placeholder="e.g. results[0].state"
                    className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="block text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">Latitude</label>
                    <input
                      type="text"
                      value={activeOutcome.latitudePath ?? ''}
                      onChange={(e) => updateOutcome({ latitudePath: e.target.value })}
                      placeholder="e.g. results[0].location.lat"
                      className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                    />
                  </div>
                  <div>
                    <label className="block text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">Longitude</label>
                    <input
                      type="text"
                      value={activeOutcome.longitudePath ?? ''}
                      onChange={(e) => updateOutcome({ longitudePath: e.target.value })}
                      placeholder="e.g. results[0].location.lon"
                      className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                    />
                  </div>
                </div>
              </div>
            </div>
          ) : (
            <>
              {/* Generic Field Mappings — non-ZIP sub-types */}
              <div>
                <p className="text-gray-400 text-xs font-medium mb-2">Field Mappings</p>
                <div className="bg-gray-800/40 border border-gray-700/50 rounded-lg px-3 py-3">
                  <FieldMappingEditor
                    mappings={activeOutcome.fieldMappings}
                    onChange={(fieldMappings) => updateOutcome({ fieldMappings })}
                    sourceContext={sourceContext}
                    capturedResponse={activeOutcome.capturedResponse}
                  />
                </div>
                <p className="text-gray-600 text-[10px] mt-1.5">
                  Click any leaf in the captured response tree to copy its path, then paste into a "From" field. Use <span className="font-mono text-gray-500">{"{{path}}"}</span> template syntax to combine multiple values — e.g. <span className="font-mono text-gray-500">{"{{results[0].data.address_components.number}} {{results[0].data.address_components.street}}"}</span>.
                </p>
              </div>

              {/* Multiple Matches */}
              <div>
                <label className="flex items-center gap-2 cursor-pointer select-none mb-2">
                  <input
                    type="checkbox"
                    checked={!!activeOutcome.multipleMatchesConfig}
                    onChange={(e) => updateOutcome({
                      multipleMatchesConfig: e.target.checked ? { arrayPath: '', itemMappings: [] } : undefined,
                    })}
                    className="w-3.5 h-3.5 rounded accent-indigo-500"
                  />
                  <span className="text-gray-400 text-xs font-medium">Multiple Matches — present a list for user selection</span>
                </label>
                {activeOutcome.multipleMatchesConfig && (
                  <div className="bg-gray-800/40 border border-gray-700/50 rounded-lg px-3 py-3 space-y-3">
                    <div>
                      <label className="block text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">Array Path</label>
                      <input
                        type="text"
                        value={activeOutcome.multipleMatchesConfig.arrayPath}
                        onChange={(e) => updateOutcome({ multipleMatchesConfig: { ...activeOutcome.multipleMatchesConfig!, arrayPath: e.target.value } })}
                        placeholder="e.g. matches or candidates"
                        className="w-full bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                      />
                      <p className="text-gray-600 text-xs mt-1">
                        Path to the array of address options. Click any array node <span className="font-mono">⎘</span> in the captured response tree to copy its path.
                      </p>
                    </div>
                    <div>
                      <p className="text-gray-500 text-[10px] font-medium uppercase tracking-wider mb-1">Per-Item Field Mappings</p>
                      <p className="text-gray-600 text-xs mb-2">
                        Paths are relative to each array item — e.g. use <span className="font-mono">streetAddress</span> not <span className="font-mono">matches[0].streetAddress</span>.
                      </p>
                      <FieldMappingEditor
                        mappings={activeOutcome.multipleMatchesConfig.itemMappings}
                        onChange={(itemMappings) => updateOutcome({ multipleMatchesConfig: { ...activeOutcome.multipleMatchesConfig!, itemMappings } })}
                        sourceContext={sourceContext}
                        capturedResponse={activeOutcome.capturedResponse}
                      />
                    </div>
                  </div>
                )}
              </div>
            </>
          )}

          <div className="pt-1 border-t border-gray-800">
            <button
              onClick={() => removeOutcome(activeOutcome.id)}
              className="text-red-400 hover:text-red-300 text-xs transition-colors"
            >
              Delete outcome
            </button>
          </div>
        </div>
      ) : (
        <div className="flex-1 flex flex-col items-center justify-center gap-2 text-center px-8">
          <p className="text-gray-500 text-sm">No outcomes defined yet.</p>
          <p className="text-gray-600 text-xs">
            Add an outcome for each distinct response type (exact match, multiple candidates, no match, error). Run a test to capture the real response for each, then map fields back to flow variables.
          </p>
          <button
            onClick={addOutcome}
            className="mt-2 bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-medium px-4 py-1.5 rounded-lg transition-colors"
          >
            + Add outcome
          </button>
        </div>
      )}

      {/* Captured response panel */}
      <div className="w-64 shrink-0 border-l border-gray-800 flex flex-col min-h-0">
        <div className="px-3 py-3 border-b border-gray-800 shrink-0 space-y-2">
          <div className="flex items-start justify-between gap-2">
            <div>
              <p className="text-white text-xs font-semibold">Captured Response</p>
              {activeOutcome?.capturedResponse ? (
                <p className="text-gray-500 text-[10px] mt-0.5">
                  {new Date(activeOutcome.capturedResponse.capturedAt).toLocaleDateString()} ·{' '}
                  <span className={activeOutcome.capturedResponse.statusCode < 300 ? 'text-emerald-400' : 'text-red-400'}>
                    {activeOutcome.capturedResponse.statusCode}
                  </span>
                </p>
              ) : (
                <p className="text-gray-600 text-[10px] mt-0.5">No response captured yet</p>
              )}
            </div>
            {activeOutcome && (
              <div className="flex flex-col gap-1 shrink-0">
                <button
                  onClick={() => onRunAndCapture(activeOutcome.id)}
                  disabled={testRunning || !hasTestData}
                  title={hasTestData ? 'Capture using Source Test data' : 'Set source test data on the Source Test tab first'}
                  className="text-[10px] bg-emerald-700 hover:bg-emerald-600 disabled:opacity-40 text-white px-2 py-1 rounded transition-colors whitespace-nowrap"
                >
                  {testRunning ? '…' : '⇡ Source'}
                </button>
                <button
                  onClick={() => onRunAndCaptureFromPayload(activeOutcome.id)}
                  disabled={testRunning || !hasPayload}
                  title={hasPayload ? 'Capture using pasted payload' : 'Paste a payload on the Payload Test tab first'}
                  className="text-[10px] bg-violet-700 hover:bg-violet-600 disabled:opacity-40 text-white px-2 py-1 rounded transition-colors whitespace-nowrap"
                >
                  {testRunning ? '…' : '⇡ Payload'}
                </button>
              </div>
            )}
          </div>
          {!hasTestData && !hasPayload && activeOutcome && (
            <div className="flex gap-3">
              <button onClick={onGoToTest} className="text-[10px] text-amber-400 hover:text-amber-300 transition-colors">
                ⚠ Source Test →
              </button>
              <button onClick={onGoToPayload} className="text-[10px] text-amber-400 hover:text-amber-300 transition-colors">
                ⚠ Payload Test →
              </button>
            </div>
          )}
          {activeOutcome?.capturedResponse?.resolvedUrl && (
            <p className="font-mono text-[10px] text-gray-600 truncate" title={activeOutcome.capturedResponse.resolvedUrl}>
              {activeOutcome.capturedResponse.resolvedUrl}
            </p>
          )}
        </div>
        <div className="flex-1 overflow-y-auto overflow-x-hidden px-2 py-2">
          {activeOutcome?.capturedResponse?.body != null ? (
            <JsonTree
              name="response"
              value={activeOutcome.capturedResponse.body}
              path=""
              depth={0}
              copiedPath={copiedPath}
              onCopy={handleCopyPath}
            />
          ) : (
            <p className="text-gray-600 text-xs text-center mt-6 leading-relaxed px-2">
              Run a test to capture the response for this outcome. The JSON tree will appear here for reference and path selection.
            </p>
          )}
        </div>
        <div className="px-3 py-2 border-t border-gray-800 shrink-0">
          <p className="text-gray-600 text-[10px] leading-relaxed">
            Click any leaf to copy its dot-notation path. Paste into a "From" field above.
          </p>
        </div>
      </div>
    </div>
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
  const [ttsProviders, setTtsProviders] = useState<string[]>([])

  // Definition edit modal
  const [showDefModal, setShowDefModal] = useState(false)
  const [defForm, setDefForm] = useState<DefFormState | null>(null)
  const [defSaving, setDefSaving] = useState(false)
  const [defFormError, setDefFormError] = useState<string | null>(null)
  const [togglingActive, setTogglingActive] = useState(false)
  const [showDefHistory, setShowDefHistory] = useState(false)
  const [historyEndpointId, setHistoryEndpointId] = useState<string | null>(null)

  // Endpoint modal
  const [endpointModal, setEndpointModal] = useState<'create' | 'edit' | null>(null)
  const [editingEndpointId, setEditingEndpointId] = useState<string | null>(null)
  const [endpointForm, setEndpointForm] = useState<EndpointForm>(BLANK_ENDPOINT_FORM)
  const [endpointTab, setEndpointTab] = useState<'general' | 'params' | 'headers' | 'body' | 'test' | 'payload' | 'response'>('general')
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
        sensitiveResponseFields: pathsToArray(endpointForm.sensitiveResponseFields),
      })
      setTestResult(result)
    } catch (e: unknown) {
      setTestResult({ success: false, statusCode: null, body: null, responseHeaders: null, resolvedUrl: null, error: (e as Error).message })
    } finally {
      setTestRunning(false)
    }
  }

  async function runAndCaptureForOutcome(outcomeId: string) {
    if (!endpointSourceContext) return
    setTestRunning(true)
    try {
      const result = await api.testEndpoint(definitionId, {
        path: endpointForm.path,
        httpMethod: endpointForm.httpMethod || undefined,
        queryParams: kvToJson(endpointForm.params),
        headers: kvToJson(endpointForm.headers),
        requestBodyTemplate: endpointForm.requestBodyTemplate || undefined,
        namespace: endpointSourceContext.namespace,
        testData: endpointForm.testData,
        sensitiveResponseFields: pathsToArray(endpointForm.sensitiveResponseFields),
      })
      let parsedBody: unknown = result.body
      try { if (result.body) parsedBody = JSON.parse(result.body) } catch { /* keep raw */ }
      const captured: CapturedResponse = {
        capturedAt: new Date().toISOString(),
        statusCode: result.statusCode ?? 0,
        resolvedUrl: result.resolvedUrl,
        body: parsedBody,
      }
      setEndpointForm(f => ({
        ...f,
        responseMapping: {
          ...f.responseMapping,
          outcomes: f.responseMapping.outcomes.map(o =>
            o.id === outcomeId ? { ...o, capturedResponse: captured } : o
          ),
        },
      }))
    } catch { /* ignore — user will see no capture */ } finally {
      setTestRunning(false)
    }
  }

  async function runPayloadTest() {
    if (!endpointSourceContext) return
    setTestRunning(true)
    setTestResult(null)
    try {
      const result = await api.testEndpoint(definitionId, {
        path: endpointForm.path,
        httpMethod: endpointForm.httpMethod || undefined,
        queryParams: kvToJson(endpointForm.params),
        headers: kvToJson(endpointForm.headers),
        requestBodyTemplate: endpointForm.payloadBody || undefined,
        namespace: endpointSourceContext.namespace,
        testData: {},
        sensitiveResponseFields: pathsToArray(endpointForm.sensitiveResponseFields),
      })
      setTestResult(result)
    } catch (e: unknown) {
      setTestResult({ success: false, statusCode: null, body: null, responseHeaders: null, resolvedUrl: null, error: (e as Error).message })
    } finally {
      setTestRunning(false)
    }
  }

  // General API endpoints aren't scoped to a fixed namespace, so their {{...}} tags are
  // resolved client-side against per-tag test values (gathered by extractVarTags) before the
  // request is sent — the server-side namespace+testData substitution used by the other
  // categories doesn't apply here.
  async function runGeneralEndpointTest() {
    setTestRunning(true)
    setTestResult(null)
    try {
      const values = endpointForm.testData
      const resolvedParams = endpointForm.params.map((r) => ({ ...r, value: substituteVarTags(r.value, values) }))
      const resolvedHeaders = endpointForm.headers.map((r) => ({ ...r, value: substituteVarTags(r.value, values) }))
      const result = await api.testEndpoint(definitionId, {
        path: substituteVarTags(endpointForm.path, values),
        httpMethod: endpointForm.httpMethod || undefined,
        queryParams: kvToJson(resolvedParams),
        headers: kvToJson(resolvedHeaders),
        requestBodyTemplate: substituteVarTags(endpointForm.requestBodyTemplate, values) || undefined,
        namespace: 'general',
        testData: {},
        sensitiveResponseFields: pathsToArray(endpointForm.sensitiveResponseFields),
      })
      setTestResult(result)
    } catch (e: unknown) {
      setTestResult({ success: false, statusCode: null, body: null, responseHeaders: null, resolvedUrl: null, error: (e as Error).message })
    } finally {
      setTestRunning(false)
    }
  }

  async function runAndCaptureFromPayload(outcomeId: string) {
    if (!endpointSourceContext) return
    setTestRunning(true)
    try {
      const result = await api.testEndpoint(definitionId, {
        path: endpointForm.path,
        httpMethod: endpointForm.httpMethod || undefined,
        queryParams: kvToJson(endpointForm.params),
        headers: kvToJson(endpointForm.headers),
        requestBodyTemplate: endpointForm.payloadBody || undefined,
        namespace: endpointSourceContext.namespace,
        testData: {},
        sensitiveResponseFields: pathsToArray(endpointForm.sensitiveResponseFields),
      })
      let parsedBody: unknown = result.body
      try { if (result.body) parsedBody = JSON.parse(result.body) } catch { /* keep raw */ }
      const captured: CapturedResponse = {
        capturedAt: new Date().toISOString(),
        statusCode: result.statusCode ?? 0,
        resolvedUrl: result.resolvedUrl,
        body: parsedBody,
      }
      setEndpointForm(f => ({
        ...f,
        responseMapping: {
          ...f.responseMapping,
          outcomes: f.responseMapping.outcomes.map(o =>
            o.id === outcomeId ? { ...o, capturedResponse: captured } : o
          ),
        },
      }))
    } catch { /* ignore */ } finally {
      setTestRunning(false)
    }
  }

  useEffect(() => {
    loadDefinition()
    loadEndpoints()
    api.listCredentials()
      .then(setKnownCreds)
      .catch(() => {})
    api.listTtsProviders?.()
      .then(setTtsProviders)
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
    const validCategories = API_CATEGORIES.map(c => c.value)
    setDefForm({
      apiCategory: validCategories.includes(def.apiCategory) ? def.apiCategory : '',
      name: def.name,
      httpMethod: def.httpMethod,
      baseUrl: def.baseUrl,
      description: def.description ?? '',
      provider: def.provider ?? '',
      timeoutSeconds: String(def.timeoutSeconds),
      rateLimitPerMinute: def.rateLimitPerMinute ? String(def.rateLimitPerMinute) : '',
      auth: authStateFromConfig(def.authConfig),
    })
    setDefFormError(null)
    setShowDefModal(true)
  }

  async function handleDefSave() {
    if (!defForm) return
    if (!defForm.apiCategory) {
      setDefFormError('API Category is required.')
      return
    }
    if (!defForm.name.trim() || !defForm.baseUrl.trim()) {
      setDefFormError('Name and base URL are required.')
      return
    }
    setDefSaving(true)
    setDefFormError(null)
    try {
      const updated = await api.updateDefinition(definitionId, {
        apiCategory: defForm.apiCategory,
        name: defForm.name.trim(),
        httpMethod: defForm.httpMethod,
        baseUrl: defForm.baseUrl.trim(),
        description: defForm.description.trim() || undefined,
        provider: defForm.provider.trim() || undefined,
        timeoutSeconds: parseInt(defForm.timeoutSeconds) || 30,
        authConfig: serializeAuthConfig(defForm.auth),
        // Always sent explicitly (never omitted) since this is a full edit form reflecting the
        // definition's current state — an empty field means "clear to unlimited" (0), not "leave
        // whatever's on the server alone".
        rateLimitPerMinute: defForm.rateLimitPerMinute.trim() ? (parseInt(defForm.rateLimitPerMinute) || 0) : 0,
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
    setEndpointForm({ ...BLANK_ENDPOINT_FORM, params: [{ key: '', value: '' }], headers: [{ key: '', value: '' }], payloadBody: '' })
    setEditingEndpointId(null)
    setEndpointFormError(null)
    setEndpointTab('general')
    setEndpointModal('create')
  }

  function openEditEndpoint(ep: ApiEndpointRecord) {
    setEndpointForm({
      apiSubType: ep.apiSubType,
      name: ep.name,
      path: ep.path,
      httpMethod: ep.httpMethod ?? '',
      description: ep.description ?? '',
      params: jsonToKv(ep.queryParams),
      headers: jsonToKv(ep.headers),
      requestBodyTemplate: ep.requestBodyTemplate ?? '',
      testData: {},
      payloadBody: '',
      responseMapping: parseResponseMapping(ep.responseMapping),
      isRetrySafe: ep.isRetrySafe,
      sensitiveResponseFields: jsonToPaths(ep.sensitiveResponseFields),
    })
    setEditingEndpointId(ep.id)
    setEndpointFormError(null)
    setEndpointTab('general')
    setEndpointModal('edit')
  }

  async function handleEndpointSave() {
    const subTypeRequired = def?.apiCategory !== 'general'
    if ((subTypeRequired && !endpointForm.apiSubType.trim()) || !endpointForm.name.trim() || !endpointForm.path.trim()) {
      setEndpointFormError(subTypeRequired ? 'Sub-type, name, and path are required.' : 'Name and path are required.')
      return
    }
    setEndpointSaving(true)
    setEndpointFormError(null)
    try {
      const data: EndpointFormData = {
        apiSubType: subTypeRequired ? endpointForm.apiSubType : '',
        name: endpointForm.name.trim(),
        path: endpointForm.path.trim(),
        httpMethod: endpointForm.httpMethod.trim() || undefined,
        description: endpointForm.description.trim() || undefined,
        requestBodyTemplate: endpointForm.requestBodyTemplate.trim() || undefined,
        queryParams: kvToJson(endpointForm.params),
        headers: kvToJson(endpointForm.headers),
        responseMapping: JSON.stringify(endpointForm.responseMapping),
        isRetrySafe: endpointForm.isRetrySafe,
        sensitiveResponseFields: pathsToJson(endpointForm.sensitiveResponseFields),
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

  async function handleSetPreferred(id: string) {
    try {
      const updated = await api.setPreferred(definitionId, id)
      // Clear preferred on all same-subtype endpoints, then mark the updated one
      setEndpoints((prev) => prev.map((ep) =>
        ep.apiSubType === updated.apiSubType ? { ...ep, isPreferred: ep.id === id } : ep
      ))
    } catch { /* ignore */ }
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

  const authBadgeInfo = authTypeBadge(def.authConfig)
  const endpointSourceContext = API_TYPE_SOURCE_CONTEXT[endpointForm.apiSubType]

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
              <span className={`text-xs px-2 py-0.5 rounded font-medium ${API_CATEGORY_BADGE_COLORS[def.apiCategory] ?? 'bg-gray-800 text-gray-300'}`}>
                {API_CATEGORY_LABELS[def.apiCategory] ?? def.apiCategory}
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
              {def.rateLimitPerMinute && (
                <>
                  <span className="text-gray-600">·</span>
                  <span className="text-gray-500 text-xs">{def.rateLimitPerMinute}/min limit</span>
                </>
              )}
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
              onClick={() => setShowDefHistory(true)}
              className="bg-gray-800 hover:bg-gray-700 border border-gray-700 text-gray-300 text-sm font-medium px-3 py-1.5 rounded-lg transition-colors"
            >
              History
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
                  <th className="px-4 py-3 font-medium">Sub-Type</th>
                  <th className="px-4 py-3 font-medium">Method</th>
                  <th className="px-4 py-3 font-medium">Path</th>
                  <th className="px-4 py-3 font-medium"></th>
                </tr>
              </thead>
              <tbody>
                {endpoints.map((ep) => (
                  <tr key={ep.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30 transition-colors">
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-2">
                        <span className="text-white font-medium">{ep.name}</span>
                        {ep.isPreferred && def.apiCategory !== 'general' && (
                          <span className="text-xs px-1.5 py-0.5 rounded bg-emerald-900/50 text-emerald-400 font-medium">Preferred</span>
                        )}
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      {ep.apiSubType ? (
                        <span className={`text-xs px-2 py-0.5 rounded font-medium ${API_SUB_TYPE_BADGE_COLORS[ep.apiSubType] ?? 'bg-gray-800 text-gray-400'}`}>
                          {API_SUB_TYPE_LABELS[ep.apiSubType] ?? ep.apiSubType}
                        </span>
                      ) : (
                        <span className="text-gray-600 text-xs">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span className="bg-gray-800 text-gray-300 text-xs px-2 py-0.5 rounded font-mono">
                        {ep.httpMethod ?? 'inherit'}
                      </span>
                    </td>
                    <td className="px-4 py-3 font-mono text-gray-300 text-xs">{ep.path}</td>
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-3">
                        {!ep.isPreferred && def.apiCategory !== 'general' && (
                          <button
                            onClick={() => handleSetPreferred(ep.id)}
                            className="text-emerald-500 hover:text-emerald-400 text-xs font-medium transition-colors"
                          >
                            Set Preferred
                          </button>
                        )}
                        <button
                          onClick={() => setHistoryEndpointId(ep.id)}
                          className="text-gray-400 hover:text-gray-300 text-xs font-medium transition-colors"
                        >
                          History
                        </button>
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
              <div>
                <label className="block text-gray-400 text-xs font-medium mb-1.5">API Category</label>
                <select
                  value={defForm.apiCategory}
                  onChange={(e) => setDefForm((f) => f ? { ...f, apiCategory: e.target.value } : f)}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
                >
                  <option value="">— Select a category —</option>
                  {API_CATEGORIES.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
                </select>
              </div>
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
                  {defForm.apiCategory === 'media' && ttsProviders.length > 0 ? (
                    <>
                      <select
                        value={defForm.provider}
                        onChange={(e) => setDefForm((f) => f ? { ...f, provider: e.target.value } : f)}
                        className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
                      >
                        <option value="">— Select a provider —</option>
                        {ttsProviders.map((p) => (
                          <option key={p} value={p}>{TTS_PROVIDER_LABELS[p] ?? p}</option>
                        ))}
                        {/* Preserves an existing value that predates the provider registry or
                            belongs to a non-TTS Media use (e.g. a TFN vendor) — never silently
                            drop or overwrite data the dropdown doesn't know about. */}
                        {defForm.provider && !ttsProviders.includes(defForm.provider) && (
                          <option value={defForm.provider}>{defForm.provider} (current value)</option>
                        )}
                      </select>
                      <p className="text-gray-500 text-xs mt-1">
                        Determines which streaming TTS backend calls routed through this integration use.
                      </p>
                    </>
                  ) : (
                    <input
                      type="text"
                      value={defForm.provider}
                      onChange={(e) => setDefForm((f) => f ? { ...f, provider: e.target.value } : f)}
                      placeholder="USPS, Google Places…"
                      className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                    />
                  )}
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
                <label className="block text-gray-400 text-xs font-medium mb-1.5">Rate limit (requests/min)</label>
                <input
                  type="number"
                  min="1"
                  placeholder="Unlimited"
                  value={defForm.rateLimitPerMinute}
                  onChange={(e) => setDefForm((f) => f ? { ...f, rateLimitPerMinute: e.target.value } : f)}
                  className="w-40 bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                />
                <p className="text-gray-500 text-xs mt-1">
                  Leave blank for unlimited. If this definition is used by more than one tenant (a
                  Portal default), this budget applies to their combined traffic, not each one separately.
                </p>
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
            <div className={`bg-gray-900 border border-gray-700 rounded-2xl w-full shadow-2xl flex flex-col max-h-[90vh] ${endpointSourceContext ? (endpointTab === 'response' ? 'max-w-5xl' : 'max-w-4xl') : def.apiCategory === 'general' ? 'max-w-3xl' : 'max-w-2xl'}`}>
              <div className="px-6 pt-6 pb-4 border-b border-gray-800 shrink-0">
                <h2 className="text-white text-lg font-semibold">
                  {endpointModal === 'edit' ? 'Edit Endpoint' : 'Add Endpoint'}
                </h2>
              </div>
              {/* Tab bar — always visible so the user can navigate back from any tab */}
              <div className="px-6 border-b border-gray-700 shrink-0 flex">
                {(endpointSourceContext
                  ? ['general', 'params', 'headers', 'body', 'test', 'payload', 'response'] as const
                  : def.apiCategory === 'general'
                    ? ['general', 'params', 'headers', 'body', 'test'] as const
                    : ['general', 'params', 'headers', 'body'] as const
                ).map((tab) => {
                  const counts: Record<string, number> = {
                    general: 0,
                    params: endpointForm.params.filter(r => r.key.trim()).length,
                    headers: endpointForm.headers.filter(r => r.key.trim()).length,
                    body: endpointForm.requestBodyTemplate.trim() ? 1 : 0,
                    test: 0,
                    payload: endpointForm.payloadBody.trim() ? 1 : 0,
                    response: endpointForm.apiSubType === 'realtime_address_autocomplete'
                      ? (endpointForm.responseMapping.autocompleteConfig ? 1 : 0)
                      : endpointForm.responseMapping.outcomes.length,
                  }
                  const labels: Record<string, string> = { general: 'General', params: 'Query Params', headers: 'Headers', body: 'Body', test: def.apiCategory === 'general' ? 'Test' : 'Source Test', payload: 'Payload Test', response: 'Response Mapping' }
                  const count = counts[tab]
                  const activeColor = tab === 'test' ? 'border-emerald-500 text-emerald-400'
                    : tab === 'response' ? 'border-violet-500 text-violet-400'
                    : 'border-indigo-500 text-white'
                  return (
                    <button
                      key={tab}
                      onClick={() => { setEndpointTab(tab); setTestResult(null) }}
                      className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors flex items-center gap-1.5 -mb-px ${
                        endpointTab === tab ? activeColor : 'border-transparent text-gray-500 hover:text-gray-300'
                      }`}
                    >
                      {labels[tab]}
                      {count > 0 && (
                        <span className={`text-white text-xs rounded-full w-4 h-4 flex items-center justify-center leading-none ${tab === 'response' ? 'bg-violet-600' : 'bg-indigo-600'}`}>
                          {count}
                        </span>
                      )}
                    </button>
                  )
                })}
              </div>
              <div className="flex flex-1 min-h-0 overflow-hidden">
              {endpointTab === 'response' ? (
                <ResponseMappingPanel
                  config={endpointForm.responseMapping}
                  onChange={(config) => setEndpointForm(f => ({ ...f, responseMapping: config }))}
                  sourceContext={endpointSourceContext ?? null}
                  subType={endpointForm.apiSubType}
                  testRunning={testRunning}
                  onRunAndCapture={runAndCaptureForOutcome}
                  onRunAndCaptureFromPayload={runAndCaptureFromPayload}
                  hasTestData={Object.values(endpointForm.testData).some(v => v.trim())}
                  hasPayload={endpointForm.payloadBody.trim().length > 0}
                  onGoToTest={() => setEndpointTab('test')}
                  onGoToPayload={() => setEndpointTab('payload')}
                />
              ) : (<>
              <div className="overflow-y-auto flex-1 px-6 py-4 space-y-4">

                {/* General tab */}
                {endpointTab === 'general' && (
                  <div className="space-y-4">
                    {def.apiCategory !== 'general' && (
                      <div>
                        <label className="block text-gray-400 text-xs font-medium mb-1.5">API Sub-Type *</label>
                        <select
                          value={endpointForm.apiSubType}
                          onChange={(e) => setEndpointForm((f) => ({ ...f, apiSubType: e.target.value }))}
                          className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
                        >
                          <option value="">Select a sub-type…</option>
                          {API_SUB_TYPES.filter(s => s.category === def.apiCategory).map((s) => (
                            <option key={s.value} value={s.value}>{s.label}</option>
                          ))}
                        </select>
                        {!API_CATEGORIES.some(c => c.value === def.apiCategory) && (
                          <p className="text-amber-400 text-xs mt-1">
                            The definition has an invalid API Category — edit the definition and set a valid category first.
                          </p>
                        )}
                      </div>
                    )}
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
                    {(() => {
                      const effectiveMethod = (endpointForm.httpMethod || def?.httpMethod || 'GET').toUpperCase()
                      if (effectiveMethod !== 'POST' && effectiveMethod !== 'PATCH') return null
                      return (
                        <div className="flex items-start gap-2.5 bg-amber-950/20 border border-amber-900/40 rounded-lg px-3 py-2.5">
                          <input
                            type="checkbox"
                            id="endpoint-retry-safe"
                            checked={endpointForm.isRetrySafe}
                            onChange={(e) => setEndpointForm((f) => ({ ...f, isRetrySafe: e.target.checked }))}
                            className="mt-0.5"
                          />
                          <label htmlFor="endpoint-retry-safe" className="cursor-pointer">
                            <span className="text-gray-300 text-xs font-medium block">Safe to automatically retry on timeout/error</span>
                            <span className="text-gray-500 text-xs mt-0.5 block">
                              {effectiveMethod} calls aren't retried automatically by default — a timeout or 5xx
                              doesn't tell us whether the vendor already processed the request, so retrying risks a
                              duplicate. Only enable this if the vendor guarantees idempotency for this endpoint
                              (e.g. their own idempotency-key support).
                            </span>
                          </label>
                        </div>
                      )
                    })()}
                    <div>
                      <label className="block text-gray-400 text-xs font-medium mb-1.5">Sensitive Response Fields</label>
                      <textarea
                        value={endpointForm.sensitiveResponseFields}
                        onChange={(e) => setEndpointForm((f) => ({ ...f, sensitiveResponseFields: e.target.value }))}
                        placeholder={'ssn\ncustomer.dob\npayment.cardNumber'}
                        rows={3}
                        spellCheck={false}
                        className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 font-mono focus:outline-none focus:border-indigo-500 resize-none"
                      />
                      <p className="text-gray-600 text-xs mt-1">
                        One dot-separated field path per line. Matching values are replaced with{' '}
                        <span className="text-gray-400 font-mono">[REDACTED]</span> before the response is shown in
                        the Test panel below and before a live flow ever stores it. Optional — blank means no masking.
                      </p>
                    </div>
                  </div>
                )}

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

                  {/* General API Test tab — prompts for a value per {{...}} tag found anywhere
                      in the path/query params/headers/body, since general endpoints aren't tied
                      to one fixed namespace like address/order/fulfillment/media sub-types are. */}
                  {endpointTab === 'test' && def.apiCategory === 'general' && (() => {
                    const tags = extractVarTags([
                      endpointForm.path,
                      ...endpointForm.params.map((r) => r.value),
                      ...endpointForm.headers.map((r) => r.value),
                      endpointForm.requestBodyTemplate,
                    ])
                    return (
                      <div className="space-y-3">
                        {tags.length === 0 ? (
                          <p className="text-gray-500 text-xs">
                            No {'{{...}}'} variables found in the path, query params, headers, or body yet.
                            You can still run the test as-is.
                          </p>
                        ) : (
                          <div className="space-y-2">
                            <p className="text-gray-500 text-xs">
                              Enter test values for the dynamic variables used by this endpoint. These are only used for this test run — they are not saved.
                            </p>
                            {tags.map((tag) => (
                              <div key={tag}>
                                <label className="block text-gray-400 text-xs font-medium mb-1 font-mono">{'{{' + tag + '}}'}</label>
                                <input
                                  type="text"
                                  value={endpointForm.testData[tag] ?? ''}
                                  onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, [tag]: e.target.value } }))}
                                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 font-mono focus:outline-none focus:border-indigo-500"
                                />
                              </div>
                            ))}
                          </div>
                        )}

                        {/* Run Test */}
                        <div className="pt-3 border-t border-gray-800 flex items-center gap-3">
                          <button
                            onClick={runGeneralEndpointTest}
                            disabled={testRunning || !endpointForm.path.trim()}
                            className="bg-emerald-700 hover:bg-emerald-600 disabled:opacity-50 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors"
                          >
                            {testRunning ? 'Running…' : 'Run Test'}
                          </button>
                          {!endpointForm.path.trim() && (
                            <span className="text-gray-500 text-xs">Enter a path on the General tab first.</span>
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
                    )
                  })()}

                  {/* Source Test tab — autocomplete variant */}
                  {endpointTab === 'test' && endpointForm.apiSubType === 'realtime_address_autocomplete' && (
                    <div className="space-y-3">
                      <div>
                        <label className="block text-gray-400 text-xs font-medium mb-1">Search Text <span className="text-gray-600 font-normal">(address.text)</span></label>
                        <input type="text" value={endpointForm.testData.text ?? ''} onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, text: e.target.value } }))} placeholder="435 Castillo St" className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500" />
                        <p className="text-gray-600 text-xs mt-1">Partial address the agent is typing. Used as {"{{address.text}}"} in the body template.</p>
                      </div>
                      <div>
                        <label className="block text-gray-400 text-xs font-medium mb-1">Session Token <span className="text-gray-600 font-normal">(address.sessionToken)</span></label>
                        <input type="text" value={endpointForm.testData.sessionToken ?? ''} onChange={(e) => setEndpointForm((f) => ({ ...f, testData: { ...f.testData, sessionToken: e.target.value } }))} placeholder="test-session-123" className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500" />
                        <p className="text-gray-600 text-xs mt-1">Any string for testing — the live flow generates a UUID per session.</p>
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
                      </div>
                      {testResult && (
                        <div className="space-y-2">
                          {testResult.resolvedUrl && <p className="text-gray-500 text-xs font-mono">→ {testResult.resolvedUrl}</p>}
                          <p className={`text-xs font-medium ${testResult.success ? 'text-emerald-400' : 'text-red-400'}`}>
                            {testResult.statusCode} {testResult.success ? 'OK' : 'Failed'}
                          </p>
                          {testResult.body && (
                            <pre className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-xs text-gray-300 font-mono overflow-x-auto whitespace-pre-wrap max-h-64">{testResult.body}</pre>
                          )}
                          {testResult.error && <p className="text-red-400 text-xs">{testResult.error}</p>}
                        </div>
                      )}
                    </div>
                  )}

                  {/* Source Test tab — standard address */}
                  {endpointTab === 'test' && endpointSourceContext?.namespace === 'address' && endpointForm.apiSubType !== 'realtime_address_autocomplete' && (
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

                  {/* Payload Test tab */}
                  {endpointTab === 'payload' && (
                    <div className="space-y-3">
                      <p className="text-gray-500 text-xs leading-relaxed">
                        Paste a raw request body (e.g. a vendor-supplied test payload) to send directly to the endpoint.
                        Auth, path, query params, and headers are still applied from the endpoint config.
                      </p>
                      <textarea
                        value={endpointForm.payloadBody}
                        onChange={(e) => setEndpointForm((f) => ({ ...f, payloadBody: e.target.value }))}
                        placeholder={'{\n  "streetAddress": "6406 Ivy Lane",\n  "city": "Greenbelt",\n  "state": "MD",\n  "ZIPCode": "20770"\n}'}
                        rows={10}
                        className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500 resize-y"
                      />
                      <div className="pt-1 border-t border-gray-800 flex items-center gap-3">
                        <button
                          onClick={runPayloadTest}
                          disabled={testRunning || !endpointForm.path.trim() || !endpointForm.payloadBody.trim()}
                          className="bg-emerald-700 hover:bg-emerald-600 disabled:opacity-50 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors"
                        >
                          {testRunning ? 'Running…' : 'Run Test'}
                        </button>
                        {!endpointForm.payloadBody.trim() && (
                          <span className="text-gray-500 text-xs">Paste a payload above to run.</span>
                        )}
                      </div>
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

              </>)}{/* end flex row */}
              </div>
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

      {showDefHistory && (
        <VersionHistoryPanel
          title="Definition Version History"
          subtitle={def.name}
          listVersions={() => api.listDefinitionVersions(definitionId)}
          onRevert={async (versionNumber) => {
            const updated = await api.revertDefinition(definitionId, versionNumber)
            setDef(updated)
          }}
          onClose={() => setShowDefHistory(false)}
        />
      )}

      {historyEndpointId && (
        <VersionHistoryPanel
          title="Endpoint Version History"
          subtitle={endpoints.find((ep) => ep.id === historyEndpointId)?.name}
          listVersions={() => api.listEndpointVersions(definitionId, historyEndpointId)}
          onRevert={async (versionNumber) => {
            const updated = await api.revertEndpoint(definitionId, historyEndpointId, versionNumber)
            setEndpoints((prev) => prev.map((ep) => (ep.id === historyEndpointId ? updated : ep)))
          }}
          onClose={() => setHistoryEndpointId(null)}
        />
      )}
    </div>
  )
}
