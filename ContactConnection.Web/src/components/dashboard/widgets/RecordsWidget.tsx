import { useCallback, useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { dashboardWidgetsApi, type RecordDetailResponse, type RecordsPage, type RecordsParams } from '../../../api/dashboardWidgets'
import type { WidgetFilterConfig } from '../../../types/dashboard'
import { useDashboardLiveAgentSessions, useDashboardLiveCallState } from '../DashboardLiveContext'
import { useRecordsSource } from '../RecordsSource'
import CallDetailModal from '../CallDetailModal'
import { useAuthStore } from '../../../stores/authStore'
import { getSubdomainFromHostname } from '../../../utils/subdomain'

// Records widget (S181, docs/client-dashboards-plan.md §C): call records in the widget's window and scope — search, sort,
// per-column filters, paging, and a detail drawer with recording playback where allowed.

function internalRecording(callId: string) {
  const { token, tenantSubdomain } = useAuthStore.getState()
  const sub = getSubdomainFromHostname() ?? tenantSubdomain
  return fetch(`/api/v1/call-records/${callId}/recording`, {
    headers: { Authorization: `Bearer ${token ?? ''}`, ...(sub ? { 'X-Tenant-Subdomain': sub } : {}) },
  })
}

export default function RecordsWidget({ config }: { config: WidgetFilterConfig }) {
  const source = useRecordsSource()
  const pageSize = config.pageSize ?? 25
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [sort, setSort] = useState<string | undefined>()
  const [desc, setDesc] = useState(true)
  const [filters, setFilters] = useState<Record<string, string>>({})
  const [appliedFilters, setAppliedFilters] = useState<Record<string, string>>({})
  const [showFilters, setShowFilters] = useState(false)
  const [data, setData] = useState<RecordsPage | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [openId, setOpenId] = useState<string | null>(null)
  const [detail, setDetail] = useState<RecordDetailResponse | null>(null)
  const [detailError, setDetailError] = useState<string | null>(null)
  const sessions = useDashboardLiveAgentSessions()
  const callState = useDashboardLiveCallState()
  const debounce = useRef<ReturnType<typeof setTimeout> | null>(null)

  const load = useCallback(() => {
    const p: RecordsParams = { page, pageSize, search: appliedSearch, sort, desc, filters: appliedFilters }
    ;(source ? source.query(p) : dashboardWidgetsApi.records(config, p))
      .then((d) => { setData(d); setError(null) })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, pageSize, appliedSearch, sort, desc, appliedFilters, source, config.campaignId, config.clientId,
    config.timeWindow?.mode, config.timeWindow?.value, (config.columns ?? []).join(',')])

  useEffect(() => { load() }, [load])

  // New calls finishing — refetch this page, debounced.
  useEffect(() => {
    if (!sessions && !callState) return
    if (debounce.current) clearTimeout(debounce.current)
    debounce.current = setTimeout(load, 2000)
    return () => { if (debounce.current) clearTimeout(debounce.current) }
  }, [sessions, callState, load])

  // Search and filters apply after a short pause in typing.
  useEffect(() => {
    const t = setTimeout(() => {
      setAppliedSearch((prev) => (prev === search ? prev : search))
      setAppliedFilters((prev) => (JSON.stringify(prev) === JSON.stringify(filters) ? prev : filters))
      if (search !== appliedSearch || JSON.stringify(filters) !== JSON.stringify(appliedFilters)) setPage(1)
    }, 400)
    return () => clearTimeout(t)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, filters])

  useEffect(() => {
    if (!openId) { setDetail(null); return }
    setDetail(null); setDetailError(null)
    ;(source ? source.detail(openId) : dashboardWidgetsApi.recordDetail(config, openId))
      .then(setDetail)
      .catch((e) => setDetailError(e instanceof Error ? e.message : 'Failed to load'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openId, source])

  if (error) return <div className="text-xs text-red-400">{error}</div>
  if (!data) return <div className="text-xs text-gray-500">Loading…</div>

  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  const toggleSort = (key: string) => {
    if (sort === key) setDesc(!desc)
    else { setSort(key); setDesc(key === 'started' || key === 'revenue' || key === 'duration') }
    setPage(1)
  }

  return (
    <div className="h-full flex flex-col min-h-0 text-xs">
      <div className="flex items-center gap-2 mb-2 shrink-0">
        <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search name, phone, email, order…"
          className="flex-1 min-w-0 bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-white placeholder-gray-500 focus:outline-none focus:border-sky-500" />
        <button onClick={() => setShowFilters(!showFilters)}
          className={`px-2 py-1 rounded border ${showFilters || Object.values(filters).some((v) => v.trim()) ? 'border-sky-500 text-sky-300' : 'border-gray-700 text-gray-400'}`}>
          Filters
        </button>
        <span className="text-gray-500 whitespace-nowrap">{data.total.toLocaleString()} calls</span>
      </div>
      {data.truncated && <p className="text-amber-300 mb-1">Showing the newest {data.total.toLocaleString()} — narrow the time window to see older calls.</p>}

      <div className="flex-1 min-h-0 overflow-auto">
        <table className="w-full whitespace-nowrap">
          <thead className="sticky top-0 bg-gray-900 z-10">
            <tr className="text-gray-500 text-left">
              {data.columns.map((c) => (
                <th key={c.key} className="px-2 py-1 font-medium cursor-pointer select-none hover:text-gray-300" onClick={() => toggleSort(c.key)}>
                  {c.label}{sort === c.key ? (desc ? ' ↓' : ' ↑') : ''}
                </th>
              ))}
            </tr>
            {showFilters && (
              <tr>
                {data.columns.map((c) => (
                  <th key={c.key} className="px-1 pb-1">
                    <input value={filters[c.key] ?? ''} onChange={(e) => setFilters({ ...filters, [c.key]: e.target.value })} placeholder="contains…"
                      className="w-full min-w-[5rem] bg-gray-800 border border-gray-700 rounded px-1.5 py-0.5 text-[11px] font-normal text-white placeholder-gray-600" />
                  </th>
                ))}
              </tr>
            )}
          </thead>
          <tbody>
            {data.rows.length === 0 && (
              <tr><td colSpan={data.columns.length} className="px-2 py-6 text-center text-gray-500">No calls match.</td></tr>
            )}
            {data.rows.map((r) => (
              <tr key={r.id} onClick={() => setOpenId(r.id)}
                className={`border-t border-gray-800 cursor-pointer hover:bg-gray-800/50 ${openId === r.id ? 'bg-gray-800/70' : ''}`}>
                {data.columns.map((c) => <td key={c.key} className="px-2 py-1 text-gray-200">{r.values[c.key] ?? <span className="text-gray-600">—</span>}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="flex items-center justify-end gap-2 pt-2 shrink-0 text-gray-400">
        <button disabled={page <= 1} onClick={() => setPage(page - 1)} className="px-2 py-0.5 border border-gray-700 rounded disabled:opacity-40">‹ Prev</button>
        <span>Page {data.page} of {pages}</span>
        <button disabled={page >= pages} onClick={() => setPage(page + 1)} className="px-2 py-0.5 border border-gray-700 rounded disabled:opacity-40">Next ›</button>
      </div>

      {openId && (detailError ? createPortal(
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60" onClick={() => setOpenId(null)}>
          <div className="bg-gray-900 border border-gray-800 rounded-xl p-5 text-sm text-red-400">{detailError}</div>
        </div>, document.body
      ) : (
        <CallDetailModal data={detail?.detail ?? null} canPlayRecording={detail?.canPlayRecording ?? false}
          loadRecording={() => (source ? source.recording(openId) : internalRecording(openId))} onClose={() => setOpenId(null)} />
      ))}
    </div>
  )
}
