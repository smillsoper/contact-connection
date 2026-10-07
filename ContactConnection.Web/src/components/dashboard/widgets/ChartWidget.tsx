import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Area, Bar, CartesianGrid, Cell, ComposedChart, Legend, Line, Pie, PieChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { dashboardWidgetsApi, type KpiMetrics, type KpiResult } from '../../../api/dashboardWidgets'
import type { ChartMetric, ChartType, WidgetFilterConfig } from '../../../types/dashboard'
import { useDashboardLiveAgentSessions, useDashboardLiveAgentState, useDashboardLiveCallState } from '../DashboardLiveContext'
import { useWidgetFetch } from '../WidgetDataSource'
import { fmt, kpiValue, resolveKpiDefs, type Format, type KpiDef } from './KpiWidget'

// Chart widget (S182): a picture of the KPI engine — the same metrics (built-in and custom), filters and production-only
// rules as the KPI widget. X axis = a time or category dimension (groupBy), optional series split (groupBy2).

export const CHART_COLORS = ['#38bdf8', '#a78bfa', '#34d399', '#fbbf24', '#f87171', '#f472b6', '#60a5fa', '#4ade80', '#fb923c', '#c084fc', '#2dd4bf', '#e879f9']

export const DEFAULT_CHART_METRICS: ChartMetric[] = [{ key: 'callsOffered' }]
export const DEFAULT_FUNNEL: ChartMetric[] = [{ key: 'callsOffered' }, { key: 'callsHandled' }, { key: 'opportunities' }, { key: 'orders' }]

/** Rates and times read on the right axis by default, counts and money on the left. */
export const defaultAxis = (f: Format): 'left' | 'right' => (f === 'pct' || f === 'secs' ? 'right' : 'left')

const TIME_DIMS = ['interval15', 'interval30', 'interval60', 'day', 'week', 'month', 'hour', 'dow']
export const isTimeDim = (d?: string) => !!d && TIME_DIMS.includes(d)

/** Short x-axis labels for time dimensions ("2:30 PM", "Mon 10/6", "Wk of 10/5", "Oct 2026"). */
function xLabel(dim: string | undefined, label: string, multiDay: boolean): string {
  const time = (hhmm: string) => {
    const [h, m] = hhmm.split(':').map(Number)
    return `${((h + 11) % 12) + 1}${m ? `:${String(m).padStart(2, '0')}` : ''} ${h < 12 ? 'AM' : 'PM'}`
  }
  const md = (ymd: string) => { const [, mo, d] = ymd.split('-'); return `${Number(mo)}/${Number(d)}` }
  switch (dim) {
    case 'interval15': case 'interval30': case 'interval60': {
      const [date, t] = label.split(' ')
      return multiDay ? `${md(date)} ${time(t)}` : time(t)
    }
    case 'hour': return time(label)
    case 'day': { const [date, dow] = label.split(' '); return `${dow} ${md(date)}` }
    case 'week': return `Wk of ${md(label)}`
    case 'month': { const [y, mo] = label.split('-'); return `${new Date(Number(y), Number(mo) - 1, 1).toLocaleString(undefined, { month: 'short' })} ${y}` }
    default: return label
  }
}

/** Compact axis ticks — "1.2k", "$4.5k", "45%", "3:20". */
function tick(v: number, f: Format): string {
  if (f === 'pct') return `${Math.round(v)}%`
  if (f === 'secs') return fmt(v, 'secs')
  const abs = Math.abs(v)
  const n = abs >= 1_000_000 ? `${(v / 1_000_000).toFixed(1)}M` : abs >= 1000 ? `${(v / 1000).toFixed(1)}k` : `${Math.round(v * 100) / 100}`
  return f === 'money' ? `$${n}` : n
}

