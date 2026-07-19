import { useCallback, useEffect, useRef, useState } from 'react'
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, XAxis, YAxis } from 'recharts'
import { dashboardWidgetsApi, type CampaignStateCountRow } from '../../../api/dashboardWidgets'
import type { WidgetFilterConfig } from '../../../types/dashboard'
import { useDashboardLiveCallState } from '../DashboardLiveContext'

const BUCKET_COLORS: Record<string, string> = {
  pre_queue: '#eab308',
  in_queue: '#f97316',
  with_agent: '#22c55e',
  post_agent: '#3b82f6',
}

const BUCKET_LABELS: Record<string, string> = {
  pre_queue: 'PreQueue',
  in_queue: 'InQueue',
  with_agent: 'WithAgent',
  post_agent: 'PostAgent',
}

// Word-wraps a campaign name onto at most maxLines lines instead of letting it run off the
// bottom of the angled axis label. Overflow beyond maxLines is truncated with an ellipsis.
function wrapLabel(label: string, maxCharsPerLine = 12, maxLines = 2): string[] {
  const words = label.split(' ')
  const lines: string[] = []
  let current = ''

  for (const word of words) {
    const candidate = current ? `${current} ${word}` : word
    if (candidate.length > maxCharsPerLine && current) {
      lines.push(current)
      current = word
      if (lines.length === maxLines) break
    } else {
      current = candidate
    }
  }
  if (lines.length < maxLines && current) lines.push(current)

  const fullyConsumed = lines.join(' ').length >= label.length
  if (!fullyConsumed) {
    const last = lines[maxLines - 1] ?? ''
    lines[maxLines - 1] = last.length > maxCharsPerLine - 1 ? `${last.slice(0, maxCharsPerLine - 1)}…` : `${last}…`
  }

  return lines
}

interface AngledTickProps {
  x?: number
  y?: number
  payload?: { value: string }
}

function WrappedAngledTick({ x = 0, y = 0, payload }: AngledTickProps) {
  const lines = wrapLabel(payload?.value ?? '')
  return (
    <g transform={`translate(${x},${y})`}>
      <text textAnchor="end" fill="#9ca3af" fontSize={10} transform="rotate(-35)">
        {lines.map((line, i) => (
          <tspan key={i} x={0} dy={i === 0 ? 12 : 12}>{line}</tspan>
        ))}
      </text>
    </g>
  )
}

export default function CallStateByCampaignWidget({ config }: { config: WidgetFilterConfig }) {
  const [rows, setRows] = useState<CampaignStateCountRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const liveEvent = useDashboardLiveCallState()
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const load = useCallback(() => {
    dashboardWidgetsApi.callStateByCampaign(config)
      .then(setRows)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.campaignId, config.clientId])

  useEffect(() => { load() }, [load])

  // Any call anywhere in the tenant changing state could affect a bar here — debounce so a
  // burst of transitions doesn't trigger a refetch per event.
  useEffect(() => {
    if (!liveEvent) return
    if (debounceRef.current) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(load, 300)
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current) }
  }, [liveEvent, load])

  if (error) return <div className="text-xs text-red-400">{error}</div>

  return (
    <div className="h-full w-full flex flex-col">
      {rows.length === 0 ? (
        <div className="flex-1 flex items-center justify-center text-xs text-gray-600 text-center px-4">
          No active calls right now.
        </div>
      ) : (
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={rows} margin={{ top: 4, right: 12, left: -20, bottom: 24 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#1f2937" vertical={false} />
            <XAxis
              dataKey="campaign_name"
              height={85}
              interval={0}
              tick={<WrappedAngledTick />}
            />
            <YAxis allowDecimals={false} tick={{ fontSize: 10, fill: '#9ca3af' }} />
            <Legend
              verticalAlign="top"
              height={24}
              formatter={(value: string) => (
                <span className="text-[11px] text-gray-300">{BUCKET_LABELS[value] ?? value}</span>
              )}
              wrapperStyle={{ fontSize: 11 }}
            />
            <Bar dataKey="pre_queue" stackId="a" fill={BUCKET_COLORS.pre_queue} />
            <Bar dataKey="in_queue" stackId="a" fill={BUCKET_COLORS.in_queue} />
            <Bar dataKey="with_agent" stackId="a" fill={BUCKET_COLORS.with_agent} />
            <Bar dataKey="post_agent" stackId="a" fill={BUCKET_COLORS.post_agent} />
          </BarChart>
        </ResponsiveContainer>
      )}
    </div>
  )
}
