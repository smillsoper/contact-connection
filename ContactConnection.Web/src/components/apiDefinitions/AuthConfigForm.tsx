// Auth configuration sub-form for API definitions.
// Credentials are stored as key references (names pointing to a future credentials store),
// not as plain-text values — matching the "safe storage" intent.

export type AuthType = 'none' | 'api_key' | 'basic' | 'bearer' | 'oauth2' | 'hmac'

export interface AuthConfigNone { type: 'none' }
export interface AuthConfigApiKey {
  type: 'api_key'
  placement: 'header' | 'query'
  paramName: string
  credentialKey: string
}
export interface AuthConfigBasic {
  type: 'basic'
  usernameKey: string
  passwordKey: string
}
export interface AuthConfigBearer {
  type: 'bearer'
  tokenKey: string
}
export interface AuthConfigOAuth2 {
  type: 'oauth2'
  tokenUrl: string
  method: string
  grantType: string
  scopes: string
  clientIdKey: string
  clientSecretKey: string
  credentialPlacement: 'header' | 'body'
  tokenRequestContentType: string
  tokenRequestTemplate: string
  tokenField: string
  expiresInField: string
}
export interface AuthConfigHmac {
  type: 'hmac'
  algorithm: string
  secretKey: string
  headerName: string
  includeTimestamp: boolean
}

export type AuthConfig =
  | AuthConfigNone
  | AuthConfigApiKey
  | AuthConfigBasic
  | AuthConfigBearer
  | AuthConfigOAuth2
  | AuthConfigHmac

export function parseAuthConfig(json: string): AuthConfig {
  try {
    return JSON.parse(json) as AuthConfig
  } catch {
    return { type: 'none' }
  }
}

function buildDefaultTokenRequestTemplate(clientIdKey: string, clientSecretKey: string, contentType: string): string {
  const idPlaceholder = clientIdKey.trim() || 'client_id'
  const secretPlaceholder = clientSecretKey.trim() || 'client_secret'
  if (contentType === 'application/x-www-form-urlencoded') {
    return `grant_type=client_credentials&client_id={{${idPlaceholder}}}&client_secret={{${secretPlaceholder}}}`
  }
  return `{\n  "grant_type": "client_credentials",\n  "client_id": "{{${idPlaceholder}}}",\n  "client_secret": "{{${secretPlaceholder}}}"\n}`
}

export function serializeAuthConfig(state: AuthFormState): string {
  const type = state.authType as AuthType
  switch (type) {
    case 'none':
      return JSON.stringify({ type: 'none' } satisfies AuthConfigNone)
    case 'api_key':
      return JSON.stringify({
        type: 'api_key',
        placement: state.apiKeyPlacement,
        paramName: state.apiKeyParamName,
        credentialKey: state.apiKeyCredentialKey,
      } satisfies AuthConfigApiKey)
    case 'basic':
      return JSON.stringify({
        type: 'basic',
        usernameKey: state.basicUsernameKey,
        passwordKey: state.basicPasswordKey,
      } satisfies AuthConfigBasic)
    case 'bearer':
      return JSON.stringify({
        type: 'bearer',
        tokenKey: state.bearerTokenKey,
      } satisfies AuthConfigBearer)
    case 'oauth2':
      return JSON.stringify({
        type: 'oauth2',
        tokenUrl: state.oauth2TokenUrl,
        method: state.oauth2Method,
        grantType: state.oauth2GrantType,
        scopes: state.oauth2Scopes,
        clientIdKey: state.oauth2ClientIdKey,
        clientSecretKey: state.oauth2ClientSecretKey,
        credentialPlacement: state.oauth2CredentialPlacement,
        tokenRequestContentType: state.oauth2TokenRequestContentType,
        tokenRequestTemplate: state.oauth2TokenRequestTemplate,
        tokenField: state.oauth2TokenField,
        expiresInField: state.oauth2ExpiresInField,
      } satisfies AuthConfigOAuth2)
    case 'hmac':
      return JSON.stringify({
        type: 'hmac',
        algorithm: state.hmacAlgorithm,
        secretKey: state.hmacSecretKey,
        headerName: state.hmacHeaderName,
        includeTimestamp: state.hmacIncludeTimestamp,
      } satisfies AuthConfigHmac)
    default:
      return JSON.stringify({ type: 'none' })
  }
}

