import type { ChartMetric, ChartType, KpiTarget, KpiWidgetConfig } from '../../types/dashboard'
import ChartTypeIcon, { CHART_TYPE_COLOR } from './ChartTypeIcon'
import type { CustomKpi } from '../../api/dashboardWidgets'
import { KPI_CATALOG, KPI_DIMENSIONS, customFormat, type Format } from './widgets/KpiWidget'
import { DEFAULT_CHART_METRICS, DEFAULT_FUNNEL, defaultAxis, isTimeDim } from './widgets/ChartWidget'
import KpiTargetInput from './KpiTargetInput'
import { ChevronDownIcon, ChevronUpIcon, DeleteIcon } from '../icons/Icons'

// Chart widget settings (S182). The chart is a view of the KPI engine: an X axis (time or category), metrics on a left /
// right axis, an optional series split, and extras (previous period, target lines).

export interface ChartSettingsValue {
  chartType: ChartType
  groupBy: string
  groupBy2: string
  chartMetrics: ChartMetric[]
  compare: boolean
  showTargets: boolean
  targets: Record<string, KpiTarget>
  revenueBasis: NonNullable<KpiWidgetConfig['revenueBasis']>
  netRevenue: boolean
}

const TYPES: { type: ChartType; label: string; hint: string }[] = [
  { type: 'line', label: 'Line', hint: 'Trends over time' },
  { type: 'area', label: 'Area', hint: 'Volume over time' },
  { type: 'bar', label: 'Bar', hint: 'Compare periods or categories' },
  { type: 'hbar', label: 'Horizontal bar', hint: 'Rank categories (agents, stations)' },
  { type: 'stacked', label: 'Stacked bar', hint: 'Composition — split by a second dimension' },
  { type: 'combo', label: 'Combo', hint: 'Bars + a line on two axes' },
  { type: 'pie', label: 'Pie', hint: "One metric's share" },
  { type: 'donut', label: 'Donut', hint: "One metric's share" },
  { type: 'heatmap', label: 'Heatmap', hint: 'Day of week × hour — when calls come in' },
  { type: 'funnel', label: 'Funnel', hint: 'Offered → handled → opportunities → orders' },
]

const TIME = KPI_DIMENSIONS.filter((d) => isTimeDim(d.value))
const CATEGORY = KPI_DIMENSIONS.filter((d) => !isTimeDim(d.value))
const input = 'w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-white focus:outline-none focus:border-sky-500'
const small = 'bg-gray-800 border border-gray-700 rounded px-1.5 py-1 text-xs text-white'

