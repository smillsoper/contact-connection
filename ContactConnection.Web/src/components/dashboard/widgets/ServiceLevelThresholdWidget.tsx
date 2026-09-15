import { useCallback, useEffect, useRef, useState } from 'react'
import { PieChart, Pie, Cell, ResponsiveContainer } from 'recharts'
import { dashboardWidgetsApi, type ServiceLevelThresholdData } from '../../../api/dashboardWidgets'
import type { WidgetFilterConfig } from '../../../types/dashboard'
import { useDashboardLiveCallState } from '../DashboardLiveContext'

const MET_COLOR = '#22c55e'
const MISSED_COLOR = '#ef4444'

export default function ServiceLevelThresholdWidget({ config }: { config: WidgetFilterConfig }) {
  const [data, setData] = useState<ServiceLevelThresholdData | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [chartSize, setChartSize] = useState({ width: 160, height: 160 })
  const liveEvent = useDashboardLiveCallState()
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const load = useCallback(() => {
    dashboardWidgetsApi.serviceLevelThreshold(config)
      .then(setData)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.campaignId, config.clientId, config.timeWindow?.mode, config.timeWindow?.value])

  useEffect(() => { load() }, [load])

  // A call answering anywhere in scope could shift this widget's percentage — debounce so a
  // burst of near-simultaneous answers doesn't trigger a refetch per event.
  useEffect(() => {
    if (!liveEvent) return
    if (debounceRef.current) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(load, 300)
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current) }
  }, [liveEvent, load])

  if (error) return <div className="text-xs text-red-400">{error}</div>
  if (!data) return <div className="text-xs text-gray-500">Loading…</div>

  const total = data.met + data.missed
  const pieData = total > 0
    ? [{ code: 'met', count: data.met, label: 'In SL' }, { code: 'missed', count: data.missed, label: 'Missed' }].filter((d) => d.count > 0)
    : [{ code: 'empty', count: 1, label: 'No data' }]

  // Scale the center label with whichever dimension is tighter, so it stays proportional
  // whether the widget gets wider or taller. Smaller factor than AgentStateCounterWidget's --
  // "66.7%" is a much wider string than that widget's plain integer count, and clips against
  // the ring at the same scale.
  const centerFontPx = Math.max(14, Math.min(chartSize.width, chartSize.height) * 0.17)
  const labelFontPx = Math.max(9, centerFontPx * 0.32)

  return (
    <div className="h-full flex flex-col items-center justify-center">
      <div className="flex flex-wrap gap-x-3 gap-y-1 justify-center mb-2 shrink-0">
        <div className="flex items-center gap-1 text-[11px] text-gray-300">
          <span className="w-2 h-2 rounded-sm shrink-0" style={{ backgroundColor: MET_COLOR }} />
          In SL ({data.met})
        </div>
        <div className="flex items-center gap-1 text-[11px] text-gray-300">
          <span className="w-2 h-2 rounded-sm shrink-0" style={{ backgroundColor: MISSED_COLOR }} />
          Missed ({data.missed})
        </div>
      </div>
      <div className="relative flex-1 w-full min-h-0">
        <ResponsiveContainer width="100%" height="100%" onResize={(width, height) => setChartSize({ width, height })}>
          <PieChart>
            <Pie
              data={pieData}
              dataKey="count"
              nameKey="label"
              innerRadius="68%"
              outerRadius="95%"
              paddingAngle={pieData.length > 1 ? 2 : 0}
              stroke="none"
            >
              {pieData.map((entry) => (
                <Cell key={entry.code} fill={entry.code === 'met' ? MET_COLOR : entry.code === 'missed' ? MISSED_COLOR : '#374151'} />
              ))}
            </Pie>
          </PieChart>
        </ResponsiveContainer>
        <div className="absolute inset-0 flex flex-col items-center justify-center pointer-events-none">
          <span className="font-bold text-white" style={{ fontSize: `${centerFontPx}px`, lineHeight: 1.1 }}>
            {data.percent_in_sl ?? 0}%
          </span>
          <span className="text-gray-500" style={{ fontSize: `${labelFontPx}px` }}>in SL</span>
        </div>
      </div>
    </div>
  )
}