export interface AuthFormState {
  authType: string
  // API Key
  apiKeyPlacement: 'header' | 'query'
  apiKeyParamName: string
  apiKeyCredentialKey: string
  // Basic
  basicUsernameKey: string
  basicPasswordKey: string
  // Bearer
  bearerTokenKey: string
  // OAuth2
  oauth2TokenUrl: string
  oauth2Method: string
  oauth2GrantType: string
  oauth2Scopes: string
  oauth2ClientIdKey: string
  oauth2ClientSecretKey: string
  oauth2CredentialPlacement: 'header' | 'body'
  oauth2TokenRequestContentType: string
  oauth2TokenRequestTemplate: string
  // set to true once user manually edits the template — prevents auto-overwrite on key changes
  oauth2TokenRequestTemplateEdited: boolean
  oauth2TokenField: string
  oauth2ExpiresInField: string
  // HMAC
  hmacAlgorithm: string
  hmacSecretKey: string
  hmacHeaderName: string
  hmacIncludeTimestamp: boolean
}

export const BLANK_AUTH_STATE: AuthFormState = {
  authType: 'none',
  apiKeyPlacement: 'header',
  apiKeyParamName: '',
  apiKeyCredentialKey: '',
  basicUsernameKey: '',
  basicPasswordKey: '',
  bearerTokenKey: '',
  oauth2TokenUrl: '',
  oauth2Method: 'POST',
  oauth2GrantType: 'client_credentials',
  oauth2Scopes: '',
  oauth2ClientIdKey: '',
  oauth2ClientSecretKey: '',
  oauth2CredentialPlacement: 'header',
  oauth2TokenRequestContentType: 'application/json',
  oauth2TokenRequestTemplate: '',
  oauth2TokenRequestTemplateEdited: false,
  oauth2TokenField: 'access_token',
  oauth2ExpiresInField: 'expires_in',
  hmacAlgorithm: 'SHA256',
  hmacSecretKey: '',
  hmacHeaderName: 'X-Signature',
  hmacIncludeTimestamp: true,
}

export function authStateFromConfig(json: string): AuthFormState {
  const cfg = parseAuthConfig(json)
  const base = { ...BLANK_AUTH_STATE, authType: cfg.type }
  switch (cfg.type) {
    case 'api_key':
      return { ...base, apiKeyPlacement: cfg.placement, apiKeyParamName: cfg.paramName, apiKeyCredentialKey: cfg.credentialKey }
    case 'basic':
      return { ...base, basicUsernameKey: cfg.usernameKey, basicPasswordKey: cfg.passwordKey }
    case 'bearer':
      return { ...base, bearerTokenKey: cfg.tokenKey }
    case 'oauth2':
      return {
        ...base,
        oauth2TokenUrl: cfg.tokenUrl,
        oauth2Method: cfg.method,
        oauth2GrantType: cfg.grantType,
        oauth2Scopes: cfg.scopes,
        oauth2ClientIdKey: cfg.clientIdKey,
        oauth2ClientSecretKey: cfg.clientSecretKey,
        oauth2CredentialPlacement: cfg.credentialPlacement ?? 'header',
        oauth2TokenRequestContentType: cfg.tokenRequestContentType ?? 'application/json',
        oauth2TokenRequestTemplate: cfg.tokenRequestTemplate ?? '',
        oauth2TokenRequestTemplateEdited: cfg.tokenRequestTemplate ? cfg.tokenRequestTemplate.length > 0 : false,
        oauth2TokenField: cfg.tokenField,
        oauth2ExpiresInField: cfg.expiresInField,
      }
    case 'hmac':
      return {
        ...base,
        hmacAlgorithm: cfg.algorithm,
        hmacSecretKey: cfg.secretKey,
        hmacHeaderName: cfg.headerName,
        hmacIncludeTimestamp: cfg.includeTimestamp,
      }
    default:
      return base
  }
}

const AUTH_METHODS = [
  { value: 'none', label: 'None' },
  { value: 'api_key', label: 'API Key' },
  { value: 'basic', label: 'Basic Auth' },
  { value: 'bearer', label: 'Bearer Token' },
  { value: 'oauth2', label: 'OAuth2' },
  { value: 'hmac', label: 'HMAC Signature' },
]

