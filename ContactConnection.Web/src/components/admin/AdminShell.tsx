import { useEffect, useState } from 'react'
import { Link, useNavigate, useLocation } from 'react-router-dom'
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

const NAV_ITEMS = [
  { label: 'Dashboard', path: '/admin' },
  { label: 'Agents', path: '/admin/agents' },
  { label: 'API Definitions', path: '/admin/api-definitions' },
  { label: 'Credentials', path: '/admin/credentials' },
  { label: 'Flows', path: '/admin/flows' },
  { label: 'Settings', path: '/admin/settings' },
]

export default function AdminShell({ children }: Props) {
  const navigate = useNavigate()
  const location = useLocation()
  const { firstName, lastName, clearAuth } = useAuthStore()

  const [tenant, setTenant] = useState<TenantInfo | null>(null)
  const [logoError, setLogoError] = useState(false)

  useEffect(() => {
    api.get<TenantInfo>('/api/v1/tenants/me')
      .then(setTenant)
      .catch(() => {})
  }, [])

  function handleLogout() {
    clearAuth()
    navigate('/login', { replace: true })
  }

  const displayLabel = tenant?.displayName ?? tenant?.name ?? 'Admin Portal'
  const logoSrc = !logoError && tenant?.logoUrl ? tenant.logoUrl : '/cc-navbar-dark.svg'

  return (
    <div className="min-h-screen flex flex-col bg-gray-950">
      {/* ── Header ── */}
      <header className="h-12 bg-gray-900 border-b border-gray-800 flex items-center px-4 gap-4 shrink-0">
        <div className="flex items-center gap-3 mr-4">
          <img
            src={logoSrc}
            alt={displayLabel}
            className="h-7 w-auto max-w-[120px] object-contain shrink-0"
            onError={() => setLogoError(true)}
          />
          {tenant?.logoUrl && !logoError && (
            <span className="text-gray-400 text-xs font-medium tracking-widest uppercase border-l border-gray-700 pl-3 ml-1">
              Admin Portal
            </span>
          )}
        </div>

        <nav className="flex items-center gap-1 flex-1">
          {NAV_ITEMS.map((item) => {
            const active =
              item.path === '/admin'
                ? location.pathname === '/admin'
                : location.pathname.startsWith(item.path)
            return (
              <Link
                key={item.path}
                to={item.path}
                className={`px-3 py-1.5 rounded text-sm transition-colors ${
                  active
                    ? 'bg-gray-700 text-white'
                    : 'text-gray-400 hover:text-white hover:bg-gray-800'
                }`}
              >
                {item.label}
              </Link>
            )
          })}
        </nav>

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
