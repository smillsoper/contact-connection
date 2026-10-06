import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { useLocation, useNavigate, useParams, Link } from 'react-router-dom'
import QRCode from 'qrcode'
import { clientPortalApi, clientSubdomain, type ClientAuthResponse, type ClientInviteInfo } from '../../api/clientPortal'
import { useClientAuthStore } from '../../stores/clientAuthStore'
import { getSubdomainFromHostname } from '../../utils/subdomain'

// Client portal sign-in (S181): its own pages, its own session — client users are never agents.

const input = 'w-full bg-gray-900 border border-gray-700 rounded-lg px-3 py-2 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-indigo-500'
const button = 'w-full bg-indigo-600 hover:bg-indigo-500 text-white text-sm font-medium py-2 rounded-lg disabled:opacity-50 transition-colors'

function Card({ title, subtitle, children }: { title: string; subtitle?: string; children: ReactNode }) {
  return (
    <div className="min-h-screen bg-gray-950 flex items-center justify-center px-4 py-10">
      <div className="w-full max-w-sm bg-gray-900 border border-gray-800 rounded-xl p-6 shadow-xl">
        <img src="/hubion-favicon.svg" alt="" className="w-10 h-10 mx-auto mb-3" />
        <h1 className="text-lg font-semibold text-white text-center">{title}</h1>
        {subtitle && <p className="text-xs text-gray-400 text-center mt-1">{subtitle}</p>}
        <div className="mt-5">{children}</div>
      </div>
    </div>
  )
}

interface MfaState { preAuthToken: string; setupRequired: boolean; subdomain: string }

/** Finishes a sign-in: a session, or on to the two-step page. */
function useFinish() {
  const navigate = useNavigate()
  const setAuth = useClientAuthStore((s) => s.setAuth)
  return (res: ClientAuthResponse, subdomain: string) => {
    if (res.mfaPending && res.preAuthToken) {
      navigate('/client/mfa', { state: { preAuthToken: res.preAuthToken, setupRequired: !!res.mfaSetupRequired, subdomain } satisfies MfaState })
    } else if (res.token && res.user) {
      setAuth(res.token, subdomain, res.user)
      navigate('/client', { replace: true })
    }
  }
}

export function ClientLoginPage() {
  const urlSubdomain = getSubdomainFromHostname()
  const stored = useClientAuthStore((s) => s.subdomain)
  const [subdomain, setSubdomain] = useState(urlSubdomain ?? stored ?? '')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const finish = useFinish()

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true); setError(null)
    try {
      // The subdomain the API should resolve: the URL's in production; the typed one in local dev.
      useClientAuthStore.setState({ subdomain })
      finish(await clientPortalApi.login(email, password), subdomain)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Sign-in failed')
    } finally { setBusy(false) }
  }

  return (
    <Card title="Client dashboards" subtitle="Sign in to see your reporting">
      <form onSubmit={submit} className="space-y-3">
        {!urlSubdomain && (
          <input className={input} placeholder="Organization (subdomain)" value={subdomain} onChange={(e) => setSubdomain(e.target.value.trim())} />
        )}
        <input className={input} type="email" autoComplete="username" placeholder="Email" value={email} onChange={(e) => setEmail(e.target.value)} />
        <input className={input} type="password" autoComplete="current-password" placeholder="Password" value={password} onChange={(e) => setPassword(e.target.value)} />
        {error && <p className="text-xs text-red-400">{error}</p>}
        <button className={button} disabled={busy || !email || !password || !subdomain}>{busy ? 'Signing in…' : 'Sign in'}</button>
        <p className="text-[11px] text-gray-500 text-center">Forgot your password? Ask your account contact to send you a new link.</p>
      </form>
    </Card>
  )
}

