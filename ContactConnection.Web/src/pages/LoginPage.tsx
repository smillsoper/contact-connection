import { useState, useEffect, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'
import { useSipStore } from '../stores/sipStore'
import { authApi } from '../api/auth'
import { getSubdomainFromHostname } from '../utils/subdomain'

export default function LoginPage() {
  const navigate = useNavigate()
  const setAuth = useAuthStore((s) => s.setAuth)
  const setSipCredentials = useSipStore((s) => s.setSipCredentials)

  const urlSubdomain = getSubdomainFromHostname()
  const [subdomain, setSubdomain] = useState(urlSubdomain ?? '')
  const [tenantName, setTenantName] = useState<string | null>(null)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  // Splash: 'splash' → 'fading' (CSS transition) → 'done'
  const [splash, setSplash] = useState<'splash' | 'fading' | 'done'>('splash')

  useEffect(() => {
    const fadeTimer = setTimeout(() => setSplash('fading'), 4400)
    const doneTimer = setTimeout(() => setSplash('done'),  5000)
    return () => { clearTimeout(fadeTimer); clearTimeout(doneTimer) }
  }, [])

  // Fetch tenant display name when subdomain is known from URL
  useEffect(() => {
    if (!urlSubdomain) return
    fetch('/api/v1/tenants/info', {
      headers: { 'X-Tenant-Subdomain': urlSubdomain },
    })
      .then((r) => (r.ok ? r.json() : null))
      .then((data) => { if (data?.name) setTenantName(data.displayName || data.name) })
      .catch(() => {})
  }, [])

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const res = await authApi.login({ email, password, tenantSubdomain: subdomain })
      if (res.mfaPending) {
        const dest = res.mfaSetupRequired ? '/mfa/setup' : '/mfa/verify'
        navigate(dest, {
          replace: true,
          state: { preAuthToken: res.preAuthToken, subdomain, email: res.email },
        })
        return
      }
      setAuth(res.token, res.agentId, subdomain, res.role, res.firstName, res.lastName)
      if (res.sipExtension && res.sipPassword)
        setSipCredentials(res.sipExtension, res.sipPassword)
      const dest = res.role === 'admin' || res.role === 'supervisor' ? '/admin' : '/agent'
      navigate(dest, { replace: true })
    } catch {
      setError('Invalid credentials. Please try again.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-950">

      {/* ── Splash screen ── */}
      {splash !== 'done' && (
        <div
          className="fixed inset-0 z-50 flex flex-col items-center justify-center bg-gray-950 gap-6"
          style={{
            transition: 'opacity 0.6s ease',
            opacity: splash === 'fading' ? 0 : 1,
            pointerEvents: 'none',
          }}
        >
          <img src="/cc-logo-dark.svg" alt="Contact Connection" className="h-24" />
          <p className="text-white text-2xl font-light tracking-wide">Call Center Solutions, LLC</p>
          <div className="flex flex-col items-center gap-1 mt-2">
            <p className="text-gray-400 text-sm tracking-wide">
              <span style={{ color: '#38BDF8' }}>CEO:</span> William Soper
            </p>
            <p className="text-gray-400 text-sm tracking-wide">
              <span style={{ color: '#38BDF8' }}>Engineer:</span> Stephen Soper
            </p>
          </div>
        </div>
      )}

      {/* ── Login card ── */}
      <div
        className="w-full max-w-sm bg-gray-900 rounded-2xl shadow-xl p-8"
        style={{
          transition: 'opacity 0.6s ease',
          opacity: splash === 'done' ? 1 : 0,
        }}
      >
        <div className="flex flex-col items-center mb-7">
          <img src="/cc-logo-dark.svg" alt="Contact Connection" className="h-14 mb-4" />
          {tenantName ? (
            <>
              <p className="text-gray-400 text-xs uppercase tracking-widest mb-1">Sign in to</p>
              <p className="text-white font-semibold text-lg">{tenantName}</p>
            </>
          ) : (
            <p className="text-white font-medium" style={{ fontSize: 18 }}>Sign In</p>
          )}
        </div>

        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
          {/* Subdomain field shown only in dev (localhost) — hidden when URL subdomain is detected */}
          {!urlSubdomain && (
            <div>
              <label className="block mb-1" style={{ color: '#38BDF8', fontSize: 16 }}>Tenant subdomain</label>
              <input
                type="text"
                required
                value={subdomain}
                onChange={(e) => setSubdomain(e.target.value)}
                className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
          )}
          <div>
            <label className="block mb-1" style={{ color: '#38BDF8', fontSize: 16 }}>Email</label>
            <input
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>
          <div>
            <label className="block mb-1" style={{ color: '#38BDF8', fontSize: 16 }}>Password</label>
            <input
              type="password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>

          {error && (
            <p className="text-red-400 text-xs">{error}</p>
          )}

          <button
            type="submit"
            disabled={loading}
            className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
          >
            {loading ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </div>
    </div>
  )
}
