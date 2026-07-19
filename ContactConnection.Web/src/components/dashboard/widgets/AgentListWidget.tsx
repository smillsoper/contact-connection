import { useCallback, useEffect, useMemo, useState } from 'react'
import { dashboardWidgetsApi, type AgentListRow } from '../../../api/dashboardWidgets'
import type { WidgetFilterConfig } from '../../../types/dashboard'
import { useDashboardLiveAgentState } from '../DashboardLiveContext'

type SortColumn = 'name' | 'state' | 'time'
type SortDirection = 'asc' | 'desc'

function SortArrow({ active, direction }: { active: boolean; direction: SortDirection }) {
  if (!active) return null
  return <span className="ml-1 text-gray-400">{direction === 'asc' ? '▲' : '▼'}</span>
}

const STATE_DOT: Record<string, string> = {
  available: 'bg-green-500',
  unavailable: 'bg-red-500',
  unavailable_break: 'bg-amber-500',
  unavailable_lunch: 'bg-orange-500',
  on_call: 'bg-violet-500',
  acw: 'bg-blue-500',
  logged_out: 'bg-gray-500',
}

function formatDuration(sinceIso: string | null): string {
  if (!sinceIso) return '—'
  const seconds = Math.max(0, Math.floor((Date.now() - new Date(sinceIso).getTime()) / 1000))
  const m = Math.floor(seconds / 60)
  const s = seconds % 60
  return `${m}:${s.toString().padStart(2, '0')}`
}

export default function AgentListWidget({ config }: { config: WidgetFilterConfig }) {
  const [rows, setRows] = useState<AgentListRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [, setTick] = useState(0)
  const [sortColumn, setSortColumn] = useState<SortColumn>('name')
  const [sortDirection, setSortDirection] = useState<SortDirection>('asc')
  const liveEvent = useDashboardLiveAgentState()

  const load = useCallback(() => {
    dashboardWidgetsApi.agentList(config)
      .then(setRows)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.campaignId, config.clientId, config.groupId, config.loggedInOnly])

  useEffect(() => { load() }, [load])

  // Re-render every second so the Time column keeps ticking without re-fetching
  useEffect(() => {
    const id = setInterval(() => setTick((t) => t + 1), 1000)
    return () => clearInterval(id)
  }, [])

  // Patch the matching row in place — avoids a full reload flicker on every state change
  useEffect(() => {
    if (!liveEvent) return
    setRows((prev) => prev.map((r) =>
      r.agent_id === liveEvent.agentId
        ? { ...r, state_code: liveEvent.code, state_label: liveEvent.label, since: liveEvent.since }
        : r
    ))
  }, [liveEvent])

  function handleSort(column: SortColumn) {
    if (sortColumn === column) {
      setSortDirection((d) => (d === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortColumn(column)
      setSortDirection('asc')
    }
  }

  const sortedRows = useMemo(() => {
    const withDir = (cmp: number) => (sortDirection === 'asc' ? cmp : -cmp)
    return [...rows].sort((a, b) => {
      if (sortColumn === 'name') return withDir(a.name.localeCompare(b.name))
      if (sortColumn === 'state') return withDir(a.state_label.localeCompare(b.state_label))
      // time — sort by the underlying timestamp, not the formatted string; rows with no
      // timestamp always sort last regardless of direction
      const aTime = a.since ? new Date(a.since).getTime() : null
      const bTime = b.since ? new Date(b.since).getTime() : null
      if (aTime === null && bTime === null) return 0
      if (aTime === null) return 1
      if (bTime === null) return -1
      return withDir(aTime - bTime)
    })
  }, [rows, sortColumn, sortDirection])

  if (error) return <div className="text-xs text-red-400">{error}</div>

  return (
    <div className="h-full overflow-auto">
      <table className="w-full text-xs">
        <thead>
          <tr className="text-left text-gray-500 border-b border-gray-800">
            <th className="py-1 pr-2 font-medium cursor-pointer select-none hover:text-gray-300" onClick={() => handleSort('name')}>
              Agent<SortArrow active={sortColumn === 'name'} direction={sortDirection} />
            </th>
            <th className="py-1 pr-2 font-medium cursor-pointer select-none hover:text-gray-300" onClick={() => handleSort('state')}>
              State<SortArrow active={sortColumn === 'state'} direction={sortDirection} />
            </th>
            <th className="py-1 font-medium cursor-pointer select-none hover:text-gray-300" onClick={() => handleSort('time')}>
              Time<SortArrow active={sortColumn === 'time'} direction={sortDirection} />
            </th>
          </tr>
        </thead>
        <tbody>
          {sortedRows.map((r) => (
            <tr key={r.agent_id} className="border-b border-gray-800/60 last:border-0">
              <td className="py-1.5 pr-2 text-gray-200 truncate max-w-[9rem]">{r.name}</td>
              <td className="py-1.5 pr-2">
                <span className="inline-flex items-center gap-1.5 text-gray-300">
                  <span className={`w-2 h-2 rounded-full shrink-0 ${STATE_DOT[r.state_code] ?? 'bg-gray-500'}`} />
                  {r.state_label}
                </span>
              </td>
              <td className="py-1.5 text-gray-500">{formatDuration(r.since)}</td>
            </tr>
          ))}
          {rows.length === 0 && (
            <tr>
              <td colSpan={3} className="py-4 text-center text-gray-600">No agents match this filter.</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}
