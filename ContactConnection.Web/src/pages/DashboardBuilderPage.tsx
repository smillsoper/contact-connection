import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import GridLayout, { WidthProvider, type Layout } from 'react-grid-layout/legacy'
import * as signalR from '@microsoft/signalr'
import 'react-grid-layout/css/styles.css'
import 'react-resizable/css/styles.css'
import { useAuthStore } from '../stores/authStore'
import { dashboardsApi } from '../api/dashboards'
import {
  WIDGET_META, WIDGET_TYPES, WIDGET_FILTER_FIELDS, newWidgetId,
  type DashboardWidgetInstance, type DashboardWidgetType, type WidgetFilterConfig,
} from '../types/dashboard'
import WidgetShell from '../components/dashboard/WidgetShell'
import WidgetIcon from '../components/dashboard/WidgetIcon'
import WidgetConfigModal from '../components/dashboard/WidgetConfigModal'
import AgentStateCounterWidget from '../components/dashboard/widgets/AgentStateCounterWidget'
import AgentListWidget from '../components/dashboard/widgets/AgentListWidget'
import CallStateByCampaignWidget from '../components/dashboard/widgets/CallStateByCampaignWidget'
import {
  DashboardLiveContext, DashboardCallStateLiveContext,
  type AgentStateEvent, type CallStateEvent,
} from '../components/dashboard/DashboardLiveContext'

const GridLayoutWithWidth = WidthProvider(GridLayout)

function renderWidget(type: DashboardWidgetType, config: WidgetFilterConfig) {
  switch (type) {
    case 'agent_state_counter':      return <AgentStateCounterWidget config={config} />
    case 'agent_list':                return <AgentListWidget config={config} />
    case 'call_state_by_campaign':    return <CallStateByCampaignWidget config={config} />
  }
}

function getTenantIdFromToken(token: string | null): string | null {
  if (!token) return null
  try {
    const payload = JSON.parse(atob(token.split('.')[1]))
    return typeof payload.tenant_id === 'string' ? payload.tenant_id : null
  } catch {
    return null
  }
}