export default function ChartWidget({ config }: { config: WidgetFilterConfig }) {
  const [data, setData] = useState<KpiResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const debounce = useRef<ReturnType<typeof setTimeout> | null>(null)
  const sessions = useDashboardLiveAgentSessions()
  const callState = useDashboardLiveCallState()
  const agentState = useDashboardLiveAgentState()
  const type: ChartType = config.chartType ?? 'line'
  const fetchData = useWidgetFetch(() => dashboardWidgetsApi.kpi(config))

  const load = useCallback(() => {
    fetchData().then((d) => { setData(d); setError(null) })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.campaignId, config.clientId, config.groupId, (config.dnis ?? []).join(','), config.groupBy, config.groupBy2,
    config.timeWindow?.mode, config.timeWindow?.value, config.compare, type])

  useEffect(() => { load() }, [load])
  useEffect(() => {
    if (!sessions && !callState && !agentState) return
    if (debounce.current) clearTimeout(debounce.current)
    debounce.current = setTimeout(load, 1000)
    return () => { if (debounce.current) clearTimeout(debounce.current) }
  }, [sessions, callState, agentState, load])

  const basis = config.revenueBasis ?? 'exclTax'
  const net = config.netRevenue ?? false
  const metrics = config.chartMetrics?.length ? config.chartMetrics : type === 'funnel' ? DEFAULT_FUNNEL : DEFAULT_CHART_METRICS
  const defs = useMemo(() => (data ? resolveKpiDefs(metrics.map((m) => m.key), data.total.custom) : []), [data, metrics])
  const val = useCallback((d: KpiDef, m: KpiMetrics) => kpiValue(d, m, basis, net), [basis, net])

  if (error) return <div className="text-xs text-red-400">{error}</div>
  if (!data) return <div className="text-xs text-gray-500">Loading…</div>
  if (defs.length === 0) return <div className="text-xs text-gray-500">Pick a metric in the widget's settings.</div>

  const footer = <p className="text-[10px] text-gray-600 mt-1 shrink-0">Production calls · {data.total.callsOffered.toLocaleString()} calls · {data.total.interactions.toLocaleString()} interactions</p>
  const tooltipStyle = { backgroundColor: '#111827', border: '1px solid #374151', borderRadius: 6, fontSize: 11 }

  // ── Funnel: totals, step by step ──
  if (type === 'funnel') {
    const steps = defs.map((d) => ({ d, v: val(d, data.total) ?? 0 }))
    const top = Math.max(...steps.map((s) => s.v), 1)
    return (
      <div className="h-full flex flex-col">
        <div className="flex-1 min-h-0 overflow-auto flex flex-col justify-center gap-1.5 px-2">
          {steps.map((s, i) => {
            const prev = i > 0 ? steps[i - 1].v : null
            return (
              <div key={s.d.key} className="flex items-center gap-3">
                <span className="w-36 shrink-0 text-xs text-gray-300 text-right truncate" title={s.d.label}>{s.d.label}</span>
                <div className="flex-1 flex justify-center">
                  <div className="h-7 rounded flex items-center justify-center text-xs font-semibold text-gray-950 transition-all"
                    style={{ width: `${Math.max(4, (s.v / top) * 100)}%`, backgroundColor: CHART_COLORS[i % CHART_COLORS.length] }}>
                    {fmt(s.v, s.d.format)}
                  </div>
                </div>
                <span className="w-24 shrink-0 text-[11px] text-gray-500">
                  {prev != null ? (prev > 0 ? `${((s.v / prev) * 100).toFixed(1)}% of prev.` : '—') : ''}
                </span>
              </div>
            )
          })}
        </div>
        {footer}
      </div>
    )
  }

  // ── Heatmap: day of week × hour of day ──
  if (type === 'heatmap') {
    const d = defs[0]
    const cells = new Map(data.rows.filter((r) => !r.subtotal && r.label2).map((r) => [`${r.label}|${r.label2}`, val(d, r.metrics)]))
    const days = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']
    const hours = Array.from({ length: 24 }, (_, h) => `${String(h).padStart(2, '0')}:00`)
    const max = Math.max(...[...cells.values()].map((v) => v ?? 0), 0)
    return (
      <div className="h-full flex flex-col">
        <div className="flex-1 min-h-0 overflow-auto">
          <table className="w-full border-separate" style={{ borderSpacing: 2 }}>
            <thead>
              <tr>
                <th />
                {hours.map((h, i) => <th key={h} className="text-[9px] text-gray-500 font-normal">{i % 3 === 0 ? xLabel('hour', h, false) : ''}</th>)}
              </tr>
            </thead>
            <tbody>
              {days.map((day) => (
                <tr key={day}>
                  <td className="text-[10px] text-gray-400 pr-1">{day}</td>
                  {hours.map((h) => {
                    const v = cells.get(`${day}|${h}`) ?? null
                    const a = max > 0 && v ? 0.12 + 0.88 * (v / max) : 0
                    return (
                      <td key={h} title={`${day} ${xLabel('hour', h, false)} — ${fmt(v, d.format)}`}
                        className="h-5 rounded-sm" style={{ backgroundColor: a ? `rgba(56,189,248,${a})` : 'rgba(31,41,55,0.6)' }} />
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
          <p className="text-[10px] text-gray-500 mt-1">{d.label} by day of week and hour · darker = more{max ? ` (busiest: ${fmt(max, d.format)})` : ''}</p>
        </div>
        {footer}
      </div>
    )
  }

  const xDim = config.groupBy
  const top = data.rows.filter((r) => !r.subtotal && !r.label2)
  const split = !!config.groupBy2 && ['line', 'area', 'bar', 'hbar', 'stacked'].includes(type)

  // ── Pie / donut: one metric's share across the categories ──
  if (type === 'pie' || type === 'donut') {
    const d = defs[0]
    const slices = top.map((r) => ({ name: xLabel(xDim, r.label, false), value: val(d, r.metrics) ?? 0 }))
      .filter((s) => s.value > 0).sort((a, b) => b.value - a.value)
    const shown = slices.length > 9 ? [...slices.slice(0, 8), { name: 'Other', value: slices.slice(8).reduce((n, s) => n + s.value, 0) }] : slices
    const total = shown.reduce((n, s) => n + s.value, 0)
    return (
      <div className="h-full flex flex-col">
        <div className="flex-1 min-h-0">
          {shown.length === 0 ? <div className="h-full flex items-center justify-center text-xs text-gray-500">No data in this time window.</div> : (
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie data={shown} dataKey="value" nameKey="name" innerRadius={type === 'donut' ? '55%' : 0} outerRadius="80%"
                  paddingAngle={type === 'donut' ? 2 : 0} stroke="#111827"
                  label={({ percent }) => ((percent ?? 0) >= 0.05 ? `${((percent ?? 0) * 100).toFixed(0)}%` : '')} labelLine={false}>
                  {shown.map((_, i) => <Cell key={i} fill={CHART_COLORS[i % CHART_COLORS.length]} />)}
                </Pie>
                <Tooltip contentStyle={tooltipStyle} formatter={(v) => [`${fmt(Number(v), d.format)} (${total ? ((Number(v) / total) * 100).toFixed(1) : 0}%)`, d.label]} />
                <Legend wrapperStyle={{ fontSize: 11 }} layout="vertical" align="right" verticalAlign="middle" />
              </PieChart>
            </ResponsiveContainer>
          )}
        </div>
        {footer}
      </div>
    )
  }

  // ── Line / area / bar / stacked / combo ──
  const xs = (split ? data.rows.filter((r) => r.subtotal) : top).map((r) => r.label)
  const multiDay = new Set(xs.map((x) => x.split(' ')[0])).size > 1
  type Point = Record<string, number | string | null>
  const points: Point[] = xs.map((x) => ({ x: xLabel(xDim, x, multiDay) }))
  const series: { id: string; name: string; def: KpiDef; axis: 'left' | 'right'; as: 'bar' | 'line' | 'area'; previous?: boolean; color: string }[] = []

  if (split) {
    const d = defs[0]
    const axis = metrics[0].axis ?? 'left'
    const names = [...new Set(data.rows.filter((r) => !r.subtotal && r.label2).map((r) => r.label2!))]
    names.forEach((n, i) => series.push({ id: `s${i}`, name: n, def: d, axis, as: type === 'line' ? 'line' : type === 'area' ? 'area' : 'bar', color: CHART_COLORS[i % CHART_COLORS.length] }))
    data.rows.filter((r) => !r.subtotal && r.label2).forEach((r) => {
      const p = points[xs.indexOf(r.label)]
      if (p) p[`s${names.indexOf(r.label2!)}`] = val(d, r.metrics)
    })
  } else {
    defs.forEach((d, i) => {
      const m = metrics.find((x) => x.key === d.key)
      const as = type === 'combo' ? (m?.as ?? (i === 0 ? 'bar' : 'line')) : type === 'line' ? 'line' : type === 'area' ? 'area' : 'bar'
      series.push({ id: `m${i}`, name: d.label, def: d, axis: m?.axis ?? defaultAxis(d.format), as, color: CHART_COLORS[i % CHART_COLORS.length] })
      top.forEach((r, j) => { points[j][`m${i}`] = val(d, r.metrics) })
    })
    // Previous period, aligned by position (bucket 1 with bucket 1).
    if (config.compare && data.previous && isTimeDim(xDim)) {
      const prevTop = data.previous.rows.filter((r) => !r.subtotal && !r.label2)
      defs.forEach((d, i) => {
        const base = series[i]
        series.push({ ...base, id: `p${i}`, name: `${d.label} (previous)`, previous: true, as: base.as === 'bar' ? 'bar' : 'line' })
        prevTop.forEach((r, j) => { if (points[j]) points[j][`p${i}`] = val(d, r.metrics) })
      })
    }
  }

  const leftFmt = series.find((s) => s.axis === 'left')?.def.format ?? 'int'
  const rightFmt = series.find((s) => s.axis === 'right')?.def.format ?? 'pct'
  const hasLeft = series.some((s) => s.axis === 'left')
  const hasRight = series.some((s) => s.axis === 'right')
  const horizontal = type === 'hbar'
  const stacked = type === 'stacked'
  const targets = config.showTargets
    ? defs.flatMap((d) => {
      const t = config.targets?.[d.key]?.good
      const s = series.find((x) => x.def.key === d.key && !x.previous)
      return t != null && s ? [{ value: t, axis: s.axis, label: `${d.label} target`, color: s.color }] : []
    })
    : []

  const valueAxis = (side: 'left' | 'right', f: Format) => horizontal
    ? <XAxis key={side} xAxisId={side} type="number" orientation={side === 'left' ? 'bottom' : 'top'} tick={{ fill: '#6b7280', fontSize: 10 }}
        tickFormatter={(v) => tick(v, f)} stroke="#374151" />
    : <YAxis key={side} yAxisId={side} orientation={side} tick={{ fill: '#6b7280', fontSize: 10 }} tickFormatter={(v) => tick(v, f)}
        stroke="#374151" width={48} />

  return (
    <div className="h-full flex flex-col">
      <div className="flex-1 min-h-0">
        {points.length === 0 ? <div className="h-full flex items-center justify-center text-xs text-gray-500">No data in this time window.</div> : (
          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart data={points} layout={horizontal ? 'vertical' : 'horizontal'} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
              <CartesianGrid stroke="#1f2937" vertical={horizontal} horizontal={!horizontal} />
              {horizontal
                ? <YAxis dataKey="x" type="category" tick={{ fill: '#9ca3af', fontSize: 10 }} width={110} stroke="#374151" />
                : <XAxis dataKey="x" tick={{ fill: '#9ca3af', fontSize: 10 }} stroke="#374151" minTickGap={12} />}
              {hasLeft && valueAxis('left', leftFmt)}
              {hasRight && valueAxis('right', rightFmt)}
              <Tooltip contentStyle={tooltipStyle} cursor={{ fill: 'rgba(148,163,184,0.08)' }}
                formatter={(v, name) => {
                  const s = series.find((x) => x.name === name)
                  return [fmt(v == null ? null : Number(v), s?.def.format ?? 'num'), name]
                }} />
              {series.length > 1 && <Legend wrapperStyle={{ fontSize: 11 }} />}
              {series.map((s) => {
                const axisProps = horizontal ? { xAxisId: s.axis } : { yAxisId: s.axis }
                const common = { dataKey: s.id, name: s.name, isAnimationActive: false, ...axisProps }
                if (s.as === 'bar')
                  return <Bar key={s.id} {...common} fill={s.color} fillOpacity={s.previous ? 0.35 : 0.9} stackId={stacked ? 'stack' : undefined}
                    radius={stacked ? 0 : horizontal ? [0, 3, 3, 0] : [3, 3, 0, 0]} maxBarSize={48} />
                if (s.as === 'area')
                  return <Area key={s.id} {...common} type="monotone" stroke={s.color} fill={s.color} fillOpacity={0.18} strokeWidth={2}
                    strokeDasharray={s.previous ? '4 4' : undefined} connectNulls />
                return <Line key={s.id} {...common} type="monotone" stroke={s.color} strokeWidth={s.previous ? 1.5 : 2}
                  strokeOpacity={s.previous ? 0.55 : 1} strokeDasharray={s.previous ? '4 4' : undefined} dot={points.length <= 31} connectNulls />
              })}
              {targets.map((t, i) => horizontal
                ? <ReferenceLine key={i} x={t.value} xAxisId={t.axis} stroke={t.color} strokeDasharray="6 3" label={{ value: t.label, fill: '#9ca3af', fontSize: 10 }} />
                : <ReferenceLine key={i} y={t.value} yAxisId={t.axis} stroke={t.color} strokeDasharray="6 3"
                    label={{ value: t.label, fill: '#9ca3af', fontSize: 10, position: 'insideTopRight' }} />)}
            </ComposedChart>
          </ResponsiveContainer>
        )}
      </div>
      {footer}
    </div>
  )
}
