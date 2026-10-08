import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuthStore, getLandingRoute } from '../stores/authStore'
import { getSubdomainFromHostname } from '../utils/subdomain'

/**
 * ContactConnection support sign-in (S184). The Portal's "Open tenant portal" lands here with a single-use code in the
 * URL fragment; it's exchanged for the support session's token and the page goes to the admin dashboard.
 */
export default function SupportLoginPage() {
  const navigate = useNavigate()
  const done = useRef(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (done.current) return
    done.current = true
    const code = new URLSearchParams(window.location.hash.replace(/^#/, '')).get('code')
    const subdomain = getSubdomainFromHostname()
    // Drop the code from the address bar and history straight away.
    window.history.replaceState(null, '', window.location.pathname + window.location.search)
    if (!code || !subdomain) { setError('This support link is incomplete — open the tenant\'s portal again from the Platform Portal.'); return }

    fetch('/api/v1/auth/support-login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Tenant-Subdomain': subdomain },
      body: JSON.stringify({ code }),
    })
      .then(async (r) => {
        const body = await r.json().catch(() => null)
        if (!r.ok) throw new Error(body?.error ?? 'This support link has expired — open the tenant\'s portal again from the Platform Portal.')
        return body as {
          response: { token: string; agentId: string; role: string; firstName: string; lastName: string; permissions: string[]; landingPage: string }
          supportSession: { id: string; expiresAt: string; tenantName: string }
        }
      })
      .then(({ response: r, supportSession }) => {
        const auth = useAuthStore.getState()
        auth.setAuth(r.token, r.agentId, subdomain, r.role, r.firstName, r.lastName, r.permissions, r.landingPage)
        auth.setSupportSession(supportSession)
        navigate(getLandingRoute(r.landingPage), { replace: true })
      })
      .catch((e: Error) => setError(e.message))
  }, [navigate])

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-950 p-6">
      {error
        ? <p className="text-red-400 text-sm max-w-md text-center">{error}</p>
        : <div className="flex flex-col items-center gap-4">
            <div className="w-8 h-8 border-2 border-sky-500 border-t-transparent rounded-full animate-spin" />
            <p className="text-gray-400 text-sm">Opening the support session…</p>
          </div>}
    </div>
  )
}
