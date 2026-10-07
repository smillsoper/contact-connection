import type { DashboardWidgetType } from '../../types/dashboard'

export default function WidgetIcon({ type }: { type: DashboardWidgetType }) {
  switch (type) {
    case 'agent_state_counter':
      return (
        <svg viewBox="0 0 24 24" className="w-7 h-7">
          <circle cx="12" cy="12" r="8" fill="none" stroke="#374151" strokeWidth="4" />
          <path d="M12 4 A8 8 0 0 1 19.8 15.2" fill="none" stroke="#8b5cf6" strokeWidth="4" strokeLinecap="round" />
          <path d="M12 4 A8 8 0 0 0 5.2 17.4" fill="none" stroke="#22c55e" strokeWidth="4" strokeLinecap="round" />
        </svg>
      )
    case 'agent_list':
      return (
        <svg viewBox="0 0 24 24" className="w-7 h-7" fill="none" stroke="#9ca3af" strokeWidth="1.5">
          <rect x="3" y="4" width="18" height="16" rx="1.5" />
          <line x1="3" y1="9.5" x2="21" y2="9.5" />
          <line x1="6" y1="13.5" x2="15" y2="13.5" />
          <line x1="6" y1="17" x2="15" y2="17" />
          <circle cx="18.5" cy="13.5" r="1" fill="#22c55e" stroke="none" />
          <circle cx="18.5" cy="17" r="1" fill="#8b5cf6" stroke="none" />
        </svg>
      )
    case 'call_state_by_campaign':
      return (
        <svg viewBox="0 0 24 24" className="w-7 h-7">
          <rect x="3" y="16" width="4" height="5" rx="0.5" fill="#22c55e" />
          <rect x="10" y="11" width="4" height="10" rx="0.5" fill="#22c55e" />
          <rect x="10" y="8" width="4" height="3" rx="0.5" fill="#f97316" />
          <rect x="17" y="13" width="4" height="8" rx="0.5" fill="#22c55e" />
          <rect x="17" y="6" width="4" height="7" rx="0.5" fill="#eab308" />
        </svg>
      )
    case 'callbacks':
      return (
        <svg viewBox="0 0 24 24" className="w-7 h-7" fill="none" stroke="#9ca3af" strokeWidth="1.5">
          <path d="M4 5c0 8 7 15 15 15l1.5-3.5-4-2-1.8 1.8c-2.5-1.2-4.6-3.3-5.8-5.8L10.7 8l-2-4z" />
          <circle cx="18" cy="6" r="4" fill="none" stroke="#0ea5e9" strokeWidth="1.5" />
          <path d="M18 4v2.2l1.4 1" stroke="#0ea5e9" strokeWidth="1.5" strokeLinecap="round" />
        </svg>
      )
    case 'queued_calls':
      return (
        <svg viewBox="0 0 24 24" className="w-7 h-7">
          <rect x="3" y="4" width="18" height="4" rx="1" fill="#d946ef" />
          <rect x="3" y="10" width="18" height="4" rx="1" fill="#6b7280" />
          <rect x="3" y="16" width="18" height="4" rx="1" fill="#6b7280" />
        </svg>
      )
    case 'chart':
      return (
        <svg viewBox="0 0 24 24" className="w-7 h-7" fill="none" strokeWidth="1.8" strokeLinecap="round">
          <path d="M4 4v16h16" stroke="#6b7280" />
          <rect x="7" y="12" width="3" height="6" rx="0.5" fill="#a78bfa" stroke="none" />
          <rect x="12" y="9" width="3" height="9" rx="0.5" fill="#38bdf8" stroke="none" />
          <path d="M6.5 10l4-3 4 2 5-5" stroke="#34d399" />
        </svg>
      )
    case 'records':
      return (
        <svg viewBox="0 0 24 24" className="w-7 h-7" fill="none" strokeWidth="1.6" strokeLinecap="round">
          <rect x="3.5" y="4.5" width="17" height="15" rx="2" stroke="#6b7280" />
          <path d="M3.5 9h17M8 13h9M8 16h6" stroke="#a78bfa" />
        </svg>
      )
    case 'kpi':
      return (
        <svg viewBox="0 0 24 24" className="w-7 h-7" fill="none" strokeWidth="1.8" strokeLinecap="round">
          <path d="M4 19h16" stroke="#6b7280" />
          <path d="M6 15l4-4 3 3 5-6" stroke="#38bdf8" />
          <path d="M15 8h3v3" stroke="#38bdf8" />
        </svg>
      )
    case 'active_calls':
      return (
        <svg viewBox="0 0 24 24" className="w-7 h-7" fill="none" strokeWidth="1.5">
          <path d="M4 5c0 8 7 15 15 15l1.5-3.5-4-2-1.8 1.8c-2.5-1.2-4.6-3.3-5.8-5.8L10.7 8l-2-4z" stroke="#22c55e" />
          <path d="M15 3.5a6 6 0 0 1 5.5 5.5M15 7a2.5 2.5 0 0 1 2 2" stroke="#22c55e" strokeLinecap="round" />
        </svg>
      )
    case 'service_level_threshold':
      return (
        <svg viewBox="0 0 24 24" className="w-7 h-7">
          <circle cx="12" cy="12" r="8" fill="none" stroke="#ef4444" strokeWidth="4" />
          <path d="M12 4 A8 8 0 0 1 19.3 16.5" fill="none" stroke="#22c55e" strokeWidth="4" strokeLinecap="round" />
        </svg>
      )
  }
}
