import { useState, type FormEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import PortalShell from '../../components/portal/PortalShell'
import { provisionTenant, type TenantFeatureFlags } from '../../api/portal'
import { TIMEZONES } from '../../utils/timezones'

const FLAG_LABELS: Record<keyof TenantFeatureFlags, string> = {
  telephony: 'Telephony',
  omsBuiltIn: 'Commerce Engine (OMS)',
  shopifyAdapter: 'Shopify Adapter',
  tenantChat: 'Tenant Chat',
}

export default function ProvisionTenantPage() {
  const navigate = useNavigate()

  const [name, setName] = useState('')
  const [subdomain, setSubdomain] = useState('')
  const [timezone, setTimezone] = useState('America/New_York')
  const [inviteEmail, setInviteEmail] = useState('')
  const [flags, setFlags] = useState<TenantFeatureFlags>({
    telephony: false,
    omsBuiltIn: false,
    shopifyAdapter: false,
    tenantChat: false,
  })
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  function handleNameChange(value: string) {
    setName(value)
    const derived = value
      .toLowerCase()
      .replace(/[^a-z0-9]/g, '-')
      .replace(/-+/g, '-')
      .replace(/^-|-$/g, '')
    setSubdomain(derived)
  }

  function toggleFlag(key: keyof TenantFeatureFlags) {
    setFlags((prev) => ({ ...prev, [key]: !prev[key] }))
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const tenant = await provisionTenant({
        name,
        subdomain,
        timezone,
        featureFlags: flags,
        inviteEmail: inviteEmail || undefined,
      })
      navigate(`/portal/tenants/${tenant.id}`, { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to provision tenant.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <PortalShell>
      <div className="p-6 max-w-lg">
        <div className="flex items-center gap-3 mb-6">
          <Link to="/portal/tenants" className="text-gray-400 hover:text-white text-sm transition-colors">
            ← Tenants
          </Link>
          <span className="text-gray-600">/</span>
          <h1 className="text-white text-xl font-semibold">Provision tenant</h1>
        </div>

        <div className="bg-gray-900 rounded-xl border border-gray-800 p-6">
          <form onSubmit={handleSubmit} className="flex flex-col gap-5">

            {/* Tenant name */}
            <div>
              <label className="block mb-1.5 text-sky-400 text-sm font-medium">Tenant name</label>
              <input
                type="text"
                required
                value={name}
                onChange={(e) => handleNameChange(e.target.value)}
                placeholder="Acme Corp"
                className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>

            {/* Subdomain */}
            <div>
              <label className="block mb-1.5 text-sky-400 text-sm font-medium">Subdomain</label>
              <div className="flex items-center gap-2">
                <input
                  type="text"
                  required
                  value={subdomain}
                  onChange={(e) => setSubdomain(e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, ''))}
                  className="flex-1 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
                <span className="text-gray-500 text-sm whitespace-nowrap">.contactconnection.local</span>
              </div>
              <p className="text-gray-500 text-xs mt-1">Lowercase letters, numbers, and hyphens only.</p>
            </div>

            {/* Invite email */}
            <div>
              <label className="block mb-1.5 text-sky-400 text-sm font-medium">Invite email</label>
              <input
                type="email"
                value={inviteEmail}
                onChange={(e) => setInviteEmail(e.target.value)}
                placeholder="admin@acmecorp.com"
                className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
              />
              <p className="text-gray-500 text-xs mt-1">The onboarding invitation will be sent to this address.</p>
            </div>

            {/* Timezone */}
            <div>
              <label className="block mb-1.5 text-sky-400 text-sm font-medium">Timezone</label>
              <select
                value={timezone}
                onChange={(e) => setTimezone(e.target.value)}
                className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
              >
                {TIMEZONES.map((tz) => (
                  <option key={tz.value} value={tz.value}>{tz.label}</option>
                ))}
              </select>
            </div>

            {/* Feature flags */}
            <div>
              <label className="block mb-3 text-sky-400 text-sm font-medium">Features</label>
              <div className="grid grid-cols-2 gap-3">
                {(Object.keys(FLAG_LABELS) as (keyof TenantFeatureFlags)[]).map((key) => (
                  <label
                    key={key}
                    className="flex items-center gap-3 cursor-pointer group"
                    onClick={() => toggleFlag(key)}
                  >
                    <div
                      className={`w-9 h-5 rounded-full relative transition-colors shrink-0 ${
                        flags[key] ? 'bg-indigo-600' : 'bg-gray-700'
                      }`}
                    >
                      <div
                        className={`absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-transform ${
                          flags[key] ? 'translate-x-4' : 'translate-x-0.5'
                        }`}
                      />
                    </div>
                    <span className="text-gray-300 text-sm group-hover:text-white transition-colors">
                      {FLAG_LABELS[key]}
                    </span>
                  </label>
                ))}
              </div>
            </div>

            {error && <p className="text-red-400 text-sm">{error}</p>}

            <div className="flex items-center gap-3 pt-2">
              <button
                type="submit"
                disabled={loading}
                className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
              >
                {loading ? 'Provisioning…' : 'Provision tenant'}
              </button>
              <Link
                to="/portal/tenants"
                className="text-gray-400 hover:text-white text-sm transition-colors"
              >
                Cancel
              </Link>
            </div>
          </form>
        </div>
      </div>
    </PortalShell>
  )
}
