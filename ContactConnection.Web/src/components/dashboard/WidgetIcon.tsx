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
  }
}
