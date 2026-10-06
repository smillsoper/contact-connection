import { useCallback, useEffect, useRef, useState } from 'react'
import { dashboardWidgetsApi, type KpiMetrics, type KpiResult } from '../../../api/dashboardWidgets'
import type { KpiTarget, WidgetFilterConfig } from '../../../types/dashboard'
import { useDashboardLiveAgentSessions, useDashboardLiveAgentState, useDashboardLiveCallState } from '../DashboardLiveContext'

// KPI widget (S181, docs/dispositions-kpi-plan.md): CRM + telephony KPIs together — close rates from the disposition
// catalog, revenue from orders, handling from call / agent state history. Production calls only; interactions counted by
// their own campaign. Refreshes on dashboard pushes (script finished, call state, agent state) — never polls.

type Format = 'pct' | 'int' | 'money' | 'num' | 'secs' | 'hours'

interface KpiDef {
  key: string
  label: string
  group: string
  format: Format
  /** For revenue KPIs: which field of the chosen revenue basis. */
  revenue?: 'total' | 'perCall' | 'perOpportunity' | 'averageOrder' | 'perAgentHour' | 'perTalkHour'
  value?: (m: KpiMetrics) => number | null
  hint?: string
}

export const KPI_CATALOG: KpiDef[] = [
  { key: 'grossCloseRate', label: 'Gross close rate', group: 'Sales', format: 'pct', value: (m) => m.grossCloseRate, hint: 'Orders on sales opportunities ÷ sales opportunities' },
  { key: 'netCloseRate', label: 'Net close rate', group: 'Sales', format: 'pct', value: (m) => m.netCloseRate, hint: 'Orders whose payment went through ÷ sales opportunities' },
  { key: 'rawCloseRate', label: 'Raw close rate', group: 'Sales', format: 'pct', value: (m) => m.rawCloseRate, hint: 'Orders ÷ all interactions' },
  { key: 'orders', label: 'Orders', group: 'Sales', format: 'int', value: (m) => m.orders },
  { key: 'netOrders', label: 'Net orders', group: 'Sales', format: 'int', value: (m) => m.netOrders },
  { key: 'declines', label: 'Declines', group: 'Sales', format: 'int', value: (m) => m.declines },
  { key: 'opportunities', label: 'Sales opportunities', group: 'Sales', format: 'int', value: (m) => m.opportunities },
  { key: 'interactions', label: 'Interactions', group: 'Sales', format: 'int', value: (m) => m.interactions },
  { key: 'revenue', label: 'Revenue', group: 'Revenue', format: 'money', revenue: 'total' },
  { key: 'revenuePerCall', label: 'Revenue per call', group: 'Revenue', format: 'money', revenue: 'perCall' },
  { key: 'revenuePerOpportunity', label: 'Revenue per opportunity', group: 'Revenue', format: 'money', revenue: 'perOpportunity' },
  { key: 'averageOrder', label: 'Average order', group: 'Revenue', format: 'money', revenue: 'averageOrder' },
  { key: 'revenuePerAgentHour', label: 'Revenue per agent hour', group: 'Revenue', format: 'money', revenue: 'perAgentHour', hint: 'Revenue ÷ the hours its agents were logged in' },
  { key: 'revenuePerTalkHour', label: 'Revenue per talk hour', group: 'Revenue', format: 'money', revenue: 'perTalkHour' },
  { key: 'upsellTakeRate', label: 'Upsell take rate', group: 'Revenue', format: 'pct', value: (m) => m.upsellTakeRate },
  { key: 'unitsPerOrder', label: 'Units per order', group: 'Revenue', format: 'num', value: (m) => m.unitsPerOrder },
  { key: 'callsOffered', label: 'Calls offered', group: 'Handling', format: 'int', value: (m) => m.callsOffered },
  { key: 'callsHandled', label: 'Calls handled', group: 'Handling', format: 'int', value: (m) => m.callsHandled },
  { key: 'callsAbandoned', label: 'Abandoned', group: 'Handling', format: 'int', value: (m) => m.callsAbandoned },
  { key: 'abandonRate', label: 'Abandon rate', group: 'Handling', format: 'pct', value: (m) => m.abandonRate },
  { key: 'serviceLevel', label: 'Service level', group: 'Handling', format: 'pct', value: (m) => m.serviceLevel },
  { key: 'aht', label: 'AHT', group: 'Handling', format: 'secs', value: (m) => m.ahtSeconds, hint: 'Average talk + average after-call work' },
  { key: 'avgTalk', label: 'Avg talk', group: 'Handling', format: 'secs', value: (m) => m.avgTalkSeconds },
  { key: 'avgAcw', label: 'Avg after-call work', group: 'Handling', format: 'secs', value: (m) => m.avgAcwSeconds },
  { key: 'loggedInHours', label: 'Agent hours logged in', group: 'Handling', format: 'hours', value: (m) => m.loggedInHours },
  { key: 'saleWithoutOrder', label: 'Sale dispositions, no order', group: 'Data quality', format: 'int', value: (m) => m.saleWithoutOrder, hint: 'Dispositioned as a sale but no order went through' },
  { key: 'unmapped', label: 'Unmapped dispositions', group: 'Data quality', format: 'int', value: (m) => m.unmapped },
]