export function ClientInvitePage() {
  const { token = '' } = useParams<{ token: string }>()
  const [info, setInfo] = useState<ClientInviteInfo | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [busy, setBusy] = useState(false)
  const finish = useFinish()

  useEffect(() => {
    clientPortalApi.invite(token)
      .then((i) => { setInfo(i); setFirstName(i.firstName); setLastName(i.lastName) })
      .catch((e) => setError(e instanceof Error ? e.message : 'This link is invalid or has expired.'))
  }, [token])

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!info) return
    if (password !== confirm) { setError("The passwords don't match."); return }
    setBusy(true); setError(null)
    try {
      const sub = clientSubdomain() ?? ''
      useClientAuthStore.setState({ subdomain: sub })
      finish(await clientPortalApi.acceptInvite(token, { firstName, lastName, password }), sub)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not set your password')
    } finally { setBusy(false) }
  }

  if (!info) {
    return (
      <Card title="Client dashboards">
        <p className="text-sm text-center text-gray-400">{error ?? 'Checking your link…'}</p>
        {error && <p className="text-xs text-center mt-3"><Link className="text-indigo-300" to="/client/login">Go to sign in</Link></p>}
      </Card>
    )
  }

  return (
    <Card title={info.tenantName} subtitle={info.hasPassword ? 'Choose a new password' : 'Set up your dashboard account'}>
      <form onSubmit={submit} className="space-y-3">
        <p className="text-xs text-gray-400">Signing in as <span className="text-gray-200">{info.email}</span></p>
        {!info.hasPassword && (
          <div className="grid grid-cols-2 gap-2">
            <input className={input} placeholder="First name" value={firstName} onChange={(e) => setFirstName(e.target.value)} />
            <input className={input} placeholder="Last name" value={lastName} onChange={(e) => setLastName(e.target.value)} />
          </div>
        )}
        <input className={input} type="password" autoComplete="new-password" placeholder={`Password (at least ${info.minPasswordLength} characters)`}
          value={password} onChange={(e) => setPassword(e.target.value)} />
        <input className={input} type="password" autoComplete="new-password" placeholder="Confirm password" value={confirm} onChange={(e) => setConfirm(e.target.value)} />
        {error && <p className="text-xs text-red-400">{error}</p>}
        <button className={button} disabled={busy || password.length < info.minPasswordLength || !confirm}>
          {busy ? 'Saving…' : 'Save and sign in'}
        </button>
      </form>
    </Card>
  )
}

export function ClientMfaPage() {
  const navigate = useNavigate()
  const state = useLocation().state as MfaState | null
  const [qr, setQr] = useState<string | null>(null)
  const [secret, setSecret] = useState<string | null>(null)
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const finish = useFinish()

  useEffect(() => {
    if (!state) { navigate('/client/login', { replace: true }); return }
    if (!state.setupRequired) return
    clientPortalApi.mfaSetup(state.preAuthToken)
      .then(async (d) => { setSecret(d.secret); setQr(await QRCode.toDataURL(d.otpAuthUri, { width: 200, margin: 2 })) })
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not start setup — sign in again.'))
  }, [state, navigate])

  if (!state) return null

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true); setError(null)
    try {
      const res = state!.setupRequired
        ? await clientPortalApi.mfaSetupConfirm(state!.preAuthToken, code.trim())
        : await clientPortalApi.mfaVerify(state!.preAuthToken, code.trim())
      finish(res, state!.subdomain)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Invalid code')
    } finally { setBusy(false) }
  }

  return (
    <Card title="Two-step sign-in" subtitle={state.setupRequired ? 'Scan this with an authenticator app, then enter the 6-digit code' : 'Enter the 6-digit code from your authenticator app'}>
      <form onSubmit={submit} className="space-y-3">
        {state.setupRequired && qr && (
          <div className="flex flex-col items-center gap-2">
            <img src={qr} alt="Authenticator QR code" className="rounded bg-white" />
            {secret && <p className="text-[11px] text-gray-500 break-all text-center">Or enter this key: <span className="text-gray-300 font-mono">{secret}</span></p>}
          </div>
        )}
        <input className={`${input} text-center tracking-widest`} inputMode="numeric" autoComplete="one-time-code" maxLength={6} placeholder="123456"
          value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} autoFocus />
        {error && <p className="text-xs text-red-400">{error}</p>}
        <button className={button} disabled={busy || code.length !== 6}>{busy ? 'Checking…' : 'Continue'}</button>
        <p className="text-[11px] text-gray-500 text-center">The code expires — if it's been more than a few minutes, <Link className="text-indigo-300" to="/client/login">sign in again</Link>.</p>
      </form>
    </Card>
  )
}
