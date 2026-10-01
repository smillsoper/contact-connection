import { useCallback, useEffect, useState } from 'react'
import { api } from '../../api/client'
import { deleteAdminCredential, setAdminCredential } from '../../api/adminCredentials'

// Campaign settings credential cards (S169) — shared by Payment Gateways and Sales Tax. Tenants enter vendor
// credentials here instead of guessing key names on the Credentials page: each field shows where its value
// currently comes from (this campaign / the client / the tenant default / not set), secrets are write-only,
// and saving writes the right key ({Vendor}:{campaignId|clientId}:{Field} or {Vendor}:{Field}) through the
// audited Credentials API. "Test credentials" checks them with the vendor without charging or recording anything.

export type CredentialSection = 'payment-gateways' | 'tax-providers'

/** Loads and renders one card per vendor in the section; `only` limits it to one provider key. */
export default function CampaignCredentialCards({ campaignId, section, only }: { campaignId: string; section: CredentialSection; only?: string }) {
  const [sets, setSets] = useState<CredentialSet[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    api.get<CredentialSet[]>(`/api/v1/campaigns/${campaignId}/${section}`)
      .then(setSets)
      .catch((e: Error) => setError(e.message))
  }, [campaignId, section])
  useEffect(load, [load])

  const shown = sets?.filter((s) => only === undefined || s.providerKey === only)
  return (
    <>
      {error && <p className="text-red-400 text-sm">{error}</p>}
      {!sets && !error && <p className="text-gray-500 text-sm">Loading…</p>}
      <div className="space-y-6">
        {shown?.map((s) => <CredentialCard key={s.providerKey} campaignId={campaignId} section={section} gateway={s} onChanged={load} />)}
      </div>
    </>
  )
}

type Scope = 'campaign' | 'client' | 'tenant'

interface GatewayField {
  name: string
  label: string
  secret: boolean
  options: string[] | null
  default: string | null
  help: string | null
  source: Scope | null
  value: string | null
  keys: Record<Scope, string>
}

export interface CredentialSet {
  providerKey: string
  displayName: string
  credentialVendor: string
  instructions: string
  fields: GatewayField[]
}

interface TestResult { succeeded: boolean; message: string; environment: string | null }

const SOURCE_LABEL: Record<Scope, string> = {
  campaign: 'Set for this campaign',
  client: 'From the client (all its campaigns)',
  tenant: 'Tenant default',
}

const inputCls = 'w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500'

