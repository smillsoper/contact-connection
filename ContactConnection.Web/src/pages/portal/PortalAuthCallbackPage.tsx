import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { msalInitialized, msalInstance } from '../../config/msal'
import { usePortalAuthStore } from '../../stores/portalAuthStore'

export default function PortalAuthCallbackPage() {
  const navigate = useNavigate()
  const setAuth = usePortalAuthStore((s) => s.setAuth)
  const attempted = useRef(false)

  useEffect(() => {
    if (attempted.current) return
    attempted.current = true

    async function process() {
      try {
        await msalInitialized

        // navigateToLoginRequestUrl: false — prevents MSAL from redirecting back to
        // the page where loginRedirect() was called (/portal/login) before resolving
        const result = await msalInstance.handleRedirectPromise({ navigateToLoginRequestUrl: false })

        if (!result?.idToken) {
          navigate('/portal/login', { replace: true })
          return
        }

        const res = await fetch('/api/v1/portal/auth/entra-login', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ idToken: result.idToken }),
        })

        if (!res.ok) {
          navigate('/portal/login?error=unauthorized', { replace: true })
          return
        }

        const data = await res.json() as {
          token: string; adminId: string; email: string
          firstName: string; lastName: string
        }
        setAuth(data.token, data.adminId, data.email, data.firstName, data.lastName)
        navigate('/portal/tenants', { replace: true })
      } catch (e) {
        console.error('Portal auth callback error:', e)
        navigate('/portal/login?error=failed', { replace: true })
      }
    }

    process()
  }, [navigate, setAuth])

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-950">
      <div className="flex flex-col items-center gap-4">
        <div className="w-8 h-8 border-2 border-indigo-500 border-t-transparent rounded-full animate-spin" />
        <p className="text-gray-400 text-sm">Signing you in…</p>
      </div>
    </div>
  )
}
