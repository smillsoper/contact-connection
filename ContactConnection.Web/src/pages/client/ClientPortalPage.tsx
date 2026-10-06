import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import GridLayout, { WidthProvider } from 'react-grid-layout/legacy'
import * as signalR from '@microsoft/signalr'
import 'react-grid-layout/css/styles.css'
import { clientPortalApi, clientSubdomain, type ClientDashboardRef } from '../../api/clientPortal'
import { useClientAuthStore } from '../../stores/clientAuthStore'
import { WIDGET_META, type DashboardWidgetInstance } from '../../types/dashboard'
import WidgetShell from '../../components/dashboard/WidgetShell'
import KpiWidget from '../../components/dashboard/widgets/KpiWidget'
import ServiceLevelThresholdWidget from '../../components/dashboard/widgets/ServiceLevelThresholdWidget'
import CallStateByCampaignWidget from '../../components/dashboard/widgets/CallStateByCampaignWidget'
import { WidgetDataSourceContext } from '../../components/dashboard/WidgetDataSource'
import {
  DashboardCallStateLiveContext, DashboardAgentSessionsLiveContext, type CallStateEvent, type AgentSessionsEvent,
} from '../../components/dashboard/DashboardLiveContext'

// The client portal (S181): the client dashboards a client user was given, read-only. Every widget fetches through the
// client-portal API, which answers from the saved config inside the dashboard's locked scope. Live: the client hub sends a
// data-free nudge and the widgets refetch.

const GridLayoutWithWidth = WidthProvider(GridLayout)

function ClientWidget({ dashboardId, widget }: { dashboardId: string; widget: DashboardWidgetInstance }) {
  const fetchData = useMemo(() => () => clientPortalApi.widgetData(dashboardId, widget.id), [dashboardId, widget.id])
  const body = (() => {
    switch (widget.widgetType) {
      case 'kpi': return <KpiWidget config={widget.config} />
      case 'service_level_threshold': return <ServiceLevelThresholdWidget config={widget.config} />
      case 'call_state_by_campaign': return <CallStateByCampaignWidget config={widget.config} />
      default: return null
    }
  })()
  return (
    <WidgetDataSourceContext.Provider value={fetchData}>
      <WidgetShell title={widget.title || WIDGET_META[widget.widgetType]?.label || 'Widget'}>{body}</WidgetShell>
    </WidgetDataSourceContext.Provider>
  )
}

function zones(): string[] {
  try { return (Intl as unknown as { supportedValuesOf(k: string): string[] }).supportedValuesOf('timeZone') } catch { return [] }
}

