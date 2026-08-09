import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import {
  listAdminApiPreferences,
  setAdminApiPreference,
  deleteAdminApiPreference,
  getAvailableEndpoints,
  listAdminTtsProviders,
  type TenantApiPreferenceRecord,
  type AvailableEndpointsResult,
  type AvailableEndpointItem,
  type TtsProviderInfo,
} from '../../api/adminApiDefinitions'
import { listAdminCredentials, setAdminCredential } from '../../api/adminCredentials'
import {
  API_CATEGORIES,
  API_SUB_TYPES,
  TTS_PROVIDER_LABELS,
} from '../../constants/apiTypes'

const TTS_SUB_TYPE = 'tts_streaming'

export default function AdminApiPreferencesPage() {
  const [prefsBySubType, setPrefsBySubType] = useState<Record<string, TenantApiPreferenceRecord>>({})
  const [loadingPrefs, setLoadingPrefs] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [expanded, setExpanded] = useState<string | null>(null)
  const [availableCache, setAvailableCache] = useState<Record<string, AvailableEndpointsResult>>({})
  const [loadingAvailable, setLoadingAvailable] = useState<string | null>(null)
  const [savingKey, setSavingKey] = useState<string | null>(null)

  const [ttsProviders, setTtsProviders] = useState<TtsProviderInfo[]>([])
  const [knownCreds, setKnownCreds] = useState<string[]>([])
  const [credValues, setCredValues] = useState<Record<string, string>>({})
  const [savingCred, setSavingCred] = useState<string | null>(null)

  useEffect(() => {
    loadPreferences()
    listAdminTtsProviders().then(setTtsProviders).catch(() => {})
    listAdminCredentials()
      .then((list) => setKnownCreds(list.map((c) => c.keyName)))
      .catch(() => {})
  }, [])

  async function loadPreferences() {
    setLoadingPrefs(true)
    setError(null)
    try {
      const list = await listAdminApiPreferences()
      const map: Record<string, TenantApiPreferenceRecord> = {}
      for (const p of list) map[p.apiSubType] = p
      setPrefsBySubType(map)
    } catch (e: unknown) {
      setError((e as Error).message)
    } finally {
      setLoadingPrefs(false)
    }
  }

  async function refreshAvailable(subType: string) {
    const result = await getAvailableEndpoints(subType)
    setAvailableCache((prev) => ({ ...prev, [subType]: result }))
  }

  async function toggleExpand(subType: string) {
    if (expanded === subType) {
      setExpanded(null)
      return
    }
    setExpanded(subType)
    if (!availableCache[subType]) {
      setLoadingAvailable(subType)
      try {
        await refreshAvailable(subType)
      } catch (e: unknown) {
        setError((e as Error).message)
      } finally {
        setLoadingAvailable(null)
      }
    }
  }

  async function handleSelect(subType: string, item: AvailableEndpointItem) {
    const key = `${subType}:${item.source}:${item.id}`
    setSavingKey(key)
    setError(null)
    try {
      const rec = await setAdminApiPreference(subType, item.source, item.id)
      setPrefsBySubType((prev) => ({ ...prev, [subType]: rec }))
      await refreshAvailable(subType)
    } catch (e: unknown) {
      setError((e as Error).message)
    } finally {
      setSavingKey(null)
    }
  }

  async function handleClear(subType: string) {
    const key = `${subType}:clear`
    setSavingKey(key)
    setError(null)
    try {
      await deleteAdminApiPreference(subType)
      setPrefsBySubType((prev) => {
        const next = { ...prev }
        delete next[subType]
        return next
      })
      await refreshAvailable(subType)
    } catch (e: unknown) {
      setError((e as Error).message)
    } finally {
      setSavingKey(null)
    }
  }

  async function handleSaveCred(keyName: string) {
    const value = credValues[keyName]?.trim()
    if (!value) return
    setSavingCred(keyName)
    setError(null)
    try {
      await setAdminCredential(keyName, value)
      setKnownCreds((prev) => (prev.includes(keyName) ? prev : [...prev, keyName]))
      setCredValues((prev) => ({ ...prev, [keyName]: '' }))
    } catch (e: unknown) {
      setError((e as Error).message)
    } finally {
      setSavingCred(null)
    }
  }

  function statusBadge(subType: string) {
    const pref = prefsBySubType[subType]
    if (!pref) return <span className="text-gray-500 text-xs">Platform default</span>
    return (
      <span
        className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${
          pref.source === 'tenant' ? 'bg-emerald-900/40 text-emerald-300' : 'bg-indigo-900/40 text-indigo-300'
        }`}
      >
        {pref.source === 'tenant' ? 'Your API' : 'Platform (selected)'}
      </span>
    )
  }

  // The TTS provider backing the currently-selected preference for tts_streaming, so the
  // credential form below the option list always matches whichever endpoint is actually
  // in effect — not whatever was last clicked.
  function selectedTtsProvider(): string | null {
    const pref = prefsBySubType[TTS_SUB_TYPE]
    const avail = availableCache[TTS_SUB_TYPE]
    if (!pref || !avail) return null
    const pool = pref.source === 'tenant' ? avail.tenantEndpoints : avail.portalEndpoints
    return pool.find((e) => e.id === pref.endpointId)?.definitionProvider ?? null
  }

  function renderOption(subType: string, item: AvailableEndpointItem) {
    const key = `${subType}:${item.source}:${item.id}`
    return (
      <div
        key={item.id}
        className={`flex items-center justify-between gap-3 rounded-lg border px-3 py-2 text-sm ${
          item.isTenantSelected ? 'border-indigo-600 bg-indigo-950/40' : 'border-gray-800 bg-gray-950/40'
        }`}
      >
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <span className="text-white font-medium truncate">{item.name}</span>
            {!item.isActive && <span className="text-gray-600 text-[10px] uppercase tracking-wide">inactive</span>}
            {item.isPreferred && item.source === 'tenant' && (
              <span className="text-amber-400 text-[10px] uppercase tracking-wide">preferred endpoint</span>
            )}
          </div>
          <p className="text-gray-500 text-xs truncate">
            {item.definitionName ?? '—'}
            {item.definitionProvider && ` · ${TTS_PROVIDER_LABELS[item.definitionProvider] ?? item.definitionProvider}`}
            {' · '}
            {item.path}
          </p>
        </div>
        {item.isTenantSelected ? (
          <span className="shrink-0 text-indigo-400 text-xs font-medium">Selected</span>
        ) : (
          <button
            onClick={() => handleSelect(subType, item)}
            disabled={savingKey === key || !item.isActive}
            className="shrink-0 text-xs font-medium text-gray-300 hover:text-white border border-gray-700 hover:border-gray-600 rounded-lg px-3 py-1.5 transition-colors disabled:opacity-40"
          >
            {savingKey === key ? '…' : 'Use this'}
          </button>
        )}
      </div>
    )
  }

  function renderTtsCredentials() {
    const provider = selectedTtsProvider()
    if (!provider) {
      return <p className="text-gray-500 text-xs mt-4">Select a TTS option above to configure its credentials.</p>
    }
    const info = ttsProviders.find((p) => p.key === provider)
    if (!info || info.requiredCredentialFields.length === 0) return null
    return (
      <div className="mt-4 pt-4 border-t border-gray-800">
        <p className="text-gray-300 text-sm font-medium mb-1">
          {TTS_PROVIDER_LABELS[provider] ?? provider} credentials
        </p>
        <p className="text-gray-500 text-xs mb-3">
          Stored securely per-tenant — required to stream audio through this provider, whether it's a
          platform-defined option or your own.
        </p>
        <div className="grid grid-cols-2 gap-3">
          {info.requiredCredentialFields.map((field) => {
            const keyName = `tts_${provider}_${field}`
            const isKnown = knownCreds.includes(keyName)
            return (
              <div key={field}>
                <label className="block text-gray-400 text-xs font-medium mb-1.5 capitalize">
                  {field} {isKnown && <span className="text-emerald-400 normal-case">· saved</span>}
                </label>
                <div className="flex gap-2">
                  <input
                    type="password"
                    value={credValues[keyName] ?? ''}
                    onChange={(e) => setCredValues((prev) => ({ ...prev, [keyName]: e.target.value }))}
                    placeholder={isKnown ? '••••••••  (leave blank to keep)' : 'Enter value'}
                    className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm placeholder-gray-600 focus:outline-none focus:border-indigo-500"
                  />
                  <button
                    onClick={() => handleSaveCred(keyName)}
                    disabled={!credValues[keyName]?.trim() || savingCred === keyName}
                    className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white text-xs font-medium px-3 rounded-lg transition-colors"
                  >
                    {savingCred === keyName ? '…' : 'Save'}
                  </button>
                </div>
              </div>
            )
          })}
        </div>
      </div>
    )
  }

  return (
    <AdminShell>
      <div className="p-6 max-w-4xl">
        <div className="mb-6">
          <h1 className="text-white text-xl font-semibold">API Preferences</h1>
          <p className="text-gray-400 text-sm mt-0.5">
            Choose which integration handles each API type. Pick a platform-provided option, or your own
            registration from{' '}
            <Link to="/admin/api-definitions" className="text-indigo-400 hover:text-indigo-300">
              API Definitions
            </Link>
            . Leave unset to use the platform default.
          </p>
        </div>

        {loadingPrefs && <p className="text-gray-400 text-sm">Loading…</p>}
        {error && <p className="text-red-400 text-sm mb-4">{error}</p>}

        {!loadingPrefs &&
          API_CATEGORIES.map((cat) => {
            const subs = API_SUB_TYPES.filter((s) => s.category === cat.value)
            if (subs.length === 0) return null
            return (
              <div key={cat.value} className="mb-8">
                <h2 className="text-xs font-semibold text-gray-500 uppercase tracking-widest mb-3">{cat.label}</h2>
                <div className="bg-gray-900 rounded-xl border border-gray-800 divide-y divide-gray-800 overflow-hidden">
                  {subs.map((sub) => (
                    <div key={sub.value}>
                      <button
                        onClick={() => toggleExpand(sub.value)}
                        className="w-full flex items-center justify-between px-4 py-3 text-left hover:bg-gray-800/30 transition-colors"
                      >
                        <span className="text-white text-sm font-medium">{sub.label}</span>
                        <div className="flex items-center gap-3">
                          {statusBadge(sub.value)}
                          <span className="text-gray-600 text-xs">{expanded === sub.value ? '▲' : '▼'}</span>
                        </div>
                      </button>

                      {expanded === sub.value && (
                        <div className="px-4 pb-4 border-t border-gray-800/60">
                          {loadingAvailable === sub.value && (
                            <p className="text-gray-500 text-xs pt-3">Loading options…</p>
                          )}
                          {availableCache[sub.value] && (
                            <>
                              <div className="pt-3">
                                <p className="text-gray-500 text-xs font-medium uppercase tracking-wide mb-2">
                                  Platform options
                                </p>
                                {availableCache[sub.value].portalEndpoints.length === 0 && (
                                  <p className="text-gray-600 text-xs">No platform-provided option for this type.</p>
                                )}
                                <div className="space-y-1.5">
                                  {availableCache[sub.value].portalEndpoints.map((item) => renderOption(sub.value, item))}
                                </div>
                              </div>

                              <div className="pt-4">
                                <div className="flex items-center justify-between mb-2">
                                  <p className="text-gray-500 text-xs font-medium uppercase tracking-wide">
                                    Your options
                                  </p>
                                  <Link
                                    to="/admin/api-definitions"
                                    className="text-indigo-400 hover:text-indigo-300 text-xs font-medium"
                                  >
                                    + Register your own →
                                  </Link>
                                </div>
                                {availableCache[sub.value].tenantEndpoints.length === 0 && (
                                  <p className="text-gray-600 text-xs">
                                    You haven't registered your own endpoint for this type yet.
                                  </p>
                                )}
                                <div className="space-y-1.5">
                                  {availableCache[sub.value].tenantEndpoints.map((item) => renderOption(sub.value, item))}
                                </div>
                              </div>

                              {prefsBySubType[sub.value] && (
                                <button
                                  onClick={() => handleClear(sub.value)}
                                  disabled={savingKey === `${sub.value}:clear`}
                                  className="mt-4 text-red-400 hover:text-red-300 text-xs font-medium disabled:opacity-50"
                                >
                                  {savingKey === `${sub.value}:clear` ? 'Clearing…' : 'Clear preference (use platform default)'}
                                </button>
                              )}

                              {sub.value === TTS_SUB_TYPE && renderTtsCredentials()}
                            </>
                          )}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )
          })}
      </div>
    </AdminShell>
  )
}
