import { Link } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import { useAuthStore } from '../../stores/authStore'
import { openCallTrace } from '../../components/calltrace/openCallTrace'

interface NavCard {
  title: string
  desc: string
  path?: string
  onClick?: () => void
  live: boolean
}

interface NavSection {
  heading: string
  cards: NavCard[]
}

const NAV_SECTIONS: NavSection[] = [
  {
    heading: 'Team',
    cards: [
      { title: 'Users', desc: 'Manage team members — invite, assign roles, reset passwords.', path: '/admin/agents', live: true },
      { title: 'Roles', desc: 'Define roles and the permissions each one grants.', path: '/admin/roles', live: true },
    ],
  },
  {
    heading: 'Telephony',
    cards: [
      { title: 'Clients / Telephony', desc: 'DIDs, campaigns, clients, and agent groups.', path: '/admin/telephony', live: true },
      { title: 'SIP Gateways', desc: 'Configure carrier gateways and SIP trunks.', path: '/admin/sip-gateways', live: true },
    ],
  },
  {
    heading: 'Flow Engine',
    cards: [
      { title: 'Flows', desc: 'Build and publish call flows for your contact center.', path: '/flows', live: true },
      { title: 'Agent Portal', desc: 'Open the agent workspace — softphone, live flow, and chat.', path: '/agent', live: true },
      { title: 'Call Trace', desc: 'Watch calls flow through telephony and script nodes in real time.', onClick: () => openCallTrace(), live: true },
    ],
  },
  {
    heading: 'Integrations',
    cards: [
      { title: 'API Definitions', desc: 'Connect external services and configure API endpoints.', path: '/admin/api-definitions', live: true },
      { title: 'Credentials', desc: 'Store API keys, tokens, and secrets for integrations.', path: '/admin/credentials', live: true },
    ],
  },
  {
    heading: 'Coming Soon',
    cards: [
      { title: 'Commerce', desc: 'Products, offers, orders, and subscriptions.', path: '/admin/commerce', live: false },
      { title: 'Reports', desc: 'Call analytics, agent performance, and custom dashboards.', path: '/admin/reports', live: false },
      { title: 'Chat', desc: 'Internal team messaging and channel management.', path: '/admin/chat', live: false },
      { title: 'Settings', desc: 'Workspace settings, branding, and security policy.', path: '/admin/settings', live: false },
    ],
  },
]

export default function TenantAdminPage() {
  const { firstName } = useAuthStore()

  return (
    <AdminShell>
      <div className="p-6 max-w-5xl">
        {/* Welcome header */}
        <div className="mb-8">
          <h1 className="text-white text-2xl font-bold">
            Welcome back{firstName ? `, ${firstName}` : ''}
          </h1>
          <p className="text-gray-400 text-sm mt-1">
            Select a section below to manage your workspace.
          </p>
        </div>

        {/* Sectioned navigation grid */}
        <div className="space-y-8">
          {NAV_SECTIONS.map((section) => (
            <div key={section.heading}>
              <h2 className="text-xs font-semibold text-gray-500 uppercase tracking-widest mb-3">
                {section.heading}
              </h2>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                {section.cards.map((card) => {
                  const inner = (
                    <div className={`rounded-xl border p-5 h-full transition-all ${
                      card.live
                        ? 'bg-gray-900 border-gray-800 hover:border-indigo-700 hover:bg-gray-800/60 cursor-pointer'
                        : 'bg-gray-900/40 border-gray-800/50 cursor-default opacity-50'
                    }`}>
                      <p className="font-semibold text-white mb-1 text-sm">{card.title}</p>
                      <p className="text-gray-400 text-xs leading-relaxed">{card.desc}</p>
                      {card.live && (
                        <p className="text-indigo-400 text-xs mt-3 font-medium">Open →</p>
                      )}
                      {!card.live && (
                        <p className="text-gray-600 text-xs mt-3">Coming soon</p>
                      )}
                    </div>
                  )

                  if (!card.live) return <div key={card.title}>{inner}</div>
                  if (card.onClick) return <button key={card.title} onClick={card.onClick} className="block text-left w-full">{inner}</button>
                  return <Link key={card.title} to={card.path!} className="block">{inner}</Link>
                })}
              </div>
            </div>
          ))}
        </div>
      </div>
    </AdminShell>
  )
}