export default function ClientPortalPage() {
  const navigate = useNavigate()
  const { id } = useParams<{ id?: string }>()
  const token = useClientAuthStore((s) => s.token)
  const profile = useClientAuthStore((s) => s.profile)
  const setProfile = useClientAuthStore((s) => s.setProfile)
  const clear = useClientAuthStore((s) => s.clear)
  const [dashboards, setDashboards] = useState<ClientDashboardRef[] | null>(null)
  const [dashboard, setDashboard] = useState<{ id: string; name: string; widgets: DashboardWidgetInstance[] } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [prefsOpen, setPrefsOpen] = useState(false)
  const [tz, setTz] = useState('')
  const [defaultDash, setDefaultDash] = useState('')
  const [live, setLive] = useState<{ call: CallStateEvent | null; sessions: AgentSessionsEvent | null }>({ call: null, sessions: null })
  const nudge = useRef<ReturnType<typeof setTimeout> | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  useEffect(() => {
    if (!token) { navigate('/client/login', { replace: true }); return }
    clientPortalApi.me()
      .then((m) => { setProfile(m.profile); setDashboards(m.dashboards) })
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not load your dashboards'))
  }, [token, navigate, setProfile])

  // Which dashboard: the URL's, else the user's default, else the first.
  const selectedId = useMemo(() => {
    if (!dashboards || dashboards.length === 0) return null
    if (id && dashboards.some((d) => d.id === id)) return id
    const def = profile?.defaultDashboardId
    return def && dashboards.some((d) => d.id === def) ? def : dashboards[0].id
  }, [dashboards, id, profile?.defaultDashboardId])

  useEffect(() => {
    if (!selectedId) { setDashboard(null); return }
    setError(null)
    clientPortalApi.dashboard(selectedId)
      .then((d) => {
        let widgets: DashboardWidgetInstance[] = []
        try { widgets = JSON.parse(d.layout) } catch { /* empty */ }
        setDashboard({ id: d.id, name: d.name, widgets })
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not open the dashboard'))
  }, [selectedId])

  // Live refresh nudges — debounced so a burst of calls refetches once.
  useEffect(() => {
    if (!token) return
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/client?access_token=${token}`, { headers: { 'X-Tenant-Subdomain': clientSubdomain() ?? '' } })
      .withAutomaticReconnect()
      .build()
    connection.on('receiveRefresh', () => {
      if (nudge.current) clearTimeout(nudge.current)
      nudge.current = setTimeout(() => {
        const at = Date.now()
        setLive({ call: { campaignId: '', state: `refresh:${at}` }, sessions: { agentId: '', at } })
      }, 1500)
    })
    // The hub puts each connection in its tenant's group on connect, so a reconnect needs nothing re-joined.
    connection.start().catch((err) => console.error('[SignalR] client portal connection failed:', err))
    return () => { if (nudge.current) clearTimeout(nudge.current); connection.stop() }
  }, [token])

  const layout = useMemo(() => (dashboard?.widgets ?? []).map((w) => ({ i: w.id, x: w.x, y: w.y, w: w.w, h: w.h, static: true })), [dashboard])

  function signOut() {
    clear()
    navigate('/client/login', { replace: true })
  }

  async function savePrefs() {
    try {
      setProfile(await clientPortalApi.setPreferences(tz || null, defaultDash || null))
      setPrefsOpen(false)
      setReloadKey((k) => k + 1)   // remount widgets so they refetch in the new time zone
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not save') }
  }

  if (!token) return null

  return (
    <div className="min-h-screen bg-gray-950 flex flex-col">
      <div className="flex items-center justify-between gap-3 bg-gray-900 border-b border-gray-800 px-4 py-2 flex-wrap">
        <div className="flex items-center gap-3 min-w-0">
          <img src="/hubion-favicon.svg" alt="" className="w-6 h-6 shrink-0" />
          <span className="text-sm font-semibold text-white truncate">{profile?.tenantName ?? 'Dashboards'}</span>
          {dashboards && dashboards.length > 1 && (
            <select value={selectedId ?? ''} onChange={(e) => navigate(`/client/d/${e.target.value}`)}
              className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white">
              {dashboards.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          )}
          {dashboards && dashboards.length === 1 && <span className="text-sm text-gray-400 truncate">{dashboards[0].name}</span>}
        </div>
        <div className="flex items-center gap-3 text-sm">
          <span className="text-gray-400 hidden sm:inline">{profile ? `${profile.firstName} ${profile.lastName}` : ''}</span>
          <button className="text-gray-300 hover:text-white" onClick={() => {
            setTz(profile?.timeZone ?? ''); setDefaultDash(profile?.defaultDashboardId ?? ''); setPrefsOpen(true)
          }}>Preferences</button>
          <button className="text-gray-300 hover:text-white" onClick={signOut}>Sign out</button>
        </div>
      </div>

      <div className="flex-1 p-4 sm:p-6">
        {error && <div className="mb-3 text-sm text-red-400">{error}</div>}
        {dashboards && dashboards.length === 0 && (
          <div className="text-center text-gray-500 text-sm mt-16">No dashboards have been shared with you yet.</div>
        )}
        {dashboard && (
          <DashboardCallStateLiveContext.Provider value={live.call}>
          <DashboardAgentSessionsLiveContext.Provider value={live.sessions}>
            <GridLayoutWithWidth key={reloadKey} className="layout" layout={layout} cols={12} rowHeight={30} margin={[12, 12]}
              isDraggable={false} isResizable={false} isDroppable={false}>
              {dashboard.widgets.map((w) => (
                <div key={w.id}><ClientWidget dashboardId={dashboard.id} widget={w} /></div>
              ))}
            </GridLayoutWithWidth>
            {dashboard.widgets.length === 0 && <div className="text-center text-gray-500 text-sm mt-16">This dashboard is empty.</div>}
          </DashboardAgentSessionsLiveContext.Provider>
          </DashboardCallStateLiveContext.Provider>
        )}
      </div>

      {prefsOpen && profile && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" onClick={() => setPrefsOpen(false)}>
          <div className="bg-gray-900 border border-gray-800 rounded-xl p-5 w-full max-w-sm" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-sm font-semibold text-white mb-4">Preferences</h3>
            <label className="block text-xs text-gray-400 mb-1">Time zone (for "today", days and hours)</label>
            <select value={tz} onChange={(e) => setTz(e.target.value)} className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-white mb-3">
              <option value="">Same as {profile.tenantName} ({profile.tenantTimeZone})</option>
              {zones().map((z) => <option key={z} value={z}>{z}</option>)}
            </select>
            <label className="block text-xs text-gray-400 mb-1">Open first</label>
            <select value={defaultDash} onChange={(e) => setDefaultDash(e.target.value)} className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-white">
              <option value="">The first dashboard</option>
              {(dashboards ?? []).map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
            <div className="flex justify-end gap-2 mt-5">
              <button className="text-sm text-gray-300 border border-gray-700 px-4 py-1.5 rounded-lg" onClick={() => setPrefsOpen(false)}>Cancel</button>
              <button className="text-sm bg-indigo-600 hover:bg-indigo-500 text-white px-4 py-1.5 rounded-lg" onClick={savePrefs}>Save</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
