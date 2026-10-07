import { useEffect, useState } from 'react'
import SearchableSelect from '../SearchableSelect'
import { listClients, listCampaigns, listAgentGroups } from '../../api/telephony'
import type { KpiWidgetConfig, TimeWindowConfig, WidgetFilterConfig, WidgetFilterFields } from '../../types/dashboard'
import { KPI_CATALOG, DEFAULT_KPIS, KPI_DIMENSIONS, customFormat, type Format } from './widgets/KpiWidget'
import KpiTargetInput from './KpiTargetInput'
import RecordsColumnsEditor from './RecordsColumnsEditor'
import DnisPicker from './DnisPicker'
import { dashboardWidgetsApi, type RecordColumn } from '../../api/dashboardWidgets'
import { customFieldsApi } from '../../api/customFields'
import type { KpiTarget } from '../../types/dashboard'
import { customKpisApi, type CustomKpi } from '../../api/dashboardWidgets'

interface Props {
  title: string
  fields: WidgetFilterFields
  initial: WidgetFilterConfig
  initialWidgetTitle: string
  onSave: (config: WidgetFilterConfig, widgetTitle: string | undefined) => void
  onClose: () => void
  /** Client dashboard (S181): only these campaigns can be picked (empty = the client's own, via clientId). */
  scope?: { clientId: string; campaignIds: string[] }
}

const DEFAULT_HOURS = 1
const DEFAULT_MINUTES = 30

