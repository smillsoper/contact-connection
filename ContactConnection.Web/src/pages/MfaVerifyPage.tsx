import { useState, type FormEvent } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { authApi } from '../api/auth'
import { useAuthStore } from '../stores/authStore'
import { useSipStore } from '../stores/sipStore'

interface LocationState {
  preAuthToken: string
  subdomain: string
  email: string
}

export default function MfaVerifyPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const setAuth = useAuthStore((s) => s.setAuth)
  const setSipCredentials = useSipStore((s) => s.setSipCredentials)

  const state = location.state as LocationState | null
  if (!state?.preAuthToken || !state?.subdomain) {
    navigate('/login', { replace: true })
    return null
  }

  const { preAuthToken, subdomain, email } = state

  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const res = await authApi.mfaVerify(preAuthToken, subdomain, code)
      setAuth(res.token, res.agentId, subdomain, res.role, res.firstName, res.lastName)
      if (res.sipExtension && res.sipPassword)
        setSipCredentials(res.sipExtension, res.sipPassword)
      const dest = res.role === 'admin' || res.role === 'supervisor' ? '/admin' : '/agent'
      navigate(dest, { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Invalid code. Please try again.')
      setCode('')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-950">
      <div className="w-full max-w-sm bg-gray-900 rounded-2xl shadow-xl p-8">
        <div className="flex flex-col items-center mb-7">
          <img src="/cc-logo-dark.svg" alt="ContactConnection" className="h-12 mb-4" />
          <p className="text-white font-medium text-lg">Two-Factor Verification</p>
          <p className="text-gray-400 text-xs mt-1 text-center">
            Enter the 6-digit code from your authenticator app
          </p>
        </div>

        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
          <div>
            <label className="block mb-1 text-sm" style={{ color: '#38BDF8' }}>
              Verification code
            </label>
            <input
              type="text"
              inputMode="numeric"
              maxLength={6}
              required
              autoFocus
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
            {loading ? 'Verifying…' : 'Verify'}
          </button>
        </form>

        <p className="text-gray-600 text-xs text-center mt-4">
          Signed in as <span className="text-gray-400">{email}</span>
        </p>
        <p className="text-gray-700 text-xs text-center mt-2">
          <button
            className="hover:text-gray-500 transition-colors"
            onClick={() => navigate('/login', { replace: true })}
          >
            Back to login
          </button>
        </p>
      </div>
    </div>
  )
}
