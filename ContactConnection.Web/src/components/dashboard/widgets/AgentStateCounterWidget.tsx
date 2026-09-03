import { useCallback, useEffect, useRef, useState } from 'react'
import { PieChart, Pie, Cell, ResponsiveContainer } from 'recharts'
import { dashboardWidgetsApi, type AgentStateCounterData } from '../../../api/dashboardWidgets'
import type { WidgetFilterConfig } from '../../../types/dashboard'
import { useDashboardLiveAgentState } from '../DashboardLiveContext'

const STATE_COLORS: Record<string, string> = {
  available: '#22c55e',
  unavailable: '#ef4444',
  unavailable_break: '#f59e0b',
  unavailable_lunch: '#f97316',
  on_call: '#8b5cf6',
  acw: '#3b82f6',
  callback_pending: '#0ea5e9',
  logged_out: '#6b7280',
}

const STATE_LABELS: Record<string, string> = {
  available: 'Available',
  unavailable: 'Unavailable',
  unavailable_break: 'Break',
  unavailable_lunch: 'Lunch',
  on_call: 'On Call',
  acw: 'ACW',
  callback_pending: 'Callback Pending',
  logged_out: 'Logged Out',
}

export default function AgentStateCounterWidget({ config }: { config: WidgetFilterConfig }) {
  const [data, setData] = useState<AgentStateCounterData | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [chartSize, setChartSize] = useState({ width: 160, height: 160 })
  const liveEvent = useDashboardLiveAgentState()
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const load = useCallback(() => {
    dashboardWidgetsApi.agentStateCounter(config)
      .then(setData)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.campaignId, config.clientId, config.groupId, config.loggedInOnly])

  useEffect(() => { load() }, [load])

  // Any agent's state changing anywhere in the tenant could affect this widget's aggregate —
  // debounce so a burst of changes doesn't trigger a refetch per event.
  useEffect(() => {
    if (!liveEvent) return
    if (debounceRef.current) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(load, 300)
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current) }
  }, [liveEvent, load])

  if (error) return <div className="text-xs text-red-400">{error}</div>
  if (!data) return <div className="text-xs text-gray-500">Loading…</div>

  const chartData = Object.entries(data.by_state)
    .filter(([, count]) => count > 0)
    .map(([code, count]) => ({ code, count, label: STATE_LABELS[code] ?? code }))

  const pieData = chartData.length ? chartData : [{ code: 'empty', count: 1, label: 'No agents' }]

  // Scale the center label with whichever dimension is tighter, so it stays proportional
  // whether the widget gets wider or taller — useful on a wall-mounted monitor where a
  // widget might be resized much larger than its default size.
  const centerFontPx = Math.max(14, Math.min(chartSize.width, chartSize.height) * 0.22)
  const labelFontPx = Math.max(9, centerFontPx * 0.32)

  return (
    <div className="h-full flex flex-col items-center justify-center">
      <div className="flex flex-wrap gap-x-3 gap-y-1 justify-center mb-2 shrink-0">
        {Object.entries(data.by_state).map(([code, count]) => (
          <div key={code} className="flex items-center gap-1 text-[11px] text-gray-300">
            <span className="w-2 h-2 rounded-sm shrink-0" style={{ backgroundColor: STATE_COLORS[code] ?? '#6b7280' }} />
            {STATE_LABELS[code] ?? code} ({count})
          </div>
        ))}
      </div>
      <div className="relative flex-1 w-full min-h-0">
        <ResponsiveContainer width="100%" height="100%" onResize={(width, height) => setChartSize({ width, height })}>
          <PieChart>
            <Pie
              data={pieData}
              dataKey="count"
              nameKey="label"
              innerRadius="60%"
              outerRadius="90%"
              paddingAngle={chartData.length > 1 ? 2 : 0}
              stroke="none"
            >
              {pieData.map((entry) => (
                <Cell key={entry.code} fill={STATE_COLORS[entry.code] ?? '#374151'} />
              ))}
            </Pie>
          </PieChart>
        </ResponsiveContainer>
        <div className="absolute inset-0 flex flex-col items-center justify-center pointer-events-none">
          <span className="font-bold text-white" style={{ fontSize: `${centerFontPx}px`, lineHeight: 1.1 }}>{data.total}</span>
          <span className="text-gray-500" style={{ fontSize: `${labelFontPx}px` }}>Agents</span>
        </div>
      </div>
    </div>
  )
}