// Client OR campaign (a campaign implies its client); agent group and numbers dialed narrow on top of either (S181).
export default function WidgetConfigModal({ title, fields: rawFields, initial, initialWidgetTitle, onSave, onClose, scope }: Props) {
  // On a client dashboard the client is fixed by the dashboard, so the widget can't pick one.
  const fields = scope ? { ...rawFields, client: false } : rawFields
  const [clients, setClients] = useState<{ id: string; name: string }[]>([])
  const [campaigns, setCampaigns] = useState<{ id: string; name: string }[]>([])
  const [groups, setGroups] = useState<{ id: string; name: string }[]>([])
  const [widgetTitle, setWidgetTitle] = useState(initialWidgetTitle)
  const [clientId, setClientId] = useState(initial.clientId ?? '')
  const [campaignId, setCampaignId] = useState(initial.campaignId ?? '')
  const [groupId, setGroupId] = useState(initial.groupId ?? '')
  const [dnis, setDnis] = useState<string[]>(initial.dnis ?? [])
  const [loggedInOnly, setLoggedInOnly] = useState(initial.loggedInOnly ?? false)
  const [timeWindowMode, setTimeWindowMode] = useState<TimeWindowConfig['mode']>(initial.timeWindow?.mode ?? 'today')
  const [groupBy, setGroupBy] = useState<string>(initial.groupBy ?? 'none')
  const [groupBy2, setGroupBy2] = useState<string>(initial.groupBy2 ?? '')
  const [percentOfTotal, setPercentOfTotal] = useState(initial.percentOfTotal ?? false)
  const [targets, setTargets] = useState<Record<string, KpiTarget>>(initial.targets ?? {})
  const [fieldNames, setFieldNames] = useState<{ name: string; label: string }[]>([])
  const [kpis, setKpis] = useState<string[]>(initial.kpis ?? DEFAULT_KPIS)
  const [revenueBasis, setRevenueBasis] = useState<NonNullable<KpiWidgetConfig['revenueBasis']>>(initial.revenueBasis ?? 'exclTax')
  const [netRevenue, setNetRevenue] = useState(initial.netRevenue ?? false)
  const [customKpis, setCustomKpis] = useState<CustomKpi[]>([])
  const [tab, setTab] = useState<'data' | 'layout' | 'kpis' | 'columns'>('data')
  const [recordColumns, setRecordColumns] = useState<RecordColumn[]>([])
  const [columns, setColumns] = useState<string[]>(initial.columns ?? [])
  const [pageSize, setPageSize] = useState(initial.pageSize ?? 25)
  const [allowRecordings, setAllowRecordings] = useState(initial.allowRecordings !== false)
  const tabbed = !!(fields.kpi || fields.records)
  const [timeWindowValue, setTimeWindowValue] = useState(
    initial.timeWindow?.value ?? (initial.timeWindow?.mode === 'minutes' ? DEFAULT_MINUTES : DEFAULT_HOURS),
  )

  useEffect(() => {
    listClients().then(setClients).catch(() => {})
    listCampaigns(scope?.clientId)
      .then((c) => setCampaigns(scope?.campaignIds.length ? c.filter((x) => scope.campaignIds.includes(x.id)) : c))
      .catch(() => {})
    listAgentGroups().then(setGroups).catch(() => {})
    if (fields.kpi) customKpisApi.list().then((k) => setCustomKpis(k.filter((x) => x.isActive))).catch(() => {})
    if (fields.records) dashboardWidgetsApi.recordColumns().then(setRecordColumns).catch(() => {})
    if (fields.kpi || fields.records) customFieldsApi.listDefinitions()
      .then((d) => setFieldNames([...new Map(d.filter((x) => x.isActive).map((x) => [x.fieldName, { name: x.fieldName, label: x.displayLabel }])).values()]))
      .catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fields.kpi, fields.records])

  function handleSave() {
    onSave({
      clientId: clientId || undefined,
      campaignId: campaignId || undefined,
      groupId: fields.group ? groupId || undefined : undefined,
      dnis: fields.dnis && dnis.length ? dnis : undefined,
      loggedInOnly: loggedInOnly || undefined,
      timeWindow: fields.timeWindow
        ? (timeWindowMode === 'hours' || timeWindowMode === 'minutes' ? { mode: timeWindowMode, value: timeWindowValue } : { mode: timeWindowMode })
        : undefined,
      ...(fields.kpi ? {
        groupBy, groupBy2: groupBy !== 'none' && groupBy2 && groupBy2 !== groupBy ? groupBy2 : undefined, kpis, revenueBasis, netRevenue,
        percentOfTotal: percentOfTotal || undefined,
        targets: Object.fromEntries(Object.entries(targets).filter(([k, t]) => kpis.includes(k) && (t.good != null || t.warn != null))),
      } : {}),
      ...(fields.records ? {
        columns: columns.length ? columns : undefined,
        pageSize,
        allowRecordings,
      } : {}),
    }, widgetTitle.trim() || undefined)
    onClose()
  }

  const input = 'w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-white focus:outline-none focus:border-sky-500'

  const dataSection = (
    <div className="space-y-3">
      <div>
        <label className="block text-xs text-gray-400 mb-1">Widget Title</label>
        <input
          type="text"
          value={widgetTitle}
          onChange={(e) => setWidgetTitle(e.target.value)}
          placeholder="Leave blank to use the default name"
          className="w-full bg-gray-800 border border-gray-700 rounded px-3 py-1.5 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
        />
        <p className="text-[11px] text-gray-500 mt-1">Handy once a filter below narrows this to one client or campaign — rename it so the tile says what it's actually showing.</p>
      </div>
      {fields.client && (
        <div>
          <label className="block text-xs text-gray-400 mb-1">Client</label>
          <SearchableSelect
            options={clients.map((c) => ({ value: c.id, label: c.name }))}
            value={clientId}
            onChange={(v) => { setClientId(v); if (v) setCampaignId('') }}
            allLabel="All clients"
            className="w-full"
          />
        </div>
      )}
      {fields.campaign && (
        <div>
          <label className="block text-xs text-gray-400 mb-1">Campaign</label>
          <SearchableSelect
            options={campaigns.map((c) => ({ value: c.id, label: c.name }))}
            value={campaignId}
            onChange={(v) => { setCampaignId(v); if (v) setClientId('') }}
            allLabel="All campaigns"
            className="w-full"
          />
        </div>
      )}
      {fields.group && (
        <div>
          <label className="block text-xs text-gray-400 mb-1">Agent Group</label>
          <SearchableSelect
            options={groups.map((g) => ({ value: g.id, label: g.name }))}
            value={groupId}
            onChange={setGroupId}
            allLabel="All groups"
            className="w-full"
          />
        </div>
      )}
      {fields.dnis && (
        <DnisPicker clientId={clientId || scope?.clientId} campaignId={campaignId || undefined} value={dnis} onChange={setDnis} />
      )}
      {fields.loggedInOnly && (
        <label className="flex items-center gap-2 text-sm text-gray-300 pt-1">
          <input
            type="checkbox"
            checked={loggedInOnly}
            onChange={(e) => setLoggedInOnly(e.target.checked)}
            className="rounded"
          />
          Logged in only
        </label>
      )}
      {fields.timeWindow && (
        <div>
          <label className="block text-xs text-gray-400 mb-1">Time Window</label>
          <div className="flex items-center gap-2">
            <select
              value={timeWindowMode}
              onChange={(e) => {
                const mode = e.target.value as TimeWindowConfig['mode']
                setTimeWindowMode(mode)
                if (mode === 'hours') setTimeWindowValue(DEFAULT_HOURS)
                else if (mode === 'minutes') setTimeWindowValue(DEFAULT_MINUTES)
              }}
              className="bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-white focus:outline-none focus:border-sky-500"
            >
              <option value="today">Today</option>
              {(fields.kpi || fields.records) && <option value="yesterday">Yesterday</option>}
              {(fields.kpi || fields.records) && <option value="week">This week (from Monday)</option>}
              {(fields.kpi || fields.records) && <option value="month">This month</option>}
              <option value="hours">Last N hours</option>
              <option value="minutes">Last N minutes</option>
            </select>
            {(timeWindowMode === 'hours' || timeWindowMode === 'minutes') && (
              <input
                type="number"
                min={1}
                value={timeWindowValue}
                onChange={(e) => setTimeWindowValue(Math.max(1, Number(e.target.value)))}
                className="w-20 bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-white focus:outline-none focus:border-sky-500"
              />
            )}
          </div>
          <p className="text-[11px] text-gray-500 mt-1">
            {timeWindowMode === 'hours' || timeWindowMode === 'minutes'
              ? 'A moving window ending now — never resets.'
              : "Calendar days in the tenant's timezone."}
          </p>
        </div>
      )}
    </div>
  )

  const dimensionOptions = (exclude?: string) => (
    <>
      {KPI_DIMENSIONS.filter((d) => d.value !== exclude).map((d) => <option key={d.value} value={d.value}>{d.label}</option>)}
      {fieldNames.length > 0 && <optgroup label="Custom field">
        {fieldNames.filter((f) => `cf:${f.name}` !== exclude).map((f) => <option key={f.name} value={`cf:${f.name}`}>{f.label}</option>)}
      </optgroup>}
    </>
  )

  const layoutSection = (
    <div className="space-y-4 max-w-xl">
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <div>
          <label className="block text-xs text-gray-400 mb-1">Break down by</label>
          <select value={groupBy} onChange={(e) => setGroupBy(e.target.value)} className={input}>
            <option value="none">Totals only (tiles)</option>
            {dimensionOptions()}
          </select>
          <p className="text-[11px] text-gray-500 mt-1">Totals only shows one tile per KPI; anything else shows a table.</p>
        </div>
        <div>
          <label className="block text-xs text-gray-400 mb-1">Then by (optional)</label>
          <select value={groupBy2} onChange={(e) => setGroupBy2(e.target.value)} className={input} disabled={groupBy === 'none'}>
            <option value="">—</option>
            {dimensionOptions(groupBy)}
          </select>
          <p className="text-[11px] text-gray-500 mt-1">A second level under each row, with a subtotal.</p>
        </div>
      </div>
      <label className={`flex items-center gap-2 text-sm text-gray-300 ${groupBy === 'none' ? 'opacity-40' : ''}`}>
        <input type="checkbox" checked={percentOfTotal} disabled={groupBy === 'none'} onChange={(e) => setPercentOfTotal(e.target.checked)} />
        Show each row's % of the total next to counts
      </label>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <div>
          <label className="block text-xs text-gray-400 mb-1">Revenue</label>
          <select value={revenueBasis} onChange={(e) => setRevenueBasis(e.target.value as typeof revenueBasis)} className={input}>
            <option value="gross">Gross (incl. tax)</option>
            <option value="exclTax">Excluding tax</option>
            <option value="merch">Merchandise only</option>
          </select>
        </div>
      </div>
      <label className="flex items-center gap-2 text-sm text-gray-300">
        <input type="checkbox" checked={netRevenue} onChange={(e) => setNetRevenue(e.target.checked)} />
        Net revenue (only orders whose payment went through)
      </label>
    </div>
  )

  const kpiRows: { key: string; label: string; group: string; format: Format }[] = [
    ...KPI_CATALOG.map((k) => ({ key: k.key, label: k.label, group: k.group, format: k.format })),
    ...customKpis.map((k) => ({ key: `custom:${k.id}`, label: k.name, group: 'Your KPIs', format: customFormat(k.format) })),
  ]
  const kpiGroups = [...new Set(kpiRows.map((k) => k.group))]
  // Sensible default direction for a new target — times, abandons and data-quality counts are better when lower.
  const lowerIsBetter = /abandon|aht|avgTalk|avgAcw|declines|unmapped|saleWithoutOrder/i
  const cols = 'grid grid-cols-[1.25rem_minmax(8rem,1fr)_6.5rem_6.5rem_5.5rem] gap-2'

  const kpisSection = (
    <div>
      <p className="text-[11px] text-gray-500 mb-2">
        Tick the KPIs to show. Targets are optional: a value that meets <span className="text-emerald-400">good</span> shows green,
        one that meets <span className="text-amber-300">warn</span> shows amber, anything else red.
        Enter targets in the KPI's own units — times as minutes:seconds (2:30), or a plain number of minutes (2.5).
      </p>
      <div className="border border-gray-800 rounded overflow-x-auto">
        <div className="min-w-[34rem]">
          <div className={`${cols} px-2 py-1.5 text-[10px] uppercase tracking-wide text-gray-500 border-b border-gray-800`}>
            <span /><span>KPI</span><span>Good</span><span>Warn</span><span>Better when</span>
          </div>
          {kpiGroups.map((g) => (
            <div key={g}>
              <div className="px-2 pt-2 pb-1 text-[10px] uppercase tracking-wide text-gray-600">{g}</div>
              {kpiRows.filter((k) => k.group === g).map((k) => {
                const on = kpis.includes(k.key)
                const t = targets[k.key] ?? { higherIsBetter: !lowerIsBetter.test(k.key) }
                const set = (p: Partial<KpiTarget>) => setTargets({ ...targets, [k.key]: { ...t, ...p } })
                return (
                  <div key={k.key} className={`${cols} items-center px-2 py-0.5 hover:bg-gray-800/40`}>
                    <input type="checkbox" checked={on}
                      onChange={(e) => setKpis(e.target.checked ? [...kpis, k.key] : kpis.filter((x) => x !== k.key))} />
                    <span className={`text-xs truncate ${on ? 'text-gray-200' : 'text-gray-500'}`} title={k.label}>{k.label}</span>
                    <KpiTargetInput value={t.good} format={k.format} placeholder="e.g." disabled={!on} onChange={(v) => set({ good: v })} />
                    <KpiTargetInput value={t.warn} format={k.format} placeholder="e.g." disabled={!on} onChange={(v) => set({ warn: v })} />
                    <select value={t.higherIsBetter ? 'up' : 'down'} disabled={!on} onChange={(e) => set({ higherIsBetter: e.target.value === 'up' })}
                      className={`bg-gray-800 border border-gray-700 rounded px-1 py-0.5 text-[11px] text-white ${on ? '' : 'opacity-40'}`}>
                      <option value="up">Higher</option>
                      <option value="down">Lower</option>
                    </select>
                  </div>
                )
              })}
            </div>
          ))}
        </div>
      </div>
    </div>
  )

  const TABS = fields.records
    ? [{ key: 'data' as const, label: 'Data' }, { key: 'columns' as const, label: `Columns (${columns.length || 'default'})` }]
    : [
      { key: 'data' as const, label: 'Data' },
      { key: 'layout' as const, label: 'Layout' },
      { key: 'kpis' as const, label: `KPIs & targets (${kpis.length})` },
    ]

  const columnsSection = (
    <RecordsColumnsEditor
      available={[...recordColumns, ...fieldNames.map((f) => ({ key: `cf:${f.name}`, label: f.label, group: 'Custom fields' }))]}
      columns={columns} pageSize={pageSize} allowRecordings={allowRecordings}
      onChange={(p) => {
        if (p.columns) setColumns(p.columns)
        if (p.pageSize) setPageSize(p.pageSize)
        if (p.allowRecordings !== undefined) setAllowRecordings(p.allowRecordings)
      }}
    />
  )

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" onClick={onClose}>
      <div
        className={`bg-gray-900 border border-gray-800 rounded-xl p-5 shadow-xl flex flex-col max-h-[90vh] w-full ${tabbed ? 'max-w-4xl' : 'max-w-sm'}`}
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="text-sm font-semibold text-white mb-1">{title}</h3>
        <p className="text-xs text-gray-500 mb-3">Filter which data this widget shows — every filter you set applies.</p>

        {tabbed && (
          <div className="flex gap-1 border-b border-gray-800 mb-4">
            {TABS.map((t) => (
              <button key={t.key} onClick={() => setTab(t.key)}
                className={`text-xs px-3 py-1.5 -mb-px border-b-2 transition-colors ${tab === t.key ? 'border-sky-500 text-white' : 'border-transparent text-gray-400 hover:text-gray-200'}`}>
                {t.label}
              </button>
            ))}
          </div>
        )}

        <div className="overflow-y-auto min-h-0 flex-1 pr-1">
          {!tabbed && dataSection}
          {tabbed && tab === 'data' && <div className="max-w-xl">{dataSection}</div>}
          {fields.records && tab === 'columns' && columnsSection}
          {fields.kpi && tab === 'layout' && layoutSection}
          {fields.kpi && tab === 'kpis' && kpisSection}
        </div>

        <div className="flex justify-end gap-2 pt-4">
          <button
            onClick={onClose}
            className="text-sm text-gray-300 border border-gray-700 hover:border-gray-500 px-4 py-1.5 rounded-lg transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleSave}
            className="text-sm bg-blue-600 hover:bg-blue-700 text-white px-4 py-1.5 rounded-lg transition-colors"
          >
            Save
          </button>
        </div>
      </div>
    </div>
  )
}
