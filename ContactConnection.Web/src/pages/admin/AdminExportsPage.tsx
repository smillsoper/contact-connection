import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import { exportsApi, STATUS_LABEL, STATUS_STYLE, type ExportDefinition } from '../../api/exports'

// Data Exports (S180, Export Worker) — every export file this tenant sends (e.g. the nightly media-agency files), with
// where each one is in the vendor lifecycle and how its last file went.

export function when(iso: string | null | undefined) {
  return iso ? new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '—'
}

export default function AdminExportsPage() {
  const [items, setItems] = useState<ExportDefinition[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => { exportsApi.list().then(setItems).catch((e: Error) => setError(e.message)) }, [])

  return (
    <AdminShell>
      <div className="max-w-5xl mx-auto px-4 sm:px-6 py-6">
        <div className="flex flex-wrap items-center justify-between gap-3 mb-2">
          <h1 className="text-xl font-semibold text-white">Data Exports</h1>
          <Link to="/admin/exports/new" className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg">New export</Link>
        </div>
        <p className="text-sm text-gray-400 mb-6">
          Files built from your calls for clients and vendors (media agencies, fulfillment houses). Each export goes
          draft → testing with the vendor → vendor approved → live. Test files can be generated at any stage.
        </p>
        {error && <p className="text-red-400 text-sm mb-4">{error}</p>}
        {items === null ? <p className="text-gray-500 text-sm">Loading…</p>
          : items.length === 0 ? <p className="text-gray-500 italic text-sm">No exports yet.</p>
          : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-gray-500 border-b border-gray-700">
                    <th className="py-2 pr-3 font-medium">Export</th>
                    <th className="py-2 pr-3 font-medium">Status</th>
                    <th className="py-2 pr-3 font-medium">Last file</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((d) => (
                    <tr key={d.id} className="border-b border-gray-800 align-top">
                      <td className="py-2 pr-3">
                        <Link to={`/admin/exports/${d.id}`} className="text-indigo-400 hover:text-indigo-300 font-medium">{d.name}</Link>
                        {d.description && <span className="block text-xs text-gray-500">{d.description}</span>}
                      </td>
                      <td className="py-2 pr-3 whitespace-nowrap">
                        <span className={`px-2 py-0.5 rounded text-xs ${STATUS_STYLE[d.status]}`}>{STATUS_LABEL[d.status]}</span>
                        {d.changedSinceApproval && <span className="block text-xs text-amber-400 mt-1">Changed since vendor approval</span>}
                      </td>
                      <td className="py-2 pr-3 text-gray-300">
                        {d.lastRun ? (
                          <>
                            <span className={d.lastRun.status === 'failed' ? 'text-red-400' : d.lastRun.status === 'succeeded' ? 'text-gray-200' : 'text-amber-300'}>
                              {d.lastRun.fileName ?? d.lastRun.status}
                            </span>
                            {d.lastRun.isTest && <span className="ml-2 text-xs text-amber-300">test</span>}
                            <span className="block text-xs text-gray-500">{when(d.lastRun.queuedAt)}</span>
                          </>
                        ) : <span className="text-gray-500">None yet</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
      </div>
    </AdminShell>
  )
}
