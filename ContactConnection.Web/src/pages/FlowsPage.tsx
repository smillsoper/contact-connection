import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { flowsApi, type FlowSummary } from '../api/flows'

export default function FlowsPage() {
  const navigate = useNavigate()
  const [flows, setFlows] = useState<FlowSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [publishingId, setPublishingId] = useState<string | null>(null)
  const [deletingId, setDeletingId] = useState<string | null>(null)

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

  return (
    <div className="min-h-screen bg-gray-950 flex flex-col">
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
          <button
            onClick={() => navigate('/designer')}
            className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-1.5 rounded-lg transition-colors"
          >
            + New Flow
          </button>
        </div>
      </div>

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
              Create your first flow
            </button>
          </div>
        ) : (
          <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 bg-gray-800/50">
                  <th className="text-left px-4 py-3 text-xs font-semibold text-gray-400 uppercase tracking-wide">Name</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-gray-400 uppercase tracking-wide">Type / Direction</th>
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
                    <td className="px-4 py-3 text-gray-400">
                      <span className="capitalize">{flow.flow_type}</span>
                      {flow.flow_direction && (
                        <span className="ml-1 text-gray-500 capitalize">
                          · {flow.flow_direction}
                          {flow.flow_sub_type && ` / ${flow.flow_sub_type}`}
                        </span>
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
                          onClick={() => navigate(`/designer/${flow.id}`)}
                          className="text-xs text-gray-400 hover:text-gray-200 border border-gray-700 hover:border-gray-500 rounded px-2.5 py-1 transition-colors"
                        >
                          Edit
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
