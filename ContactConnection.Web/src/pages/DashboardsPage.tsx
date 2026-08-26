import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { dashboardsApi, type DashboardSummary } from '../api/dashboards'

export default function DashboardsPage() {
  const navigate = useNavigate()
  const [dashboards, setDashboards] = useState<DashboardSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [deletingId, setDeletingId] = useState<string | null>(null)

  async function load() {
    setLoading(true)
    setError(null)
    try {
      setDashboards(await dashboardsApi.list())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load dashboards')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  async function handleDelete(id: string, name: string) {
    if (!window.confirm(`Delete "${name}"? This cannot be undone.`)) return
    setDeletingId(id)
    try {
      await dashboardsApi.delete(id)
      await load()
    } finally {
      setDeletingId(null)
    }
  }

  function fmt(iso: string) {
    return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
  }

  return (
    <div className="min-h-screen bg-gray-950 flex flex-col">
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
            <span className="text-sm font-semibold text-white">Supervisor Dashboards</span>
          </div>
          <button
            onClick={() => navigate('/dashboard-builder')}
            className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-1.5 rounded-lg transition-colors"
          >
            + New Dashboard
          </button>
        </div>
      </div>

      <div className="flex-1 p-6">
        {loading ? (
          <div className="flex items-center justify-center h-40 text-gray-500 text-sm">Loading…</div>
        ) : error ? (
          <div className="flex flex-col items-center justify-center h-40 gap-3">
            <p className="text-red-400 text-sm font-medium">Error loading dashboards</p>
            <p className="text-red-500 text-xs font-mono">{error}</p>
            <button onClick={load} className="text-sm text-sky-400 hover:underline">Retry</button>
          </div>
        ) : dashboards.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-40 gap-3">
            <p className="text-gray-500 text-sm">No dashboards yet.</p>
            <button
              onClick={() => navigate('/dashboard-builder')}
              className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-1.5 rounded-lg transition-colors"
            >
              Create your first dashboard
            </button>
          </div>
        ) : (
          <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 bg-gray-800/50">
                  <th className="text-left px-4 py-3 text-xs font-semibold text-gray-400 uppercase tracking-wide">Name</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-gray-400 uppercase tracking-wide">Visibility</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-gray-400 uppercase tracking-wide">Updated</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {dashboards.map((d) => (
                  <tr key={d.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/40 transition-colors">
                    <td className="px-4 py-3 font-medium text-white">{d.name}</td>
                    <td className="px-4 py-3">
                      {d.is_shared ? (
                        <span className="inline-flex items-center text-xs font-medium text-sky-300 bg-sky-900/30 border border-sky-700 px-2 py-0.5 rounded-full">
                          Shared
                        </span>
                      ) : (
                        <span className="inline-flex items-center text-xs font-medium text-gray-400 bg-gray-800 border border-gray-700 px-2 py-0.5 rounded-full">
                          Private
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-gray-500">{fmt(d.updated_at)}</td>
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-2">
                        <button
                          onClick={() => navigate(`/dashboard-builder/${d.id}`)}
                          className="text-xs text-gray-400 hover:text-gray-200 border border-gray-700 hover:border-gray-500 rounded px-2.5 py-1 transition-colors"
                        >
                          Open
                        </button>
                        <button
                          onClick={() => handleDelete(d.id, d.name)}
                          disabled={deletingId === d.id}
                          className="text-xs text-red-400 hover:text-red-300 border border-red-900 hover:border-red-700 rounded px-2.5 py-1 disabled:opacity-50 transition-colors"
                        >
                          {deletingId === d.id ? 'Deleting…' : 'Delete'}
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