export default function DashboardBuilderPage() {
  const navigate = useNavigate()
  const { id } = useParams<{ id?: string }>()
  const token = useAuthStore((s) => s.token)
  const tenantSubdomain = useAuthStore((s) => s.tenantSubdomain)
  // reports.view (checked at the route level) is enough to open and look at an existing
  // dashboard's live widgets — there's no separate read-only viewer page, this builder doubles
  // as both. reports.manage additionally unlocks creating, editing layout/config, and saving.
  const canManage = useAuthStore((s) => s.hasPermission('reports.manage'))

  // Nothing to view on the blank "create new" canvas without manage rights — bounce back rather
  // than show an editor whose only action (Save) is about to be hidden.
  useEffect(() => {
    if (!id && !canManage) navigate('/dashboards', { replace: true })
  }, [id, canManage, navigate])

  const [dashboardId, setDashboardId] = useState<string | null>(id ?? null)
  const [name, setName] = useState('New Dashboard')
  const [isShared, setIsShared] = useState(false)
  const [widgets, setWidgets] = useState<DashboardWidgetInstance[]>([])
  const [loading, setLoading] = useState(!!id)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [configuringId, setConfiguringId] = useState<string | null>(null)
  const [liveEvent, setLiveEvent] = useState<AgentStateEvent | null>(null)
  const [liveCallEvent, setLiveCallEvent] = useState<CallStateEvent | null>(null)
  // Opening an existing dashboard defaults to view-only, even for a manager — most visits are
  // "just looking," not editing, and a blank canvas has nothing to view, so it starts in edit
  // mode instead. editMode (not isEditing alone) is what every edit-affordance below actually
  // checks — it also folds in the permission check, so a view-only role can never toggle in.
  const [isEditing, setIsEditing] = useState(!id)
  const editMode = canManage && isEditing

  const draggingTypeRef = useRef<DashboardWidgetType | null>(null)

  // Load an existing dashboard by id — also used to discard unsaved edits when leaving edit mode
  // without saving (Cancel), by re-fetching the last-saved version from the server.
  const loadDashboard = useCallback((dashId: string) => {
    setLoading(true)
    return dashboardsApi.getDetail(dashId)
      .then((detail) => {
        setDashboardId(detail.id)
        setName(detail.name)
        setIsShared(detail.is_shared)
        try {
          setWidgets(JSON.parse(detail.layout) as DashboardWidgetInstance[])
        } catch {
          setWidgets([])
        }
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load dashboard'))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    if (!id) return
    loadDashboard(id)
  }, [id, loadDashboard])

  const handleCancelEdit = useCallback(() => {
    // A brand-new, never-saved dashboard has nothing to revert to — leave the page entirely,
    // same as before this view/edit toggle existed. An existing one drops back to view mode and
    // discards any unsaved local changes by re-fetching what's actually saved.
    if (!dashboardId) { navigate('/dashboards'); return }
    setError(null)
    setIsEditing(false)
    loadDashboard(dashboardId)
  }, [dashboardId, navigate, loadDashboard])

  const handleStartEdit = useCallback(() => {
    // View mode allows free drag/resize as a personal, unsaved layout preference (see the grid
    // props below) — so `widgets` may already differ from what's actually saved by the time
    // someone clicks Edit. Re-fetch first so a real edit session always starts from the true
    // saved layout, not whatever they'd been idly rearranging for themselves; otherwise a casual
    // view-mode drag followed by Edit → Save would silently promote a personal tweak to
    // everyone's layout.
    if (dashboardId) loadDashboard(dashboardId)
    setIsEditing(true)
  }, [dashboardId, loadDashboard])

  // SignalR — join the supervisor group for this tenant, listen for agent state pushes
  useEffect(() => {
    if (!token) return
    const tenantId = getTenantIdFromToken(token)
    if (!tenantId) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/flow?access_token=${token}`, {
        headers: { 'X-Tenant-Subdomain': tenantSubdomain ?? '' },
      })
      .withAutomaticReconnect()
      .build()

    connection.on('receiveAgentStateSnapshot', (agentId: string, stateCode: string, label: string, sinceIso: string) => {
      setLiveEvent({ agentId, code: stateCode, label, since: sinceIso })
    })

    connection.on('receiveCallStateSnapshot', (campaignId: string, state: string) => {
      setLiveCallEvent({ campaignId, state })
    })

    connection.start()
      .then(() => connection.invoke('JoinSupervisorView', tenantId))
      .catch((err) => console.error('[SignalR] supervisor dashboard connection failed:', err))

    return () => { connection.stop() }
  }, [token, tenantSubdomain])

  const layout: Layout = useMemo(
    () => widgets.map((w) => ({ i: w.id, x: w.x, y: w.y, w: w.w, h: w.h })),
    [widgets],
  )

  function handleLayoutChange(next: Layout) {
    setWidgets((prev) => prev.map((w) => {
      const pos = next.find((n) => n.i === w.id)
      return pos ? { ...w, x: pos.x, y: pos.y, w: pos.w, h: pos.h } : w
    }))
  }

  function handleDrop(_layout: Layout, item: { x: number; y: number } | undefined, e: Event) {
    const type = draggingTypeRef.current
    draggingTypeRef.current = null
    if (!type || !item) return

    const dragEvent = e as unknown as DragEvent
    const dropped = dragEvent.dataTransfer?.getData('text/plain') as DashboardWidgetType | ''
    const widgetType = (dropped || type) as DashboardWidgetType
    const meta = WIDGET_META[widgetType]

    setWidgets((prev) => [...prev, {
      id: newWidgetId(),
      widgetType,
      x: item.x,
      y: item.y,
      w: meta.defaultSize.w,
      h: meta.defaultSize.h,
      config: {},
    }])
  }

  function handleRemove(widgetId: string) {
    setWidgets((prev) => prev.filter((w) => w.id !== widgetId))
  }

  function handleConfigSave(widgetId: string, config: WidgetFilterConfig) {
    setWidgets((prev) => prev.map((w) => (w.id === widgetId ? { ...w, config } : w)))
  }

  const handleSave = useCallback(async () => {
    setSaving(true)
    setError(null)
    try {
      const layoutJson = JSON.stringify(widgets)
      if (dashboardId) {
        await dashboardsApi.update(dashboardId, name, isShared, layoutJson)
      } else {
        const created = await dashboardsApi.create(name, isShared, layoutJson)
        setDashboardId(created.id)
        navigate(`/dashboard-builder/${created.id}`, { replace: true })
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to save dashboard')
    } finally {
      setSaving(false)
    }
  }, [dashboardId, name, isShared, widgets, navigate])

  const configuringWidget = widgets.find((w) => w.id === configuringId)

  return (
    <div className="min-h-screen bg-gray-950 flex flex-col">
      {/* Header */}
      <div className="flex items-stretch bg-gray-900 border-b border-gray-800 shrink-0">
        <img src="/cc-navbar-dark.svg" alt="Contact Connection" className="shrink-0 block" />
        <div className="flex items-center justify-between flex-1 px-4 py-2 gap-4">
          <div className="flex items-center gap-3">
            <div className="w-px h-5 bg-gray-700" />
            <button
              onClick={() => navigate('/dashboards')}
              className="text-gray-400 hover:text-gray-200 text-sm flex items-center gap-1 transition-colors shrink-0"
            >
              ← Back
            </button>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Dashboard Name"
              disabled={!editMode}
              className="bg-gray-800 border border-gray-700 rounded px-3 py-1 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500 w-56 disabled:opacity-60 disabled:cursor-not-allowed"
            />
            <label className="flex items-center gap-1.5 text-sm text-gray-300 shrink-0">
              <input
                type="checkbox"
                checked={isShared}
                onChange={(e) => setIsShared(e.target.checked)}
                disabled={!editMode}
                className="rounded disabled:opacity-60"
              />
              Shared
            </label>
            {!canManage && (
              <span className="text-xs text-gray-500 border border-gray-700 rounded-full px-2.5 py-0.5 shrink-0">
                View only
              </span>
            )}
          </div>
          <div className="flex items-center gap-2 shrink-0">
            {error && <span className="text-xs text-red-400">{error}</span>}
            {editMode ? (
              <>
                <button
                  onClick={handleCancelEdit}
                  className="text-sm text-gray-300 border border-gray-700 hover:border-gray-500 px-4 py-1.5 rounded-lg transition-colors"
                >
                  Cancel
                </button>
                <button
                  onClick={handleSave}
                  disabled={saving || !name.trim()}
                  className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-1.5 rounded-lg disabled:opacity-50 transition-colors"
                >
                  {saving ? 'Saving…' : 'Save'}
                </button>
              </>
            ) : (
              <>
                <button
                  onClick={() => navigate('/dashboards')}
                  className="text-sm text-gray-300 border border-gray-700 hover:border-gray-500 px-4 py-1.5 rounded-lg transition-colors"
                >
                  Back
                </button>
                {canManage && (
                  <button
                    onClick={handleStartEdit}
                    className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-1.5 rounded-lg transition-colors"
                  >
                    Edit
                  </button>
                )}
              </>
            )}
          </div>
        </div>
      </div>

      {/* Widget palette — edit-mode only */}
      {editMode && (
      <div className="flex items-center gap-3 bg-gray-900 border-b border-gray-800 px-6 py-4 overflow-x-auto shrink-0">
        {WIDGET_TYPES.map((type) => {
          const meta = WIDGET_META[type]
          return (
            <div
              key={type}
              draggable
              unselectable="on"
              onDragStart={(e) => {
                draggingTypeRef.current = type
                e.dataTransfer.setData('text/plain', type)
                e.dataTransfer.effectAllowed = 'copy'
              }}
              className="flex flex-col items-center gap-1.5 w-20 shrink-0 cursor-grab active:cursor-grabbing select-none"
            >
              <div className="w-14 h-14 rounded-lg bg-gray-800 border border-gray-700 flex items-center justify-center text-gray-400">
                <WidgetIcon type={type} />
              </div>
              <span className="text-[11px] text-gray-400 text-center leading-tight">{meta.label}</span>
            </div>
          )
        })}
      </div>
      )}

      {/* Canvas */}
      <div className="flex-1 p-6">
        {!loading && !editMode && widgets.length > 0 && (
          <p className="text-gray-600 text-xs mb-3">
            Drag or resize tiles to rearrange them for yourself — it's just for this screen and won't change what anyone else sees.
          </p>
        )}
        {loading ? (
          <div className="flex items-center justify-center h-40 text-gray-500 text-sm">Loading…</div>
        ) : (
          <DashboardLiveContext.Provider value={liveEvent}>
          <DashboardCallStateLiveContext.Provider value={liveCallEvent}>
            <GridLayoutWithWidth
              className="layout"
              layout={layout}
              cols={12}
              rowHeight={30}
              margin={[12, 12]}
              draggableHandle=".widget-drag-handle"
              // Free rearranging/resizing is allowed in view mode too — a personal, unsaved
              // layout preference (see handleStartEdit) since not everyone's screen is the same.
              // Only isDroppable stays edit-gated: it accepts *new* widgets from the palette,
              // which is hidden outside edit mode anyway, so there's nothing to drop.
              isDraggable
              isResizable
              isDroppable={editMode}
              droppingItem={{ i: '__dropping-elem__', x: 0, y: 0, w: 4, h: 8 }}
              onDrop={handleDrop}
              onDropDragOver={() => ({})}
              onLayoutChange={handleLayoutChange}
            >
              {widgets.map((w) => (
                <div key={w.id}>
                  <WidgetShell
                    title={WIDGET_META[w.widgetType].label}
                    onConfigure={editMode ? () => setConfiguringId(w.id) : undefined}
                    onRemove={editMode ? () => handleRemove(w.id) : undefined}
                  >
                    {renderWidget(w.widgetType, w.config)}
                  </WidgetShell>
                </div>
              ))}
            </GridLayoutWithWidth>
            {widgets.length === 0 && (
              <div className="flex items-center justify-center h-40 text-gray-600 text-sm border-2 border-dashed border-gray-800 rounded-xl">
                {editMode ? 'Drag a widget from the palette above to get started.' : 'This dashboard has no widgets yet.'}
              </div>
            )}
          </DashboardCallStateLiveContext.Provider>
          </DashboardLiveContext.Provider>
        )}
      </div>

      {configuringWidget && (
        <WidgetConfigModal
          title={`Configure — ${WIDGET_META[configuringWidget.widgetType].label}`}
          fields={WIDGET_FILTER_FIELDS[configuringWidget.widgetType]}
          initial={configuringWidget.config}
          onSave={(config) => handleConfigSave(configuringWidget.id, config)}
          onClose={() => setConfiguringId(null)}
        />
      )}
    </div>
  )
}