/** Report dimensions (S181) — what a KPI table can be broken down by. Custom fields are added as "cf:<name>". */
export const KPI_DIMENSIONS: { value: string; label: string }[] = [
  { value: 'campaign', label: 'Campaign' }, { value: 'client', label: 'Client' }, { value: 'agent', label: 'Agent' },
  { value: 'disposition', label: 'Disposition' }, { value: 'category', label: 'Reporting category' },
  { value: 'day', label: 'Day' }, { value: 'hour', label: 'Hour of day' },
  { value: 'agency', label: 'Media agency' }, { value: 'station', label: 'Station' }, { value: 'dnis', label: 'Number dialed (DNIS)' },
]

export function dimensionLabel(d: string | undefined) {
  if (!d) return ''
  if (d.startsWith('cf:')) return d.slice(3)
  return KPI_DIMENSIONS.find((x) => x.value === d)?.label ?? d
}

/** Green when the value meets "good", amber when it meets "warning", red otherwise — in the KPI's displayed units. */
export function targetColor(value: number | null | undefined, t: KpiTarget | undefined): string {
  if (value == null || !t || t.good == null) return ''
  const meets = (limit: number) => t.higherIsBetter ? value >= limit : value <= limit
  if (meets(t.good)) return 'text-emerald-400'
  if (t.warn != null && meets(t.warn)) return 'text-amber-300'
  return 'text-red-400'
}

export const DEFAULT_KPIS = ['grossCloseRate', 'netCloseRate', 'orders', 'revenue', 'revenuePerCall', 'averageOrder',
  'revenuePerAgentHour', 'callsOffered', 'serviceLevel', 'aht']