const HTTP_METHODS = ['POST', 'GET', 'PUT', 'PATCH']
const OAUTH_GRANT_TYPES = [
  { value: 'client_credentials', label: 'Client Credentials' },
  { value: 'authorization_code', label: 'Authorization Code' },
  { value: 'password', label: 'Resource Owner Password' },
]
const HMAC_ALGORITHMS = ['SHA256', 'SHA512', 'SHA1', 'MD5']
const CONTENT_TYPES = [
  { value: 'application/json', label: 'JSON (application/json)' },
  { value: 'application/x-www-form-urlencoded', label: 'Form Encoded (application/x-www-form-urlencoded)' },
]

interface InputProps {
  label: string
  value: string
  onChange: (v: string) => void
  placeholder?: string
  hint?: string
  type?: string
}

function Field({ label, value, onChange, placeholder, hint, type = 'text' }: InputProps) {
  return (
    <div>
      <label className="block text-gray-400 text-xs font-medium mb-1">{label}</label>
      <input
        type={type}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
      />
      {hint && <p className="text-gray-600 text-xs mt-1">{hint}</p>}
    </div>
  )
}

interface Props {
  state: AuthFormState
  onChange: (patch: Partial<AuthFormState>) => void
}

export default function AuthConfigForm({ state, onChange }: Props) {
  const set = (patch: Partial<AuthFormState>) => onChange(patch)

  // Regenerate the token request body template when keys or content-type change,
  // unless the user has manually edited it.
  function handleOAuth2KeyChange(patch: Partial<AuthFormState>) {
    const next = { ...state, ...patch }
    if (next.oauth2CredentialPlacement === 'body' && !next.oauth2TokenRequestTemplateEdited) {
      const generated = buildDefaultTokenRequestTemplate(
        next.oauth2ClientIdKey,
        next.oauth2ClientSecretKey,
        next.oauth2TokenRequestContentType,
      )
      set({ ...patch, oauth2TokenRequestTemplate: generated })
    } else {
      set(patch)
    }
  }

  function handleSwitchToBody() {
    const generated = buildDefaultTokenRequestTemplate(
      state.oauth2ClientIdKey,
      state.oauth2ClientSecretKey,
      state.oauth2TokenRequestContentType,
    )
    set({
      oauth2CredentialPlacement: 'body',
      oauth2TokenRequestTemplate: generated,
      oauth2TokenRequestTemplateEdited: false,
    })
  }

  function handleContentTypeChange(ct: string) {
    if (state.oauth2CredentialPlacement === 'body' && !state.oauth2TokenRequestTemplateEdited) {
      const generated = buildDefaultTokenRequestTemplate(state.oauth2ClientIdKey, state.oauth2ClientSecretKey, ct)
      set({ oauth2TokenRequestContentType: ct, oauth2TokenRequestTemplate: generated })
    } else {
      set({ oauth2TokenRequestContentType: ct })
    }
  }

  function handleResetTemplate() {
    const generated = buildDefaultTokenRequestTemplate(
      state.oauth2ClientIdKey,
      state.oauth2ClientSecretKey,
      state.oauth2TokenRequestContentType,
    )
    set({ oauth2TokenRequestTemplate: generated, oauth2TokenRequestTemplateEdited: false })
  }

  return (
    <div className="space-y-3">
      {/* ── Auth method selector ── */}
      <div>
        <label className="block text-gray-400 text-xs font-medium mb-1">Authentication Method</label>
        <select
          value={state.authType}
          onChange={(e) => set({ authType: e.target.value })}
          className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
        >
          {AUTH_METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
        </select>
      </div>

      {/* ── API Key ── */}
      {state.authType === 'api_key' && (
        <div className="border border-gray-700 rounded-lg p-3 space-y-3 bg-gray-800/30">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-gray-400 text-xs font-medium mb-1">Placement</label>
              <select
                value={state.apiKeyPlacement}
                onChange={(e) => set({ apiKeyPlacement: e.target.value as 'header' | 'query' })}
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
              >
                <option value="header">Request Header</option>
                <option value="query">Query Parameter</option>
              </select>
            </div>
            <Field
              label={state.apiKeyPlacement === 'header' ? 'Header Name' : 'Param Name'}
              value={state.apiKeyParamName}
              onChange={(v) => set({ apiKeyParamName: v })}
              placeholder={state.apiKeyPlacement === 'header' ? 'X-API-Key' : 'api_key'}
            />
          </div>
          <Field
            label="Credential Key Reference"
            value={state.apiKeyCredentialKey}
            onChange={(v) => set({ apiKeyCredentialKey: v })}
            placeholder="e.g. usps_api_key"
            hint="Name of the stored credential. Actual value is managed in the Credentials store."
          />
        </div>
      )}

      {/* ── Basic Auth ── */}
      {state.authType === 'basic' && (
        <div className="border border-gray-700 rounded-lg p-3 space-y-3 bg-gray-800/30">
          <div className="grid grid-cols-2 gap-3">
            <Field
              label="Username Credential Key"
              value={state.basicUsernameKey}
              onChange={(v) => set({ basicUsernameKey: v })}
              placeholder="e.g. usps_username"
              hint="Key reference to stored credential"
            />
            <Field
              label="Password Credential Key"
              value={state.basicPasswordKey}
              onChange={(v) => set({ basicPasswordKey: v })}
              placeholder="e.g. usps_password"
              hint="Key reference to stored credential"
            />
          </div>
        </div>
      )}

      {/* ── Bearer Token ── */}
      {state.authType === 'bearer' && (
        <div className="border border-gray-700 rounded-lg p-3 bg-gray-800/30">
          <Field
            label="Token Credential Key"
            value={state.bearerTokenKey}
            onChange={(v) => set({ bearerTokenKey: v })}
            placeholder="e.g. stripe_bearer_token"
            hint="Name of the stored credential. Sent as Authorization: Bearer {value}."
          />
        </div>
      )}

      {/* ── OAuth2 ── */}
      {state.authType === 'oauth2' && (
        <div className="border border-gray-700 rounded-lg p-3 space-y-3 bg-gray-800/30">
          {/* Token endpoint */}
          <p className="text-gray-500 text-xs font-medium uppercase tracking-wide">Token endpoint</p>
          <div className="grid grid-cols-4 gap-3">
            <div className="col-span-3">
              <Field
                label="Token URL"
                value={state.oauth2TokenUrl}
                onChange={(v) => set({ oauth2TokenUrl: v })}
                placeholder="https://auth.example.com/oauth/token"
              />
            </div>
            <div>
              <label className="block text-gray-400 text-xs font-medium mb-1">Method</label>
              <select
                value={state.oauth2Method}
                onChange={(e) => set({ oauth2Method: e.target.value })}
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
              >
                {HTTP_METHODS.map((m) => <option key={m} value={m}>{m}</option>)}
              </select>
            </div>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-gray-400 text-xs font-medium mb-1">Grant Type</label>
              <select
                value={state.oauth2GrantType}
                onChange={(e) => set({ oauth2GrantType: e.target.value })}
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
              >
                {OAUTH_GRANT_TYPES.map((g) => <option key={g.value} value={g.value}>{g.label}</option>)}
              </select>
            </div>
            <Field
              label="Scopes (space-separated)"
              value={state.oauth2Scopes}
              onChange={(v) => set({ oauth2Scopes: v })}
              placeholder="read write"
            />
          </div>

          {/* Credentials */}
          <p className="text-gray-500 text-xs font-medium uppercase tracking-wide pt-1">Credentials</p>
          <div className="grid grid-cols-2 gap-3">
            <Field
              label="Client ID Credential Key"
              value={state.oauth2ClientIdKey}
              onChange={(v) => handleOAuth2KeyChange({ oauth2ClientIdKey: v })}
              placeholder="e.g. usps_client_id"
              hint="Key reference to stored credential"
            />
            <Field
              label="Client Secret Credential Key"
              value={state.oauth2ClientSecretKey}
              onChange={(v) => handleOAuth2KeyChange({ oauth2ClientSecretKey: v })}
              placeholder="e.g. usps_client_secret"
              hint="Key reference to stored credential"
            />
          </div>

          {/* Credential placement toggle */}
          <div>
            <label className="block text-gray-400 text-xs font-medium mb-1.5">Credential Placement</label>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => set({ oauth2CredentialPlacement: 'header' })}
                className={`flex-1 px-3 py-2 rounded-lg text-sm font-medium transition-colors border ${
                  state.oauth2CredentialPlacement === 'header'
                    ? 'bg-indigo-600 border-indigo-500 text-white'
                    : 'bg-gray-800 border-gray-700 text-gray-400 hover:text-white hover:border-gray-600'
                }`}
              >
                Header (Basic Auth)
              </button>
              <button
                type="button"
                onClick={handleSwitchToBody}
                className={`flex-1 px-3 py-2 rounded-lg text-sm font-medium transition-colors border ${
                  state.oauth2CredentialPlacement === 'body'
                    ? 'bg-indigo-600 border-indigo-500 text-white'
                    : 'bg-gray-800 border-gray-700 text-gray-400 hover:text-white hover:border-gray-600'
                }`}
              >
                Body (JSON / Form)
              </button>
            </div>
            {state.oauth2CredentialPlacement === 'header' && (
              <p className="text-gray-600 text-xs mt-1">Credentials sent as Authorization: Basic base64(clientId:clientSecret)</p>
            )}
          </div>

          {/* Body template — shown only when placement = body */}
          {state.oauth2CredentialPlacement === 'body' && (
            <div className="space-y-2">
              <div>
                <label className="block text-gray-400 text-xs font-medium mb-1">Token Request Content-Type</label>
                <select
                  value={state.oauth2TokenRequestContentType}
                  onChange={(e) => handleContentTypeChange(e.target.value)}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
                >
                  {CONTENT_TYPES.map((ct) => <option key={ct.value} value={ct.value}>{ct.label}</option>)}
                </select>
              </div>
              <div>
                <div className="flex items-center justify-between mb-1">
                  <label className="text-gray-400 text-xs font-medium">Token Request Body</label>
                  <button
                    type="button"
                    onClick={handleResetTemplate}
                    className="text-gray-600 hover:text-gray-400 text-xs transition-colors"
                    title="Reset to auto-generated template"
                  >
                    Reset to default
                  </button>
                </div>
                <textarea
                  value={state.oauth2TokenRequestTemplate}
                  onChange={(e) => set({ oauth2TokenRequestTemplate: e.target.value, oauth2TokenRequestTemplateEdited: true })}
                  rows={5}
                  spellCheck={false}
                  className="w-full bg-gray-900 border border-gray-700 rounded-lg px-3 py-2 text-green-300 text-xs font-mono placeholder-gray-600 focus:outline-none focus:border-indigo-500 resize-none"
                  placeholder={buildDefaultTokenRequestTemplate('client_id', 'client_secret', state.oauth2TokenRequestContentType)}
                />
                <p className="text-gray-600 text-xs mt-1">
                  Use <span className="text-gray-400 font-mono">{'{{'}</span><span className="text-gray-400 font-mono">credential_key</span><span className="text-gray-400 font-mono">{'}}'}</span> to reference stored credentials. Values are substituted at runtime.
                </p>
              </div>
            </div>
          )}

          {/* Token response mapping */}
          <p className="text-gray-500 text-xs font-medium uppercase tracking-wide pt-1">Token response mapping</p>
          <div className="grid grid-cols-2 gap-3">
            <Field
              label="Token Field Name"
              value={state.oauth2TokenField}
              onChange={(v) => set({ oauth2TokenField: v })}
              placeholder="access_token"
              hint="Field in token response containing the bearer token"
            />
            <Field
              label="Expires In Field Name"
              value={state.oauth2ExpiresInField}
              onChange={(v) => set({ oauth2ExpiresInField: v })}
              placeholder="expires_in"
              hint="Field containing TTL in seconds (optional)"
            />
          </div>
        </div>
      )}

      {/* ── HMAC ── */}
      {state.authType === 'hmac' && (
        <div className="border border-gray-700 rounded-lg p-3 space-y-3 bg-gray-800/30">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-gray-400 text-xs font-medium mb-1">Algorithm</label>
              <select
                value={state.hmacAlgorithm}
                onChange={(e) => set({ hmacAlgorithm: e.target.value })}
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-indigo-500"
              >
                {HMAC_ALGORITHMS.map((a) => <option key={a} value={a}>{a}</option>)}
              </select>
            </div>
            <Field
              label="Signature Header Name"
              value={state.hmacHeaderName}
              onChange={(v) => set({ hmacHeaderName: v })}
              placeholder="X-Signature"
            />
          </div>
          <Field
            label="Secret Credential Key"
            value={state.hmacSecretKey}
            onChange={(v) => set({ hmacSecretKey: v })}
            placeholder="e.g. shopify_webhook_secret"
            hint="Key reference to stored credential"
          />
          <label className="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={state.hmacIncludeTimestamp}
              onChange={(e) => set({ hmacIncludeTimestamp: e.target.checked })}
              className="w-4 h-4 rounded bg-gray-800 border-gray-600 accent-indigo-500"
            />
            <span className="text-gray-300 text-sm">Include request timestamp in signature</span>
          </label>
        </div>
      )}
    </div>
  )
}
