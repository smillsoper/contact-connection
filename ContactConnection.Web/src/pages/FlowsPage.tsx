import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { flowsApi, type FlowSummary } from '../api/flows'

function designerPath(flow: FlowSummary): string {
  return flow.flow_type === 'telephony'
    ? `/telephony-designer/${flow.id}`
    : `/designer/${flow.id}`
}

interface FlowExportEnvelope {
  ccflow_version: '1'
  flow_type: string
  flow_direction: string | null
  flow_sub_type: string | null
  name: string
  definition: unknown
}

export default function FlowsPage() {
  const navigate = useNavigate()
  const [flows, setFlows] = useState<FlowSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [publishingId, setPublishingId] = useState<string | null>(null)
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [exportingId, setExportingId] = useState<string | null>(null)
  const [importing, setImporting] = useState(false)
  const [importError, setImportError] = useState<string | null>(null)
  const [importWarnings, setImportWarnings] = useState<string[]>([])
  const [newMenuOpen, setNewMenuOpen] = useState(false)
  const newMenuRef = useRef<HTMLDivElement>(null)
  const importInputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (newMenuRef.current && !newMenuRef.current.contains(e.target as Node)) {
        setNewMenuOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [])

  async function load() {
    setLoading(true)
    setError(null)
    try {
      const data = await flowsApi.listAll()
      setFlows(data)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load flows')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  async function handlePublish(id: string) {
    setPublishingId(id)
    try {
      await flowsApi.publish(id)
      await load()
    } finally {
      setPublishingId(null)
    }
  }

  async function handleDelete(id: string, name: string) {
    if (!window.confirm(`Delete "${name}"? This cannot be undone.`)) return
    setDeletingId(id)
    try {
      await flowsApi.delete(id)
      await load()
    } finally {
      setDeletingId(null)
    }
  }

  function fmt(iso: string) {
    return new Date(iso).toLocaleDateString(undefined, {
      month: 'short', day: 'numeric', year: 'numeric',
    })
  }

  async function handleExport(flow: FlowSummary) {
    setExportingId(flow.id)
    try {
      const detail = await flowsApi.getDetail(flow.id)
      const envelope: FlowExportEnvelope = {
        ccflow_version: '1',
        flow_type: flow.flow_type,
        flow_direction: flow.flow_direction ?? null,
        flow_sub_type: flow.flow_sub_type ?? null,
        name: flow.name,
        definition: JSON.parse(detail.definition),
      }
      const blob = new Blob([JSON.stringify(envelope, null, 2)], { type: 'application/json' })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `${flow.name.replace(/[^a-z0-9]+/gi, '_').toLowerCase()}.ccflow.json`
      a.click()
      URL.revokeObjectURL(url)
    } catch {
      // silently ignore — export failure is non-critical
    } finally {
      setExportingId(null)
    }
  }

  async function handleImport(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ''   // reset so the same file can be re-imported
    if (!file) return

    setImporting(true)
    setImportError(null)
    setImportWarnings([])

    let envelope: FlowExportEnvelope
    try {
      const text = await file.text()
      const parsed = JSON.parse(text)
      if (!parsed.flow_type || !parsed.definition || !parsed.name)
        throw new Error('Missing required fields (flow_type, name, definition).')
      envelope = parsed as FlowExportEnvelope
    } catch (err) {
      setImportError(err instanceof Error ? err.message : 'Invalid file format.')
      setImporting(false)
      return
    }

    // Sanitize cross-flow references — wipe any targetFlowId that doesn't exist in this tenant
    const existingIds = new Set(flows.map((f) => f.id))
    const warnings: string[] = []
    const definition = envelope.definition as Record<string, unknown>
    const nodes = (definition.nodes ?? {}) as Record<string, Record<string, unknown>>
    for (const [nodeId, node] of Object.entries(nodes)) {
      if (node.type === 'execute_flow' || node.type === 'transition_to_flow') {
        const refId = node.targetFlowId as string | undefined
        if (refId && !existingIds.has(refId)) {
          warnings.push(
            `Node "${node.label ?? nodeId}" (${node.type}) referenced a flow that doesn't exist in this tenant — link cleared.`
          )
          node.targetFlowId = ''
          node.targetFlowName = ''
        }
      }
    }

    try {
      const detail = await flowsApi.create(
        envelope.name,
        envelope.flow_type,
        definition as never,
        envelope.flow_direction ?? undefined,
        envelope.flow_sub_type ?? undefined,
      )
      if (warnings.length > 0) setImportWarnings(warnings)
      const path = envelope.flow_type === 'telephony'
        ? `/telephony-designer/${detail.id}`
        : `/designer/${detail.id}`
      navigate(path)
    } catch (err) {
      setImportError(err instanceof Error ? err.message : 'Failed to import flow.')
    } finally {
      setImporting(false)
    }
  }

  return (
    <div className="min-h-screen bg-gray-950 flex flex-col">
      {/* Hidden file input for import */}
      <input
        ref={importInputRef}
        type="file"
        accept=".json,.ccflow.json"
        className="hidden"
        onChange={handleImport}
      />

      {/* Header */}
      <div className="flex items-stretch bg-gray-900 border-b border-gray-800 shrink-0">
        <img src="/cc-navbar-dark.svg" alt="Contact Connection" className="shrink-0 block" />
        <div className="flex items-center justify-between flex-1 px-4">
          <div className="flex items-center gap-3">
            <div className="w-px h-5 bg-gray-700" />
            <button
              onClick={() => navigate('/admin')}
              className="text-gray-400 hover:text-gray-200 text-sm flex items-center gap-1 transition-colors"
            >
              ← Back
            </button>
            <span className="text-sm font-semibold text-white">Flows</span>
          </div>
          <div className="flex items-center gap-2">
          <button
            onClick={() => importInputRef.current?.click()}
            disabled={importing}
            className="text-sm text-gray-300 hover:text-white border border-gray-700 hover:border-gray-500 px-4 py-1.5 rounded-lg disabled:opacity-50 transition-colors"
          >
            {importing ? 'Importing…' : 'Import'}
          </button>
          <div ref={newMenuRef} className="relative">
            <button
              onClick={() => setNewMenuOpen((v) => !v)}
              className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-1.5 rounded-lg transition-colors flex items-center gap-1.5"
            >
              + New Flow
              <svg className={`w-3 h-3 transition-transform ${newMenuOpen ? 'rotate-180' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
              </svg>
            </button>
            {newMenuOpen && (
              <div className="absolute right-0 mt-1 w-48 bg-gray-800 border border-gray-700 rounded-lg shadow-xl overflow-hidden z-50">
                <button
                  onClick={() => { setNewMenuOpen(false); navigate('/designer') }}
                  className="w-full text-left px-4 py-2.5 text-sm text-gray-200 hover:bg-gray-700 transition-colors"
                >
                  Script Flow (CRM)
                </button>
                <button
                  onClick={() => { setNewMenuOpen(false); navigate('/telephony-designer') }}
                  className="w-full text-left px-4 py-2.5 text-sm text-gray-200 hover:bg-gray-700 transition-colors border-t border-gray-700"
                >
                  Telephony Flow
                </button>
              </div>
            )}
          </div>
          </div>
        </div>
      </div>

      {/* Import error */}
      {importError && (
        <div className="mx-6 mt-4 flex items-start gap-3 bg-red-950 border border-red-800 rounded-lg px-4 py-3">
          <span className="text-red-400 text-sm flex-1">Import failed: {importError}</span>
          <button onClick={() => setImportError(null)} className="text-red-600 hover:text-red-400 text-xs shrink-0">✕</button>
        </div>
      )}

      {/* Import warnings (cross-flow reference issues) */}
      {importWarnings.length > 0 && (
        <div className="mx-6 mt-4 bg-amber-950 border border-amber-800 rounded-lg px-4 py-3">
          <div className="flex items-start justify-between gap-3">
            <div>
              <p className="text-amber-300 text-sm font-semibold mb-1">Flow imported with warnings</p>
              <ul className="list-disc list-inside space-y-0.5">
                {importWarnings.map((w, i) => (
                  <li key={i} className="text-amber-400 text-xs">{w}</li>
                ))}
              </ul>
            </div>
            <button onClick={() => setImportWarnings([])} className="text-amber-600 hover:text-amber-400 text-xs shrink-0">✕</button>
          </div>
        </div>
      )}

      {/* Content */}
      <div className="flex-1 p-6">
        {loading ? (
          <div className="flex items-center justify-center h-40 text-gray-500 text-sm">
            Loading…
          </div>
        ) : error ? (
          <div className="flex flex-col items-center justify-center h-40 gap-3">
            <p className="text-red-400 text-sm font-medium">Error loading flows</p>
            <p className="text-red-500 text-xs font-mono">{error}</p>
            <button onClick={load} className="text-sm text-sky-400 hover:underline">Retry</button>
          </div>
        ) : flows.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-40 gap-3">
            <p className="text-gray-500 text-sm">No flows yet.</p>
            <button
              onClick={() => navigate('/designer')}
              className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-1.5 rounded-lg transition-colors"
            >
              Create your first script flow
            </button>
          </div>
        ) : (
          <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 bg-gray-800/50">
                  <th className="text-left px-4 py-3 text-xs font-semibold text-gray-400 uppercase tracking-wide">Name</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-gray-400 uppercase tracking-wide">Type</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-gray-400 uppercase tracking-wide">Direction / Sub-type</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-gray-400 uppercase tracking-wide">Status</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-gray-400 uppercase tracking-wide">Version</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-gray-400 uppercase tracking-wide">Updated</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {flows.map((flow) => (
                  <tr key={flow.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/40 transition-colors">
                    <td className="px-4 py-3 font-medium text-white">{flow.name}</td>
                    <td className="px-4 py-3">
                      {flow.flow_type === 'telephony' ? (
                        <span className="inline-flex items-center text-xs font-medium text-violet-300 bg-violet-900/30 border border-violet-700 px-2 py-0.5 rounded-full">
                          Telephony
                        </span>
                      ) : (
                        <span className="inline-flex items-center text-xs font-medium text-sky-300 bg-sky-900/30 border border-sky-700 px-2 py-0.5 rounded-full">
                          Script
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {flow.flow_type === 'telephony' ? (
                        <div className="flex items-center gap-1.5 flex-wrap">
                          {flow.flow_direction ? (
                            <span className={`inline-flex items-center text-xs font-medium px-2 py-0.5 rounded-full ${
                              flow.flow_direction === 'inbound'
                                ? 'text-emerald-300 bg-emerald-900/30 border border-emerald-700'
                                : 'text-amber-300 bg-amber-900/30 border border-amber-700'
                            }`}>
                              {flow.flow_direction === 'inbound' ? 'Inbound' : 'Outbound'}
                            </span>
                          ) : (
                            <span className="text-xs text-gray-600">—</span>
                          )}
                          {flow.flow_sub_type && (
                            <span className="inline-flex items-center text-xs font-medium text-gray-400 bg-gray-800 border border-gray-700 px-2 py-0.5 rounded-full capitalize">
                              {flow.flow_sub_type}
                            </span>
                          )}
                        </div>
                      ) : (
                        <span className="text-xs text-gray-600">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {flow.is_active ? (
                        <span className="inline-flex items-center gap-1 text-xs font-medium text-emerald-400 bg-emerald-900/30 border border-emerald-700 px-2 py-0.5 rounded-full">
                          <span className="w-1.5 h-1.5 rounded-full bg-emerald-500" />
                          Published
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 text-xs font-medium text-amber-400 bg-amber-900/30 border border-amber-700 px-2 py-0.5 rounded-full">
                          <span className="w-1.5 h-1.5 rounded-full bg-amber-400" />
                          Draft
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-gray-400">v{flow.version}</td>
                    <td className="px-4 py-3 text-gray-500">{fmt(flow.updated_at)}</td>
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-2">
                        {!flow.is_active && (
                          <button
                            onClick={() => handlePublish(flow.id)}
                            disabled={publishingId === flow.id}
                            className="text-xs text-sky-400 hover:text-sky-300 border border-sky-800 hover:border-sky-600 rounded px-2.5 py-1 disabled:opacity-50 transition-colors"
                          >
                            {publishingId === flow.id ? 'Publishing…' : 'Publish'}
                          </button>
                        )}
                        <button
                          onClick={() => navigate(designerPath(flow))}
                          className="text-xs text-gray-400 hover:text-gray-200 border border-gray-700 hover:border-gray-500 rounded px-2.5 py-1 transition-colors"
                        >
                          Edit
                        </button>
                        <button
                          onClick={() => handleExport(flow)}
                          disabled={exportingId === flow.id}
                          className="text-xs text-gray-400 hover:text-gray-200 border border-gray-700 hover:border-gray-500 rounded px-2.5 py-1 disabled:opacity-50 transition-colors"
                        >
                          {exportingId === flow.id ? 'Exporting…' : 'Export'}
                        </button>
                        <button
                          onClick={() => handleDelete(flow.id, flow.name)}
                          disabled={deletingId === flow.id}
                          className="text-xs text-red-400 hover:text-red-300 border border-red-900 hover:border-red-700 rounded px-2.5 py-1 disabled:opacity-50 transition-colors"
                        >
                          {deletingId === flow.id ? 'Deleting…' : 'Delete'}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