function fmt(v: number | null | undefined, f: Format) {
  if (v == null) return '—'
  switch (f) {
    case 'pct': return `${v.toFixed(1)}%`
    case 'int': return v.toLocaleString()
    case 'money': return `$${v.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
    case 'num': return v.toFixed(2)
    case 'hours': return `${v.toFixed(1)} h`
    case 'secs': {
      const s = Math.round(v)
      return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
    }
  }
}

export default function KpiWidget({ config }: { config: WidgetFilterConfig }) {
  const [data, setData] = useState<KpiResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const debounce = useRef<ReturnType<typeof setTimeout> | null>(null)
  const sessions = useDashboardLiveAgentSessions()
  const callState = useDashboardLiveCallState()
  const agentState = useDashboardLiveAgentState()

  const load = useCallback(() => {
    dashboardWidgetsApi.kpi(config).then((d) => { setData(d); setError(null) })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.campaignId, config.clientId, config.groupBy, config.groupBy2, config.timeWindow?.mode, config.timeWindow?.value])

  useEffect(() => { load() }, [load])

  // A finished script (orders, dispositions), a call changing state or an agent changing state can move these numbers —
  // debounced so a burst of events refetches once.
  useEffect(() => {
    if (!sessions && !callState && !agentState) return
    if (debounce.current) clearTimeout(debounce.current)
    debounce.current = setTimeout(load, 1000)
    return () => { if (debounce.current) clearTimeout(debounce.current) }
  }, [sessions, callState, agentState, load])

  if (error) return <div className="text-xs text-red-400">{error}</div>
  if (!data) return <div className="text-xs text-gray-500">Loading…</div>

  const basis = config.revenueBasis ?? 'exclTax'
  const net = config.netRevenue ?? false
  const custom = data.total.custom
  const defs: KpiDef[] = (config.kpis ?? DEFAULT_KPIS).flatMap((key) => {
    if (key.startsWith('custom:')) {
      const id = key.slice(7)
      const k = custom.find((c) => c.id === id)
      const format: Format = !k ? 'pct' : k.format === 'currency' ? 'money' : k.format === 'duration' ? 'secs'
        : k.format === 'integer' ? 'int' : k.format === 'number' ? 'num' : 'pct'
      return k ? [{ key, label: k.name, group: 'Your KPIs', format,
        value: (m: KpiMetrics) => m.custom.find((c) => c.id === id)?.value ?? null }] : []
    }
    const d = KPI_CATALOG.find((x) => x.key === key)
    return d ? [d] : []
  })
  const valueOf = (d: KpiDef, m: KpiMetrics) => {
    if (d.revenue) {
      const r = m[basis]
      return d.revenue === 'total' && net ? r.net : r[d.revenue]
    }
    return d.value?.(m) ?? null
  }
  const basisLabel = `${basis === 'gross' ? 'gross' : basis === 'merch' ? 'merchandise' : 'excl. tax'}${net ? ', net' : ''}`
  const footer = (
    <p className="text-[10px] text-gray-600 mt-1 shrink-0">
      Production calls · revenue {basisLabel}
      {data.total.unmapped > 0 && <span className="text-amber-400"> · {data.total.unmapped} unmapped disposition{data.total.unmapped === 1 ? '' : 's'}</span>}
    </p>
  )

  if ((config.groupBy ?? 'none') === 'none' || data.rows.length === 0) {
    return (
      <div className="h-full flex flex-col">
        <div className="flex-1 min-h-0 overflow-y-auto grid gap-2" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(118px, 1fr))' }}>
          {defs.map((d) => {
            const v = valueOf(d, data.total)
            const color = targetColor(v, config.targets?.[d.key])
            return (
              <div key={d.key} className="bg-gray-800/50 rounded-lg px-2.5 py-2" title={d.hint}>
                <div className="text-[11px] text-gray-400 leading-tight">{d.label}</div>
                <div className={`text-lg font-semibold tabular-nums ${color || 'text-white'}`}>{fmt(v, d.format)}</div>
              </div>
            )
          })}
        </div>
        {footer}
      </div>
    )
  }

  // Report table (S181): one or two dimensions, subtotals per first-level group, "% of total" beside count KPIs.
  const twoLevel = !!config.groupBy2 && data.rows.some((r) => r.label2 != null)
  const pct = (d: KpiDef) => !!config.percentOfTotal && d.format === 'int'
  const cells = (m: KpiMetrics, strong: boolean) => defs.flatMap((d) => {
    const v = valueOf(d, m)
    const color = targetColor(v, config.targets?.[d.key])
    const out = [<td key={d.key} className={`py-1 px-2 text-right tabular-nums ${color}`}>{fmt(v, d.format)}</td>]
    if (pct(d)) {
      const total = valueOf(d, data.total)
      out.push(<td key={`${d.key}%`} className={`py-1 px-2 text-right tabular-nums ${strong ? '' : 'text-gray-500'}`}>
        {v == null || !total ? '—' : `${((v / total) * 100).toFixed(1)}%`}</td>)
    }
    return out
  })
  let lastGroup: string | null = null

  return (
    <div className="h-full flex flex-col">
      <div className="flex-1 min-h-0 overflow-auto">
        <table className="w-full text-xs">
          <thead className="sticky top-0 bg-gray-900">
            <tr className="text-left text-gray-500 border-b border-gray-800">
              <th className="py-1 pr-2 font-medium">{dimensionLabel(config.groupBy)}</th>
              {twoLevel && <th className="py-1 pr-2 font-medium">{dimensionLabel(config.groupBy2)}</th>}
              {defs.flatMap((d) => [
                <th key={d.key} className="py-1 px-2 font-medium text-right whitespace-nowrap" title={d.hint}>{d.label}</th>,
                ...(pct(d) ? [<th key={`${d.key}%`} className="py-1 px-2 font-medium text-right whitespace-nowrap">% of total</th>] : []),
              ])}
            </tr>
          </thead>
          <tbody>
            {data.rows.map((r) => {
              const showGroup = r.label !== lastGroup
              lastGroup = r.label
              return (
                <tr key={r.key} className={r.subtotal ? 'border-b border-gray-700 text-gray-100 font-semibold bg-gray-800/40' : 'border-b border-gray-800/60 text-gray-300'}>
                  <td className="py-1 pr-2 whitespace-nowrap">{r.subtotal ? `${r.label} subtotal` : twoLevel && !showGroup ? '' : r.label}</td>
                  {twoLevel && <td className="py-1 pr-2 whitespace-nowrap">{r.subtotal ? '' : r.label2}</td>}
                  {cells(r.metrics, r.subtotal)}
                </tr>
              )
            })}
            <tr className="text-white font-semibold">
              <td className="py-1 pr-2">Total</td>
              {twoLevel && <td />}
              {cells(data.total, true)}
            </tr>
          </tbody>
        </table>
      </div>
      {footer}
    </div>
  )
}
