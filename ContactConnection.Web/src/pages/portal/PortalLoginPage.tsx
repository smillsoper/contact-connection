import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { msalInitialized, msalInstance, portalLoginRequest } from '../../config/msal'

const ERROR_MESSAGES: Record<string, string> = {
  unauthorized: 'Your account is not authorized for portal access.',
  failed: 'Sign-in failed. Please try again.',
  token_exchange: 'Could not complete sign-in. Please try again.',
}

export default function PortalLoginPage() {
  const [searchParams] = useSearchParams()
  const errorKey = searchParams.get('error')

  const [splash, setSplash] = useState<'splash' | 'fading' | 'done'>('splash')

  useEffect(() => {
    const fadeTimer = setTimeout(() => setSplash('fading'), 4400)
    const doneTimer = setTimeout(() => setSplash('done'),  5000)
    return () => { clearTimeout(fadeTimer); clearTimeout(doneTimer) }
  }, [])

  async function handleLogin() {
    await msalInitialized
    await msalInstance.loginRedirect(portalLoginRequest)
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
        <div className="flex flex-col items-center mb-8">
          <img src="/cc-logo-dark.svg" alt="ContactConnection" className="h-14 mb-4" />
          <p className="text-gray-400 text-sm font-medium tracking-widest uppercase">Platform Portal</p>
        </div>

        {errorKey && (
          <div className="mb-4 bg-red-900/40 border border-red-700 text-red-300 text-sm px-4 py-2 rounded-lg">
            {ERROR_MESSAGES[errorKey] ?? 'An error occurred. Please try again.'}
          </div>
        )}

        <button
          onClick={handleLogin}
          className="w-full flex items-center justify-center gap-3 bg-[#0078D4] hover:bg-[#106EBE] text-white rounded-lg px-4 py-3 text-sm font-medium transition-colors"
        >
          <svg width="18" height="18" viewBox="0 0 21 21" fill="none" xmlns="http://www.w3.org/2000/svg">
            <rect x="1" y="1" width="9" height="9" fill="#F25022"/>
            <rect x="11" y="1" width="9" height="9" fill="#7FBA00"/>
            <rect x="1" y="11" width="9" height="9" fill="#00A4EF"/>
            <rect x="11" y="11" width="9" height="9" fill="#FFB900"/>
          </svg>
          Sign in with Microsoft
        </button>
      </div>
    </div>
  )
}