export default function ChartSettings({ value, onChange, customKpis, fieldNames, windowMode }: {
  value: ChartSettingsValue
  onChange: (v: ChartSettingsValue) => void
  customKpis: CustomKpi[]
  fieldNames: { name: string; label: string }[]
  windowMode: string
}) {
  const set = (p: Partial<ChartSettingsValue>) => onChange({ ...value, ...p })
  const t = value.chartType
  const cartesian = ['line', 'area', 'bar', 'hbar', 'stacked', 'combo'].includes(t)
  const single = ['pie', 'donut', 'heatmap'].includes(t)
  const splittable = ['line', 'area', 'bar', 'hbar', 'stacked'].includes(t)
  const metrics = value.chartMetrics.length ? value.chartMetrics : t === 'funnel' ? DEFAULT_FUNNEL : DEFAULT_CHART_METRICS
  const catalog = [
    ...KPI_CATALOG.map((k) => ({ key: k.key, label: k.label, group: k.group, format: k.format })),
    ...customKpis.map((k) => ({ key: `custom:${k.id}`, label: k.name, group: 'Your KPIs', format: customFormat(k.format) as Format })),
  ]
  const formatOf = (key: string): Format => catalog.find((c) => c.key === key)?.format ?? 'int'
  const groups = [...new Set(catalog.map((c) => c.group))]
  const setMetric = (i: number, p: Partial<ChartMetric>) => set({ chartMetrics: metrics.map((m, j) => (j === i ? { ...m, ...p } : m)) })
  const move = (i: number, d: number) => {
    const next = [...metrics]; const j = i + d
    if (j < 0 || j >= next.length) return
    ;[next[i], next[j]] = [next[j], next[i]]
    set({ chartMetrics: next })
  }
  const split = splittable && metrics.length === 1 ? value.groupBy2 : ''
  const compareAllowed = cartesian && isTimeDim(value.groupBy) && !split
  const manyPoints = (value.groupBy === 'interval15' || value.groupBy === 'interval30') && (windowMode === 'week' || windowMode === 'month')

  function pickType(type: ChartType) {
    const p: Partial<ChartSettingsValue> = { chartType: type }
    if (type === 'funnel') p.chartMetrics = value.chartType === 'funnel' ? value.chartMetrics : DEFAULT_FUNNEL
    else if (value.chartType === 'funnel') p.chartMetrics = DEFAULT_CHART_METRICS
    if (['pie', 'donut', 'heatmap'].includes(type)) p.chartMetrics = [metrics[0] ?? DEFAULT_CHART_METRICS[0]]
    if ((type === 'pie' || type === 'donut' || type === 'hbar') && isTimeDim(value.groupBy)) p.groupBy = 'campaign'
    if (['line', 'area', 'combo'].includes(type) && !isTimeDim(value.groupBy)) p.groupBy = 'interval30'
    set(p)
  }

  const metricOptions = (
    <>
      {groups.map((g) => (
        <optgroup key={g} label={g}>
          {catalog.filter((c) => c.group === g).map((c) => <option key={c.key} value={c.key}>{c.label}</option>)}
        </optgroup>
      ))}
    </>
  )

  return (
    <div className="space-y-5">
      <div>
        <label className="block text-xs text-gray-400 mb-1.5">Chart type</label>
        <div className="grid grid-cols-2 sm:grid-cols-5 gap-2">
          {TYPES.map((x) => (
            <button key={x.type} onClick={() => pickType(x.type)} title={x.hint}
              style={t === x.type ? { borderColor: CHART_TYPE_COLOR[x.type], background: `${CHART_TYPE_COLOR[x.type]}1a` } : undefined}
              className={`text-left rounded-lg border px-2.5 py-2 transition-colors ${t === x.type ? '' : 'border-gray-700 hover:border-gray-500'}`}>
              {/* A miniature of the chart, colour-coded (William, S184) */}
              <ChartTypeIcon type={x.type} active={t === x.type} size={44} />
              <div className="text-xs text-white mt-1" style={t === x.type ? { color: CHART_TYPE_COLOR[x.type] } : undefined}>{x.label}</div>
              <div className="text-[10px] text-gray-500 leading-tight mt-0.5">{x.hint}</div>
            </button>
          ))}
        </div>
      </div>

      {t !== 'funnel' && t !== 'heatmap' && (
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div>
            <label className="block text-xs text-gray-400 mb-1">{t === 'hbar' ? 'Bars (one per…)' : t === 'pie' || t === 'donut' ? 'Slices (one per…)' : 'X axis'}</label>
            <select value={value.groupBy} onChange={(e) => set({ groupBy: e.target.value })} className={input}>
              {t !== 'pie' && t !== 'donut' && <optgroup label="Time">{TIME.map((d) => <option key={d.value} value={d.value}>{d.label}</option>)}</optgroup>}
              <optgroup label="Category">{CATEGORY.map((d) => <option key={d.value} value={d.value}>{d.label}</option>)}</optgroup>
              {fieldNames.length > 0 && <optgroup label="Custom field">{fieldNames.map((f) => <option key={f.name} value={`cf:${f.name}`}>{f.label}</option>)}</optgroup>}
            </select>
            {manyPoints && <p className="text-[11px] text-amber-300 mt-1">That's a lot of points over a {windowMode} — Hour or Day may read better.</p>}
          </div>
          {splittable && (
            <div>
              <label className="block text-xs text-gray-400 mb-1">Split into series by (optional)</label>
              <select value={split} disabled={metrics.length > 1} onChange={(e) => set({ groupBy2: e.target.value })} className={input}>
                <option value="">—</option>
                {CATEGORY.filter((d) => d.value !== value.groupBy).map((d) => <option key={d.value} value={d.value}>{d.label}</option>)}
                {fieldNames.filter((f) => `cf:${f.name}` !== value.groupBy).map((f) => <option key={f.name} value={`cf:${f.name}`}>{f.label}</option>)}
              </select>
              <p className="text-[11px] text-gray-500 mt-1">
                {metrics.length > 1 ? 'A split works with one metric — remove the others to use it.' : 'One line or bar per value, e.g. per campaign.'}
              </p>
            </div>
          )}
        </div>
      )}
      {t === 'heatmap' && <p className="text-xs text-gray-400">Rows are days of the week, columns hours of the day — best over a week or a month.</p>}

      <div>
        <label className="block text-xs text-gray-400 mb-1">{t === 'funnel' ? 'Funnel steps (top to bottom)' : single ? 'Metric' : 'Metrics'}</label>
        <div className="space-y-1.5">
          {metrics.map((m, i) => (
            <div key={i} className="flex flex-wrap items-center gap-2">
              <select value={m.key} onChange={(e) => setMetric(i, { key: e.target.value, axis: undefined })} className={`${small} flex-1 min-w-[12rem]`}>
                {metricOptions}
              </select>
              {cartesian && !split && (
                <select value={m.axis ?? defaultAxis(formatOf(m.key))} onChange={(e) => setMetric(i, { axis: e.target.value as 'left' | 'right' })} className={small} title="Which Y axis">
                  <option value="left">Left axis</option>
                  <option value="right">Right axis</option>
                </select>
              )}
              {t === 'combo' && (
                <select value={m.as ?? (i === 0 ? 'bar' : 'line')} onChange={(e) => setMetric(i, { as: e.target.value as 'bar' | 'line' })} className={small}>
                  <option value="bar">Bars</option>
                  <option value="line">Line</option>
                </select>
              )}
              {value.showTargets && cartesian && (
                <div className="w-28" title="Target line">
                  <KpiTargetInput value={value.targets[m.key]?.good} format={formatOf(m.key)} placeholder="target"
                    onChange={(v) => set({ targets: { ...value.targets, [m.key]: { ...(value.targets[m.key] ?? { higherIsBetter: true }), good: v } } })} />
                </div>
              )}
              {t === 'funnel' && (
                <>
                  <button className="text-gray-500 hover:text-white px-1 disabled:opacity-30" disabled={i === 0} onClick={() => move(i, -1)} title="Move up"><ChevronUpIcon size={13} /></button>
                  <button className="text-gray-500 hover:text-white px-1 disabled:opacity-30" disabled={i === metrics.length - 1} onClick={() => move(i, 1)} title="Move down"><ChevronDownIcon size={13} /></button>
                </>
              )}
              {metrics.length > 1 && (
                <button className="text-gray-500 hover:text-red-300 px-1" onClick={() => set({ chartMetrics: metrics.filter((_, j) => j !== i) })} title="Remove"><DeleteIcon size={13} /></button>
              )}
            </div>
          ))}
        </div>
        {!single && metrics.length < 8 && (
          <button className="mt-2 text-xs text-sky-300 hover:text-sky-200"
            onClick={() => set({ chartMetrics: [...metrics, { key: t === 'funnel' ? 'netOrders' : 'serviceLevel' }], groupBy2: '' })}>
            + Add {t === 'funnel' ? 'step' : 'metric'}
          </button>
        )}
      </div>

      {cartesian && (
        <div className="space-y-2">
          <label className={`flex items-center gap-2 text-sm ${compareAllowed ? 'text-gray-300' : 'text-gray-600'}`}>
            <input type="checkbox" checked={value.compare && compareAllowed} disabled={!compareAllowed} onChange={(e) => set({ compare: e.target.checked })} />
            Compare to the previous period (dashed)
            <span className="text-[11px] text-gray-500">
              {compareAllowed ? '— today vs. the same hours yesterday, this week vs. last week…' : '— needs a time X axis and no series split'}
            </span>
          </label>
          <label className="flex items-center gap-2 text-sm text-gray-300">
            <input type="checkbox" checked={value.showTargets} onChange={(e) => set({ showTargets: e.target.checked })} />
            Show target lines <span className="text-[11px] text-gray-500">— set a target next to each metric</span>
          </label>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 max-w-xl">
        <div>
          <label className="block text-xs text-gray-400 mb-1">Revenue</label>
          <select value={value.revenueBasis} onChange={(e) => set({ revenueBasis: e.target.value as ChartSettingsValue['revenueBasis'] })} className={input}>
            <option value="gross">Gross (incl. tax)</option>
            <option value="exclTax">Excluding tax</option>
            <option value="merch">Merchandise only</option>
          </select>
        </div>
        <label className="flex items-end gap-2 text-sm text-gray-300 pb-1.5">
          <input type="checkbox" checked={value.netRevenue} onChange={(e) => set({ netRevenue: e.target.checked })} />
          Net revenue only
        </label>
      </div>
    </div>
  )
}

/** The saved config for a chart: the query shape each type needs (heatmap = day × hour, funnel = totals). */
export function chartConfig(v: ChartSettingsValue): Partial<KpiWidgetConfig> & Record<string, unknown> {
  const metrics = v.chartMetrics.length ? v.chartMetrics : v.chartType === 'funnel' ? DEFAULT_FUNNEL : DEFAULT_CHART_METRICS
  const single = ['pie', 'donut', 'heatmap'].includes(v.chartType)
  const splittable = ['line', 'area', 'bar', 'hbar', 'stacked'].includes(v.chartType)
  const groupBy = v.chartType === 'heatmap' ? 'dow' : v.chartType === 'funnel' ? 'none' : v.groupBy
  const groupBy2 = v.chartType === 'heatmap' ? 'hour' : splittable && metrics.length === 1 && v.groupBy2 && v.groupBy2 !== groupBy ? v.groupBy2 : undefined
  const compare = v.compare && ['line', 'area', 'bar', 'hbar', 'stacked', 'combo'].includes(v.chartType) && isTimeDim(groupBy) && !groupBy2
  const keys = (single ? metrics.slice(0, 1) : metrics).map((m) => m.key)
  return {
    chartType: v.chartType,
    chartMetrics: single ? metrics.slice(0, 1) : metrics,
    groupBy, groupBy2,
    compare: compare || undefined,
    showTargets: v.showTargets || undefined,
    targets: Object.fromEntries(Object.entries(v.targets).filter(([k, t]) => keys.includes(k) && t.good != null)),
    revenueBasis: v.revenueBasis,
    netRevenue: v.netRevenue,
  }
}
