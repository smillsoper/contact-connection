import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'
import { getTenantAdminInviteInfo, acceptTenantAdminInvite, type TenantAdminInviteInfo } from '../api/adminInvite'

export default function TenantAdminInviteAcceptPage() {
  const { token } = useParams<{ token: string }>()
  const navigate = useNavigate()
  const setAuth = useAuthStore((s) => s.setAuth)

  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<TenantAdminInviteInfo | null>(null)

  const [step, setStep] = useState(0)
  const [stepError, setStepError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [password, setPassword] = useState('')
  const [passwordConfirm, setPasswordConfirm] = useState('')

  const showMfaStep = info?.mfaRequirement !== 'off'
  const totalSteps = showMfaStep ? 2 : 1

  useEffect(() => {
    if (!token) return
    getTenantAdminInviteInfo(token)
      .then((data) => { setInfo(data); setLoading(false) })
      .catch((err) => {
        setError(err instanceof Error ? err.message : 'Invalid invitation.')
        setLoading(false)
      })
  }, [token])

  function validateAccount(): string | null {
    if (!firstName.trim()) return 'First name is required.'
    if (!lastName.trim()) return 'Last name is required.'
    if (password.length < 8) return 'Password must be at least 8 characters.'
    if (password !== passwordConfirm) return 'Passwords do not match.'
    return null
  }

  function handleNext() {
    if (step === 0) {
      const err = validateAccount()
      if (err) { setStepError(err); return }
    }
    setStepError(null)
    setStep((s) => s + 1)
  }

  async function handleComplete() {
    if (step === 0) {
      const err = validateAccount()
      if (err) { setStepError(err); return }
    }
    setSubmitting(true)
    setStepError(null)
    try {
      const result = await acceptTenantAdminInvite(token!, { firstName: firstName.trim(), lastName: lastName.trim(), password })
      setAuth(result.token, result.agent.id, result.agent.tenantSubdomain, result.agent.role, result.agent.firstName, result.agent.lastName)
      navigate('/admin', { replace: true })
    } catch (err) {
      setStepError(err instanceof Error ? err.message : 'Setup failed. Please try again.')
    } finally {
      setSubmitting(false)
    }
  }

  const tenantLogo = info?.tenantLogoUrl
  const tenantLabel = info?.tenantDisplayName ?? info?.tenantName ?? 'ContactConnection'

  if (loading) {
    return (
      <div className="min-h-screen bg-gray-950 flex items-center justify-center">
        <p className="text-gray-400 text-sm">Validating invitation…</p>
      </div>
    )
  }

  if (error || !info) {
    return (
      <div className="min-h-screen bg-gray-950 flex items-center justify-center p-6">
        <div className="bg-gray-900 border border-gray-800 rounded-xl p-8 max-w-md w-full text-center">
          <p className="text-red-400 font-medium mb-2">Invitation Invalid</p>
          <p className="text-gray-400 text-sm">{error ?? 'This link is no longer valid.'}</p>
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-gray-950 flex flex-col items-center justify-center p-6">
      <div className="w-full max-w-md">

        {/* Logo + header */}
        <div className="text-center mb-8">
          {tenantLogo ? (
            <img
              src={tenantLogo}
              alt={tenantLabel}
              className="h-12 w-auto mx-auto mb-4 object-contain"
              onError={(e) => {
                (e.target as HTMLImageElement).src = '/cc-logo-dark.svg'
              }}
            />
          ) : (
            <img src="/cc-logo-dark.svg" alt="ContactConnection" className="h-12 w-auto mx-auto mb-4" />
          )}
          <p className="text-gray-500 text-xs font-semibold tracking-widest uppercase mb-1">
            {tenantLabel}
          </p>
          <h1 className="text-white text-xl font-bold">Set up your admin account</h1>
          <p className="text-gray-400 text-sm mt-1">
            You've been invited as an administrator for <strong className="text-gray-200">{info.tenantName}</strong>.
          </p>
        </div>

        {/* Step dots */}
        {totalSteps > 1 && (
          <div className="flex items-center justify-center gap-2 mb-6">
            {Array.from({ length: totalSteps }).map((_, i) => (
              <div
                key={i}
                className={`rounded-full transition-all ${
                  i === step ? 'w-6 h-2 bg-indigo-500' : i < step ? 'w-2 h-2 bg-indigo-600' : 'w-2 h-2 bg-gray-700'
                }`}
              />
            ))}
          </div>
        )}

        {/* Card */}
        <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">

          {/* Step 0 — Account setup */}
          {step === 0 && (
            <>
              <h2 className="text-white font-semibold text-lg mb-4">Your Account</h2>
              <p className="text-gray-400 text-sm mb-4">
                Your sign-in email will be <strong className="text-indigo-400">{info.email}</strong>.
              </p>
              <div className="flex flex-col gap-4">
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="block mb-1.5 text-sky-400 text-xs font-medium">First name</label>
                    <input
                      type="text"
                      value={firstName}
                      onChange={(e) => setFirstName(e.target.value)}
                      placeholder="Jane"
                      className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                    />
                  </div>
                  <div>
                    <label className="block mb-1.5 text-sky-400 text-xs font-medium">Last name</label>
                    <input
                      type="text"
                      value={lastName}
                      onChange={(e) => setLastName(e.target.value)}
                      placeholder="Smith"
                      className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                    />
                  </div>
                </div>
                <div>
                  <label className="block mb-1.5 text-sky-400 text-xs font-medium">Password</label>
                  <input
                    type="password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="At least 8 characters"
                    className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                  />
                </div>
                <div>
                  <label className="block mb-1.5 text-sky-400 text-xs font-medium">Confirm password</label>
                  <input
                    type="password"
                    value={passwordConfirm}
                    onChange={(e) => setPasswordConfirm(e.target.value)}
                    placeholder="Re-enter password"
                    className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                  />
                </div>
              </div>
            </>
          )}

          {/* Step 1 — MFA (if applicable) */}
          {step === 1 && showMfaStep && (
            <>
              <h2 className="text-white font-semibold text-lg mb-4">Multi-Factor Authentication</h2>
              {info.mfaRequirement === 'on' ? (
                <div className="bg-amber-900/20 border border-amber-800/40 rounded-lg p-4 mb-4">
                  <p className="text-amber-300 text-sm font-medium mb-1">MFA Required</p>
                  <p className="text-gray-400 text-sm">
                    Your workspace requires multi-factor authentication. You'll be prompted to complete MFA setup
                    the first time you sign in to the admin portal.
                  </p>
                </div>
              ) : (
                <div className="bg-indigo-900/20 border border-indigo-800/40 rounded-lg p-4 mb-4">
                  <p className="text-indigo-300 text-sm font-medium mb-1">MFA Available</p>
                  <p className="text-gray-400 text-sm">
                    Your workspace supports optional multi-factor authentication. You can enable it from your
                    profile settings after signing in.
                  </p>
                </div>
              )}
              <p className="text-gray-500 text-xs">
                MFA setup via authenticator app will be available in your profile settings.
              </p>
            </>
          )}

          {/* Error */}
          {stepError && <p className="text-red-400 text-sm mt-4">{stepError}</p>}

          {/* Navigation */}
          <div className="flex items-center justify-between mt-6 pt-5 border-t border-gray-800">
            {step > 0 ? (
              <button
                type="button"
                onClick={() => { setStepError(null); setStep((s) => s - 1) }}
                className="text-gray-400 hover:text-white text-sm transition-colors"
              >
                ← Back
              </button>
            ) : (
              <span />
            )}

            {step < totalSteps - 1 ? (
              <button
                type="button"
                onClick={handleNext}
                className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
              >
                Continue →
              </button>
            ) : (
              <button
                type="button"
                onClick={handleComplete}
                disabled={submitting}
                className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
              >
                {submitting ? 'Setting up…' : 'Enter Admin Portal'}
              </button>
            )}
          </div>
        </div>

        <p className="text-center text-gray-600 text-xs mt-4">
          Invitation expires {new Date(info.expiresAt).toLocaleDateString()}
        </p>
      </div>
    </div>
  )
}