function CredentialCard({ campaignId, section, gateway, onChanged }: { campaignId: string; section: CredentialSection; gateway: CredentialSet; onChanged: () => void }) {
  const [scope, setScope] = useState<Scope>('campaign')
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const [test, setTest] = useState<TestResult | null>(null)

  const changed = gateway.fields.filter((f) => (drafts[f.name] ?? '').trim() !== '')

  async function save() {
    setBusy(true); setMsg(null); setTest(null)
    try {
      for (const f of changed) await setAdminCredential(f.keys[scope], drafts[f.name].trim())
      setDrafts({})
      setMsg({ ok: true, text: `Saved ${changed.length} value${changed.length === 1 ? '' : 's'} (${scope === 'campaign' ? 'this campaign' : scope === 'client' ? 'client-wide' : 'tenant default'}).` })
      onChanged()
    } catch (e) {
      setMsg({ ok: false, text: e instanceof Error ? e.message : 'Save failed.' })
    } finally { setBusy(false) }
  }

  async function clearCampaignValue(f: GatewayField) {
    setBusy(true); setMsg(null); setTest(null)
    try {
      await deleteAdminCredential(f.keys.campaign)
      setMsg({ ok: true, text: `${f.label}: campaign value removed — now uses the client or tenant value, if any.` })
      onChanged()
    } catch (e) {
      setMsg({ ok: false, text: e instanceof Error ? e.message : 'Remove failed.' })
    } finally { setBusy(false) }
  }

  async function runTest() {
    setBusy(true); setTest(null); setMsg(null)
    try {
      setTest(await api.post<TestResult>(`/api/v1/campaigns/${campaignId}/${section}/${gateway.providerKey}/test`))
    } catch (e) {
      setTest({ succeeded: false, message: e instanceof Error ? e.message : 'Test failed.', environment: null })
    } finally { setBusy(false) }
  }

  const configured = gateway.fields.filter((f) => f.secret).every((f) => f.source)

  return (
    <div className="border border-gray-800 rounded-lg p-4">
      <div className="flex flex-wrap items-center gap-3 mb-2">
        <h3 className="text-white text-sm font-semibold">{gateway.displayName}</h3>
        <span className={`text-xs rounded px-1.5 py-0.5 border ${configured ? 'text-emerald-300 border-emerald-800 bg-emerald-950/40' : 'text-gray-400 border-gray-700'}`}>
          {configured ? 'Configured' : 'Not configured'}
        </span>
      </div>
      <p className="text-xs text-gray-400 mb-4 leading-snug">{gateway.instructions}</p>

      <div className="space-y-3">
        {gateway.fields.map((f) => (
          <div key={f.name} className="grid grid-cols-1 md:grid-cols-[12rem_1fr] gap-2 items-start">
            <div>
              <p className="text-sm text-gray-200">{f.label}</p>
              <p className={`text-xs ${f.source ? 'text-emerald-400' : 'text-gray-500'}`}>
                {f.source ? SOURCE_LABEL[f.source] : 'Not set'}
                {!f.secret && f.value ? <span className="text-gray-400"> · {f.value}</span> : null}
              </p>
            </div>
            <div>
              <div className="flex gap-2">
                {f.options ? (
                  <select value={drafts[f.name] ?? ''} onChange={(e) => setDrafts({ ...drafts, [f.name]: e.target.value })} className={inputCls}>
                    <option value="">{f.source ? '— keep current —' : `— default: ${f.default ?? 'none'} —`}</option>
                    {f.options.map((o) => <option key={o} value={o}>{o}</option>)}
                  </select>
                ) : (
                  <input
                    type={f.secret ? 'password' : 'text'}
                    autoComplete="new-password"
                    value={drafts[f.name] ?? ''}
                    placeholder={f.source ? '•••••••• (set — type to replace)' : 'Enter value'}
                    onChange={(e) => setDrafts({ ...drafts, [f.name]: e.target.value })}
                    className={inputCls}
                  />
                )}
                {f.source === 'campaign' && (
                  <button onClick={() => clearCampaignValue(f)} disabled={busy}
                    className="text-xs text-gray-400 hover:text-red-300 whitespace-nowrap px-2" title="Remove this campaign's value and fall back to the client / tenant value">
                    Remove
                  </button>
                )}
              </div>
              {f.help && <p className="text-[11px] text-gray-500 mt-1 leading-snug">{f.help}</p>}
            </div>
          </div>
        ))}
      </div>

      <div className="flex flex-wrap items-center gap-3 mt-5 pt-4 border-t border-gray-800">
        <label className="text-xs text-gray-400">Save to</label>
        <select value={scope} onChange={(e) => setScope(e.target.value as Scope)} className="bg-gray-800 text-white rounded-lg px-2 py-1.5 text-sm">
          <option value="campaign">This campaign</option>
          <option value="client">All of this client's campaigns</option>
          <option value="tenant">Tenant default (all campaigns)</option>
        </select>
        <button onClick={save} disabled={busy || changed.length === 0}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded-lg px-4 py-1.5 text-sm font-medium">
          {busy ? 'Working…' : 'Save'}
        </button>
        <button onClick={runTest} disabled={busy}
          className="border border-gray-700 text-gray-200 hover:bg-gray-800 disabled:opacity-40 rounded-lg px-4 py-1.5 text-sm">
          Test credentials
        </button>
        {msg && <span className={`text-xs ${msg.ok ? 'text-emerald-400' : 'text-red-400'}`}>{msg.text}</span>}
      </div>
      {test && (
        <p className={`text-sm mt-3 ${test.succeeded ? 'text-emerald-300' : 'text-red-300'}`}>
          {test.succeeded ? '✓ ' : '✗ '}{test.message}
        </p>
      )}
    </div>
  )
}
