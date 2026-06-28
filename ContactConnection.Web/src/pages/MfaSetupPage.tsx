import { useState, useEffect, type FormEvent } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import QRCode from 'qrcode'
import { authApi } from '../api/auth'
import { useAuthStore, getLandingRoute } from '../stores/authStore'
import { useSipStore } from '../stores/sipStore'

interface LocationState {
  preAuthToken: string
  subdomain: string
  email: string
}

export default function MfaSetupPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const setAuth = useAuthStore((s) => s.setAuth)
  const setSipCredentials = useSipStore((s) => s.setSipCredentials)

  const state = location.state as LocationState | null
  const { preAuthToken, subdomain, email } = state ?? {}

  const [qrUrl, setQrUrl] = useState<string | null>(null)
  const [secret, setSecret] = useState<string | null>(null)
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [loadingSetup, setLoadingSetup] = useState(true)

  useEffect(() => {
    if (!preAuthToken || !subdomain) {
      navigate('/login', { replace: true })
      return
    }

    authApi.mfaSetup(preAuthToken, subdomain)
      .then(async (data) => {
        setSecret(data.secret)
        const url = await QRCode.toDataURL(data.otpAuthUri, { width: 200, margin: 2 })
        setQrUrl(url)
      })
      .catch(() => navigate('/login', { replace: true }))
      .finally(() => setLoadingSetup(false))
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (!preAuthToken || !subdomain) return
    setError(null)
    setLoading(true)
    try {
      const res = await authApi.mfaSetupConfirm(preAuthToken, subdomain, code)
      setAuth(res.token, res.agentId, subdomain, res.role, res.firstName, res.lastName, res.permissions ?? [], res.landingPage ?? undefined)
      if (res.sipExtension && res.sipPassword)
        setSipCredentials(res.sipExtension, res.sipPassword)
      navigate(getLandingRoute(res.landingPage ?? null), { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Invalid code. Please try again.')
    } finally {
      setLoading(false)
    }
  }

  if (loadingSetup) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-950">
        <p className="text-gray-400 text-sm">Loading…</p>
      </div>
    )
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-950">
      <div className="w-full max-w-sm bg-gray-900 rounded-2xl shadow-xl p-8">
        <div className="flex flex-col items-center mb-6">
          <img src="/cc-logo-dark.svg" alt="ContactConnection" className="h-12 mb-4" />
          <p className="text-white font-medium text-lg">Set Up Two-Factor Auth</p>
          <p className="text-gray-400 text-xs mt-1 text-center">
            Scan the QR code with an authenticator app (Google Authenticator, Authy, etc.)
          </p>
        </div>

        {qrUrl && (
          <div className="flex justify-center mb-4">
            <div className="bg-white p-2 rounded-lg">
              <img src={qrUrl} alt="MFA QR Code" className="w-40 h-40" />
            </div>
          </div>
        )}

        {secret && (
          <div className="mb-5 text-center">
            <p className="text-gray-500 text-xs mb-1">Or enter the key manually:</p>
            <code className="text-indigo-400 text-xs tracking-widest select-all break-all">{secret}</code>
          </div>
        )}

        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
          <div>
            <label className="block mb-1 text-sm" style={{ color: '#38BDF8' }}>
              Enter the 6-digit code from your app
            </label>
            <input
              type="text"
              inputMode="numeric"
              maxLength={6}
              required
              autoComplete="one-time-code"
              value={code}
              onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 tracking-widest text-center"
              placeholder="000000"
            />
          </div>

          {error && <p className="text-red-400 text-xs">{error}</p>}

          <button
            type="submit"
            disabled={loading || code.length !== 6}
            className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
          >
            {loading ? 'Verifying…' : 'Enable & Continue'}
          </button>
        </form>

        <p className="text-gray-600 text-xs text-center mt-4">
          Signed in as <span className="text-gray-400">{email}</span>
        </p>
      </div>
    </div>
  )
}
