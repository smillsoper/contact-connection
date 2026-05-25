import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'
import {
  getOnboardingInfo,
  completeOnboarding,
  type OnboardingInfo,
  type OnboardingFeatureFlags,
  type CompleteOnboardingRequest,
} from '../api/onboarding'
import { TIMEZONES } from '../utils/timezones'

const STEPS = [
  'Your Account',
  'Identity & Branding',
  'Locale & Regional',
  'Communication',
  'Security',
  'Features',
  'Additional Admins',
]

const FLAG_META: { key: keyof OnboardingFeatureFlags; label: string; desc: string }[] = [
  { key: 'telephony',     label: 'Telephony',             desc: 'Voice call handling, softphone, and call recording' },
  { key: 'omsBuiltIn',   label: 'Commerce Engine (OMS)', desc: 'Order management, product catalog, and cart' },
  { key: 'shopifyAdapter', label: 'Shopify Adapter',      desc: 'Sync orders and customers from Shopify' },
  { key: 'tenantChat',   label: 'Tenant Chat',            desc: 'Internal messaging for agents and supervisors' },
]

export default function OnboardingPage() {
  const { token } = useParams<{ token: string }>()
  const navigate = useNavigate()
  const setAuth = useAuthStore((s) => s.setAuth)

  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<OnboardingInfo | null>(null)
  const [step, setStep] = useState(0)
  const [submitting, setSubmitting] = useState(false)
  const [stepError, setStepError] = useState<string | null>(null)

  // Step 0 — Account
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [password, setPassword] = useState('')
  const [passwordConfirm, setPasswordConfirm] = useState('')

  // Step 1 — Identity & Branding
  const [displayName, setDisplayName] = useState('')
  const [logoUrl, setLogoUrl] = useState('')

  // Step 2 — Locale & Regional
  const [timezone, setTimezone] = useState('America/New_York')
  const [dateFormat, setDateFormat] = useState('MM/DD/YYYY')
  const [timeFormat, setTimeFormat] = useState('12h')

  // Step 3 — Communication
  const [supportEmail, setSupportEmail] = useState('')
  const [billingEmail, setBillingEmail] = useState('')

  // Step 4 — Security
  const [sessionTimeout, setSessionTimeout] = useState(480)
  const [mfaRequirement, setMfaRequirement] = useState('off')

  // Step 5 — Features
  const [flags, setFlags] = useState<OnboardingFeatureFlags>({
    telephony: false,
    omsBuiltIn: false,
    shopifyAdapter: false,
    tenantChat: false,
  })

  // Step 6 — Additional Admins
  const [adminEmails, setAdminEmails] = useState<string[]>([''])

  useEffect(() => {
    if (!token) return
    getOnboardingInfo(token)
      .then((data) => {
        setInfo(data)
        setTimezone(data.timezone)
        setDisplayName(data.tenantName)
        setFlags(data.featureFlags)
        setLoading(false)
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : 'Invalid invitation.')
        setLoading(false)
      })
  }, [token])

  function validateStep(): string | null {
    if (step === 0) {
      if (!firstName.trim()) return 'First name is required.'
      if (!lastName.trim()) return 'Last name is required.'
      if (password.length < 8) return 'Password must be at least 8 characters.'
      if (password !== passwordConfirm) return 'Passwords do not match.'
    }
    if (step === 3) {
      if (supportEmail && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(supportEmail))
        return 'Support email is not a valid email address.'
      if (billingEmail && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(billingEmail))
        return 'Billing email is not a valid email address.'
    }
    return null
  }

  function handleNext() {
    const err = validateStep()
    if (err) { setStepError(err); return }
    setStepError(null)
    setStep((s) => s + 1)
  }

  function handleBack() {
    setStepError(null)
    setStep((s) => s - 1)
  }

  function addAdminEmail() {
    setAdminEmails((prev) => [...prev, ''])
  }

  function updateAdminEmail(index: number, value: string) {
    setAdminEmails((prev) => prev.map((e, i) => (i === index ? value : e)))
  }

  function removeAdminEmail(index: number) {
    setAdminEmails((prev) => prev.filter((_, i) => i !== index))
  }

  async function handleComplete() {
    setSubmitting(true)
    setStepError(null)
    try {
      const validAdminEmails = adminEmails
        .map((e) => e.trim())
        .filter((e) => e.length > 0 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(e))

      const payload: CompleteOnboardingRequest = {
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        password,
        displayName: displayName.trim() || undefined,
        logoUrl: logoUrl.trim() || undefined,
        dateFormat,
        timeFormat,
        supportEmail: supportEmail.trim() || undefined,
        billingEmail: billingEmail.trim() || undefined,
        sessionTimeoutMinutes: sessionTimeout,
        mfaRequirement,
        featureFlags: flags,
        additionalAdminEmails: validAdminEmails.length > 0 ? validAdminEmails : undefined,
      }
      const result = await completeOnboarding(token!, payload)
      setAuth(result.token, result.agent.id, result.agent.tenantSubdomain, result.agent.role, result.agent.firstName, result.agent.lastName)
      navigate('/admin', { replace: true })
    } catch (err) {
      setStepError(err instanceof Error ? err.message : 'Setup failed. Please try again.')
    } finally {
      setSubmitting(false)
    }
  }

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
      <div className="w-full max-w-xl">

        {/* Header */}
        <div className="text-center mb-8">
          <p className="text-gray-500 text-xs font-semibold tracking-widest uppercase mb-1">
            ContactConnection
          </p>
          <h1 className="text-white text-2xl font-bold">Welcome to {info.tenantName}</h1>
          <p className="text-gray-400 text-sm mt-1">Let's get your workspace set up.</p>
        </div>

        {/* Step indicator */}
        <div className="flex items-center justify-between mb-6 px-1">
          {STEPS.map((label, i) => (
            <div key={i} className="flex flex-col items-center gap-1 flex-1">
              <div
                className={`w-7 h-7 rounded-full flex items-center justify-center text-xs font-semibold transition-colors ${
                  i < step
                    ? 'bg-indigo-600 text-white'
                    : i === step
                    ? 'bg-indigo-500 text-white ring-2 ring-indigo-400'
                    : 'bg-gray-800 text-gray-500'
                }`}
              >
                {i < step ? '✓' : i + 1}
              </div>
              <span className={`text-xs hidden sm:block ${i === step ? 'text-indigo-400' : 'text-gray-600'}`}>
                {label}
              </span>
            </div>
          ))}
        </div>

        {/* Card */}
        <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
          <h2 className="text-white font-semibold text-lg mb-5">{STEPS[step]}</h2>

          {/* Step 0 — Your Account */}
          {step === 0 && (
            <div className="flex flex-col gap-4">
              <p className="text-gray-400 text-sm">
                You're setting up <strong className="text-gray-200">{info.tenantName}</strong>.
                Your account email will be <strong className="text-indigo-400">{info.email}</strong>.
              </p>
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
          )}

          {/* Step 1 — Identity & Branding */}
          {step === 1 && (
            <div className="flex flex-col gap-4">
              <div>
                <label className="block mb-1.5 text-sky-400 text-xs font-medium">Display name</label>
                <input
                  type="text"
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                  placeholder={info.tenantName}
                  className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
                <p className="text-gray-500 text-xs mt-1">The name shown to agents and customers. Defaults to the tenant name.</p>
              </div>
              <div>
                <label className="block mb-1.5 text-sky-400 text-xs font-medium">Logo URL <span className="text-gray-500 font-normal">(optional)</span></label>
                <input
                  type="url"
                  value={logoUrl}
                  onChange={(e) => setLogoUrl(e.target.value)}
                  placeholder="https://example.com/logo.png"
                  className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
                <p className="text-gray-500 text-xs mt-1">Direct URL to a PNG or SVG. Can be updated from settings later.</p>
              </div>
              {logoUrl && (
                <div className="flex items-center gap-3 p-3 bg-gray-800 rounded-lg">
                  <img
                    src={logoUrl}
                    alt="Logo preview"
                    className="h-10 w-auto max-w-[120px] object-contain rounded"
                    onError={(e) => { (e.target as HTMLImageElement).style.display = 'none' }}
                  />
                  <span className="text-gray-400 text-xs">Logo preview</span>
                </div>
              )}
            </div>
          )}

          {/* Step 2 — Locale & Regional */}
          {step === 2 && (
            <div className="flex flex-col gap-4">
              <div>
                <label className="block mb-1.5 text-sky-400 text-xs font-medium">Timezone</label>
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
              <div>
                <label className="block mb-2 text-sky-400 text-xs font-medium">Date format</label>
                <div className="flex gap-3">
                  {['MM/DD/YYYY', 'DD/MM/YYYY'].map((fmt) => (
                    <label key={fmt} className="flex items-center gap-2 cursor-pointer">
                      <input
                        type="radio"
                        name="dateFormat"
                        checked={dateFormat === fmt}
                        onChange={() => setDateFormat(fmt)}
                        className="accent-indigo-500"
                      />
                      <span className="text-gray-300 text-sm">{fmt}</span>
                    </label>
                  ))}
                </div>
              </div>
              <div>
                <label className="block mb-2 text-sky-400 text-xs font-medium">Time format</label>
                <div className="flex gap-3">
                  {[{ value: '12h', label: '12-hour (1:30 PM)' }, { value: '24h', label: '24-hour (13:30)' }].map((opt) => (
                    <label key={opt.value} className="flex items-center gap-2 cursor-pointer">
                      <input
                        type="radio"
                        name="timeFormat"
                        checked={timeFormat === opt.value}
                        onChange={() => setTimeFormat(opt.value)}
                        className="accent-indigo-500"
                      />
                      <span className="text-gray-300 text-sm">{opt.label}</span>
                    </label>
                  ))}
                </div>
              </div>
            </div>
          )}

          {/* Step 3 — Communication */}
          {step === 3 && (
            <div className="flex flex-col gap-4">
              <div>
                <label className="block mb-1.5 text-sky-400 text-xs font-medium">Support contact email <span className="text-gray-500 font-normal">(optional)</span></label>
                <input
                  type="email"
                  value={supportEmail}
                  onChange={(e) => setSupportEmail(e.target.value)}
                  placeholder="support@yourcompany.com"
                  className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
                <p className="text-gray-500 text-xs mt-1">Shown to customers for support inquiries.</p>
              </div>
              <div>
                <label className="block mb-1.5 text-sky-400 text-xs font-medium">Billing contact email <span className="text-gray-500 font-normal">(optional)</span></label>
                <input
                  type="email"
                  value={billingEmail}
                  onChange={(e) => setBillingEmail(e.target.value)}
                  placeholder="billing@yourcompany.com"
                  className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
                <p className="text-gray-500 text-xs mt-1">Used for platform billing correspondence.</p>
              </div>
            </div>
          )}

          {/* Step 4 — Security */}
          {step === 4 && (
            <div className="flex flex-col gap-4">
              <div>
                <label className="block mb-1.5 text-sky-400 text-xs font-medium">Session timeout</label>
                <select
                  value={sessionTimeout}
                  onChange={(e) => setSessionTimeout(Number(e.target.value))}
                  className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                >
                  <option value={60}>1 hour</option>
                  <option value={240}>4 hours</option>
                  <option value={480}>8 hours</option>
                  <option value={720}>12 hours</option>
                  <option value={1440}>24 hours</option>
                </select>
                <p className="text-gray-500 text-xs mt-1">Agents will be signed out after this period of inactivity.</p>
              </div>
              <div>
                <label className="block mb-2 text-sky-400 text-xs font-medium">MFA requirement</label>
                <div className="flex flex-col gap-2">
                  {[
                    { value: 'off', label: 'Off', desc: 'MFA is not required' },
                    { value: 'optional', label: 'Optional', desc: 'Agents can enable MFA for their account' },
                    { value: 'on', label: 'Required', desc: 'All agents must use MFA to sign in' },
                  ].map((opt) => (
                    <label key={opt.value} className="flex items-start gap-3 cursor-pointer group">
                      <input
                        type="radio"
                        name="mfaRequirement"
                        checked={mfaRequirement === opt.value}
                        onChange={() => setMfaRequirement(opt.value)}
                        className="accent-indigo-500 mt-0.5"
                      />
                      <div>
                        <span className="text-gray-200 text-sm font-medium">{opt.label}</span>
                        <span className="text-gray-500 text-xs ml-2">{opt.desc}</span>
                      </div>
                    </label>
                  ))}
                </div>
              </div>
            </div>
          )}

          {/* Step 5 — Features */}
          {step === 5 && (
            <div className="flex flex-col gap-3">
              <p className="text-gray-400 text-sm mb-1">
                These features were selected during provisioning. You can adjust them now or change them later from the admin portal.
              </p>
              {FLAG_META.map(({ key, label, desc }) => (
                <label
                  key={key}
                  className="flex items-center gap-4 p-3 rounded-lg bg-gray-800/60 cursor-pointer group hover:bg-gray-800 transition-colors"
                  onClick={() => setFlags((prev) => ({ ...prev, [key]: !prev[key] }))}
                >
                  <div
                    className={`w-10 h-6 rounded-full relative flex-shrink-0 transition-colors ${
                      flags[key] ? 'bg-indigo-600' : 'bg-gray-600'
                    }`}
                  >
                    <div
                      className={`absolute top-1 w-4 h-4 bg-white rounded-full shadow transition-transform ${
                        flags[key] ? 'translate-x-5' : 'translate-x-1'
                      }`}
                    />
                  </div>
                  <div>
                    <p className="text-gray-200 text-sm font-medium group-hover:text-white transition-colors">{label}</p>
                    <p className="text-gray-500 text-xs">{desc}</p>
                  </div>
                </label>
              ))}
            </div>
          )}

          {/* Step 6 — Additional Admins */}
          {step === 6 && (
            <div className="flex flex-col gap-4">
              <p className="text-gray-400 text-sm">
                Optionally invite additional administrators now. Each person will receive a setup email
                to create their account. You can also do this from the Administration panel after setup.
              </p>
              {adminEmails.map((email, i) => (
                <div key={i} className="flex gap-2">
                  <input
                    type="email"
                    value={email}
                    onChange={(e) => updateAdminEmail(i, e.target.value)}
                    placeholder="admin@yourcompany.com"
                    className="flex-1 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                  />
                  {adminEmails.length > 1 && (
                    <button
                      type="button"
                      onClick={() => removeAdminEmail(i)}
                      className="text-gray-500 hover:text-red-400 px-2 transition-colors text-sm"
                    >
                      ✕
                    </button>
                  )}
                </div>
              ))}
              <button
                type="button"
                onClick={addAdminEmail}
                className="text-indigo-400 hover:text-indigo-300 text-sm self-start transition-colors"
              >
                + Add another
              </button>
            </div>
          )}

          {/* Error */}
          {stepError && (
            <p className="text-red-400 text-sm mt-4">{stepError}</p>
          )}

          {/* Navigation */}
          <div className="flex items-center justify-between mt-6 pt-5 border-t border-gray-800">
            <button
              type="button"
              onClick={handleBack}
              disabled={step === 0}
              className="text-gray-400 hover:text-white disabled:opacity-30 disabled:cursor-not-allowed text-sm transition-colors"
            >
              ← Back
            </button>
            {step < STEPS.length - 1 ? (
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
                {submitting ? 'Setting up…' : 'Complete Setup'}
              </button>
            )}
          </div>
        </div>

        <p className="text-center text-gray-600 text-xs mt-4">
          Invitation sent to {info.email} · Expires {new Date(info.expiresAt).toLocaleDateString()}
        </p>
      </div>
    </div>
  )
}
