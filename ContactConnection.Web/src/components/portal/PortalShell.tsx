import { Link, useNavigate, useLocation } from 'react-router-dom'
import { usePortalAuthStore } from '../../stores/portalAuthStore'

interface Props {
  children: React.ReactNode
}

const NAV_ITEMS = [
  { label: 'Tenants', path: '/portal/tenants' },
  { label: 'API Definitions', path: '/portal/api-definitions' },
  { label: 'Credentials', path: '/portal/credentials' },
  { label: 'Maintenance', path: '/portal/maintenance' },
]

export default function PortalShell({ children }: Props) {
  const navigate = useNavigate()
  const location = useLocation()
  const { firstName, lastName, clearAuth } = usePortalAuthStore()

  function handleLogout() {
    clearAuth()
    navigate('/portal/login', { replace: true })
  }

  return (
    <div className="min-h-screen flex flex-col bg-gray-950">
      {/* ── Header ── */}
      <header className="h-12 bg-gray-900 border-b border-gray-800 flex items-center px-4 gap-4 shrink-0">
        <div className="flex items-center gap-2 mr-4">
          <img src="/cc-navbar-dark.svg" alt="ContactConnection" className="shrink-0 block" />
          <span className="text-gray-400 text-xs font-medium tracking-widest uppercase border-l border-gray-700 pl-3 ml-1">
            Platform Portal
          </span>
        </div>

        <nav className="flex items-center gap-1 flex-1">
          {NAV_ITEMS.map((item) => {
            const active = location.pathname.startsWith(item.path)
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
