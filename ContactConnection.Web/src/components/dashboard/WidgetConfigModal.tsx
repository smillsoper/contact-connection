import { useEffect, useState } from 'react'
import SearchableSelect from '../SearchableSelect'
import { listClients, listCampaigns, listAgentGroups } from '../../api/telephony'
import type { KpiWidgetConfig, TimeWindowConfig, WidgetFilterConfig, WidgetFilterFields } from '../../types/dashboard'
import { KPI_CATALOG, DEFAULT_KPIS, KPI_DIMENSIONS } from './widgets/KpiWidget'
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
}

const DEFAULT_HOURS = 1
const DEFAULT_MINUTES = 30

// Exactly one of client/campaign/group scopes a widget at a time — picking one clears the
// others, since the backend's agent-set resolution only honors a single filter dimension.
export default function WidgetConfigModal({ title, fields, initial, initialWidgetTitle, onSave, onClose }: Props) {
  const [clients, setClients] = useState<{ id: string; name: string }[]>([])
  const [campaigns, setCampaigns] = useState<{ id: string; name: string }[]>([])
  const [groups, setGroups] = useState<{ id: string; name: string }[]>([])
  const [widgetTitle, setWidgetTitle] = useState(initialWidgetTitle)
  const [clientId, setClientId] = useState(initial.clientId ?? '')
  const [campaignId, setCampaignId] = useState(initial.campaignId ?? '')
  const [groupId, setGroupId] = useState(initial.groupId ?? '')
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
  const [timeWindowValue, setTimeWindowValue] = useState(
    initial.timeWindow?.value ?? (initial.timeWindow?.mode === 'minutes' ? DEFAULT_MINUTES : DEFAULT_HOURS),
  )

  useEffect(() => {
    listClients().then(setClients).catch(() => {})
    listCampaigns().then(setCampaigns).catch(() => {})
    listAgentGroups().then(setGroups).catch(() => {})
    if (fields.kpi) customKpisApi.list().then((k) => setCustomKpis(k.filter((x) => x.isActive))).catch(() => {})
    if (fields.kpi) customFieldsApi.listDefinitions()
      .then((d) => setFieldNames([...new Map(d.filter((x) => x.isActive).map((x) => [x.fieldName, { name: x.fieldName, label: x.displayLabel }])).values()]))
      .catch(() => {})
  }, [fields.kpi])

  function handleSave() {
    onSave({
      clientId: clientId || undefined,
      campaignId: campaignId || undefined,
      groupId: groupId || undefined,
      loggedInOnly: loggedInOnly || undefined,
      timeWindow: fields.timeWindow
        ? (timeWindowMode === 'hours' || timeWindowMode === 'minutes' ? { mode: timeWindowMode, value: timeWindowValue } : { mode: timeWindowMode })
        : undefined,
      ...(fields.kpi ? {
        groupBy, groupBy2: groupBy !== 'none' && groupBy2 && groupBy2 !== groupBy ? groupBy2 : undefined, kpis, revenueBasis, netRevenue,
        percentOfTotal: percentOfTotal || undefined,
        targets: Object.fromEntries(Object.entries(targets).filter(([k, t]) => kpis.includes(k) && (t.good != null || t.warn != null))),
      } : {}),
    }, widgetTitle.trim() || undefined)
    onClose()
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div
        className="bg-gray-900 border border-gray-800 rounded-xl p-5 w-96 shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 className="text-sm font-semibold text-white mb-1">{title}</h3>
        <p className="text-xs text-gray-500 mb-4">Filter which data this widget shows. Pick one — the most specific wins.</p>

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
                onChange={(v) => { setClientId(v); if (v) { setCampaignId(''); setGroupId('') } }}
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
                onChange={(v) => { setCampaignId(v); if (v) { setClientId(''); setGroupId('') } }}
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
                onChange={(v) => { setGroupId(v); if (v) { setClientId(''); setCampaignId('') } }}
                allLabel="All groups"
                className="w-full"
              />
            </div>
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
                  {fields.kpi && <option value="yesterday">Yesterday</option>}
                  {fields.kpi && <option value="week">This week (from Monday)</option>}
                  {fields.kpi && <option value="month">This month</option>}
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
          {fields.kpi && (
            <>
              <div className="grid grid-cols-2 gap-2">
                <div>
                  <label className="block text-xs text-gray-400 mb-1">Break down by</label>
                  <select value={groupBy} onChange={(e) => setGroupBy(e.target.value)}
                    className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-white">
                    <option value="none">Totals only (tiles)</option>
                    {KPI_DIMENSIONS.map((d) => <option key={d.value} value={d.value}>{d.label}</option>)}
                    {fieldNames.length > 0 && <optgroup label="Custom field">
                      {fieldNames.map((f) => <option key={f.name} value={`cf:${f.name}`}>{f.label}</option>)}
                    </optgroup>}
                  </select>
                </div>
                <div>
                  <label className="block text-xs text-gray-400 mb-1">Revenue</label>
                  <select value={revenueBasis} onChange={(e) => setRevenueBasis(e.target.value as typeof revenueBasis)}
                    className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-white">
                    <option value="gross">Gross (incl. tax)</option>
                    <option value="exclTax">Excluding tax</option>
                    <option value="merch">Merchandise only</option>
                  </select>
                </div>
              </div>
              {groupBy !== 'none' && (
                <div className="grid grid-cols-2 gap-2">
                  <div>
                    <label className="block text-xs text-gray-400 mb-1">Then by (optional)</label>
                    <select value={groupBy2} onChange={(e) => setGroupBy2(e.target.value)}
                      className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-white">
                      <option value="">—</option>
                      {KPI_DIMENSIONS.filter((d) => d.value !== groupBy).map((d) => <option key={d.value} value={d.value}>{d.label}</option>)}
                      {fieldNames.length > 0 && <optgroup label="Custom field">
                        {fieldNames.filter((f) => `cf:${f.name}` !== groupBy).map((f) => <option key={f.name} value={`cf:${f.name}`}>{f.label}</option>)}
                      </optgroup>}
                    </select>
                  </div>
                  <label className="flex items-end gap-2 text-sm text-gray-300 pb-1.5">
                    <input type="checkbox" checked={percentOfTotal} onChange={(e) => setPercentOfTotal(e.target.checked)} />
                    % of total for counts
                  </label>
                </div>
              )}
              <label className="flex items-center gap-2 text-sm text-gray-300">
                <input type="checkbox" checked={netRevenue} onChange={(e) => setNetRevenue(e.target.checked)} />
                Net revenue (only orders whose payment went through)
              </label>
              <div>
                <label className="block text-xs text-gray-400 mb-1">KPIs to show</label>
                <div className="max-h-48 overflow-y-auto border border-gray-800 rounded p-2 space-y-1">
                  {[...KPI_CATALOG.map((k) => ({ key: k.key, label: k.label, group: k.group })),
                    ...customKpis.map((k) => ({ key: `custom:${k.id}`, label: k.name, group: 'Your KPIs' }))].map((k) => (
                    <label key={k.key} className="flex items-center gap-2 text-xs text-gray-300">
                      <input type="checkbox" checked={kpis.includes(k.key)}
                        onChange={(e) => setKpis(e.target.checked ? [...kpis, k.key] : kpis.filter((x) => x !== k.key))} />
                      {k.label} <span className="text-gray-600">{k.group}</span>
                    </label>
                  ))}
                </div>
              </div>
              {kpis.length > 0 && (
                <div>
                  <label className="block text-xs text-gray-400 mb-1">Targets (optional) — colours the value green / amber / red</label>
                  <div className="max-h-40 overflow-y-auto border border-gray-800 rounded p-2 space-y-1">
                    {kpis.map((key) => {
                      const def = KPI_CATALOG.find((k) => k.key === key)
                      const name = def?.label ?? customKpis.find((c) => `custom:${c.id}` === key)?.name ?? key
                      const t = targets[key] ?? { higherIsBetter: true }
                      const set = (p: Partial<KpiTarget>) => setTargets({ ...targets, [key]: { ...t, ...p } })
                      const num = (v: string) => v.trim() === '' ? null : Number(v)
                      return (
                        <div key={key} className="flex items-center gap-1.5 text-[11px] text-gray-300">
                          <span className="flex-1 truncate" title={name}>{name}</span>
                          <input type="number" placeholder="good" value={t.good ?? ''} onChange={(e) => set({ good: num(e.target.value) })}
                            className="w-16 bg-gray-800 border border-gray-700 rounded px-1 py-0.5 text-white" />
                          <input type="number" placeholder="warn" value={t.warn ?? ''} onChange={(e) => set({ warn: num(e.target.value) })}
                            className="w-16 bg-gray-800 border border-gray-700 rounded px-1 py-0.5 text-white" />
                          <select value={t.higherIsBetter ? 'up' : 'down'} onChange={(e) => set({ higherIsBetter: e.target.value === 'up' })}
                            className="bg-gray-800 border border-gray-700 rounded px-1 py-0.5 text-white">
                            <option value="up">higher better</option>
                            <option value="down">lower better</option>
                          </select>
                        </div>
                      )
                    })}
                  </div>
                  <p className="text-[10px] text-gray-500 mt-1">In the KPI's own units: 25 = 25%, 4.50 = $4.50, 180 = 3:00 for times.</p>
                </div>
              )}
            </>
          )}
        </div>

        <div className="flex justify-end gap-2 mt-5">
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
