import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../stores/authStore'
import { api } from '../../api/client'

interface TenantInfo {
  id: string
  name: string
  displayName: string | null
  logoUrl: string | null
  subdomain: string
  onboardingComplete: boolean
}

interface Props {
  children: React.ReactNode
}

export default function AdminShell({ children }: Props) {
  const navigate = useNavigate()
  const { firstName, lastName, clearAuth } = useAuthStore()

  const [tenant, setTenant] = useState<TenantInfo | null>(null)
  const [logoError, setLogoError] = useState(false)

  useEffect(() => {
    api.get<TenantInfo>('/api/v1/tenants/me')
      .then(setTenant)
      .catch(() => { })
  }, [])

  function handleLogout() {
    clearAuth()
    navigate('/login', { replace: true })
  }

  const displayLabel = tenant?.displayName ?? tenant?.name ?? 'Admin Portal'
  const isCustomLogo = !logoError && !!tenant?.logoUrl
  const logoSrc = isCustomLogo ? tenant!.logoUrl! : '/cc-navbar-dark.svg'

  return (
    <div className="min-h-screen flex flex-col bg-gray-950">
      {/* ── Header ── */}
      <header className="h-12 bg-gray-900 border-b border-gray-800 flex items-center px-4 gap-4 shrink-0">

        {/* Logo — links back to the dashboard */}
        <Link to="/admin" className="self-stretch flex items-stretch shrink-0 mr-2">
          <img
            src={logoSrc}
            alt={displayLabel}
            className={`w-auto shrink-0 ${isCustomLogo ? 'h-7 max-w-[120px] object-contain self-center' : 'h-full'}`}
            onError={() => setLogoError(true)}
          />
        </Link>

        {isCustomLogo && (
          <span className="text-gray-400 text-xs font-medium tracking-widest uppercase border-l border-gray-700 pl-3">
            Admin Portal
          </span>
        )}

        <Link
          to="/admin"
          className="text-gray-400 hover:text-white text-sm transition-colors ml-2"
        >
          Dashboard
        </Link>

        {/* Spacer */}
        <div className="flex-1" />

        {/* User + sign out */}
        <div className="flex items-center gap-3">
          <span className="text-gray-400 text-sm">{firstName} {lastName}</span>
          <button
            onClick={handleLogout}
            className="text-gray-400 hover:text-white text-sm transition-colors"
          >
            Sign out
          </button>
        </div>
      </header>

      {/* ── Content ── */}
      <main className="flex-1 overflow-auto">
        {children}
      </main>
    </div>
  )
}
