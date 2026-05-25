import { Link } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import { useAuthStore } from '../../stores/authStore'

const PLACEHOLDER_CARDS = [
  { title: 'Agents', desc: 'Manage your team — view, reset passwords, and deactivate agents.', icon: '👥', path: '/admin/agents', color: 'indigo', live: true },
  { title: 'Flows', desc: 'Build and publish call flows for your contact center.', icon: '⚡', path: '/admin/flows', color: 'sky', live: false },
  { title: 'Commerce', desc: 'Products, offers, orders, and subscriptions.', icon: '🛒', path: '/admin/commerce', color: 'emerald', live: false },
  { title: 'Settings', desc: 'Workspace settings, branding, and security.', icon: '⚙️', path: '/admin/settings', color: 'gray', live: false },
]

const COLOR_CLASSES: Record<string, string> = {
  indigo: 'bg-indigo-900/30 border-indigo-800/40 text-indigo-400',
  sky: 'bg-sky-900/30 border-sky-800/40 text-sky-400',
  emerald: 'bg-emerald-900/30 border-emerald-800/40 text-emerald-400',
  gray: 'bg-gray-800/60 border-gray-700/40 text-gray-400',
}

export default function TenantAdminPage() {
  const { firstName } = useAuthStore()

  return (
    <AdminShell>
      <div className="p-6 max-w-4xl">
        {/* Welcome header */}
        <div className="mb-8">
          <h1 className="text-white text-2xl font-bold">
            Welcome back{firstName ? `, ${firstName}` : ''}
          </h1>
          <p className="text-gray-400 text-sm mt-1">
            Here's an overview of your workspace. More features are on the way.
          </p>
        </div>

        {/* Quick-access cards */}
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-8">
          {PLACEHOLDER_CARDS.map((card) => {
            const inner = (
              <div className={`rounded-xl border p-5 transition-colors ${COLOR_CLASSES[card.color]} ${card.live ? 'hover:brightness-110 cursor-pointer' : 'cursor-default select-none'}`}>
                <div className="flex items-start gap-3">
                  <span className="text-2xl">{card.icon}</span>
                  <div>
                    <p className="font-semibold text-white mb-0.5">{card.title}</p>
                    <p className="text-gray-400 text-sm">{card.desc}</p>
                  </div>
                </div>
                <p className="text-xs mt-3 opacity-60">{card.live ? '→' : 'Coming soon'}</p>
              </div>
            )
            return card.live
              ? <Link key={card.title} to={card.path}>{inner}</Link>
              : <div key={card.title}>{inner}</div>
          })}
        </div>

        {/* Status banner */}
        <div className="bg-gray-900 border border-gray-800 rounded-xl p-5">
          <p className="text-gray-300 text-sm font-medium mb-1">Admin Portal — Skeleton</p>
          <p className="text-gray-500 text-sm">
            This portal is under active development. Agent management, settings, reporting,
            and full commerce administration will be added in upcoming sessions.
          </p>
        </div>
      </div>
    </AdminShell>
  )
}
