import { useState } from 'react'
import PortalShell from '../../components/portal/PortalShell'
import { migrateTenants, type MigrateTenantsResult } from '../../api/portal'

export default function MaintenancePage() {
  const [running, setRunning] = useState(false)
  const [result, setResult] = useState<MigrateTenantsResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [ranAt, setRanAt] = useState<Date | null>(null)

  async function handleRun() {
    setRunning(true)
    setError(null)
    setResult(null)
    try {
      const r = await migrateTenants()
      setResult(r)
      setRanAt(new Date())
    } catch (e: unknown) {
      setError((e as Error).message)
    } finally {
      setRunning(false)
    }
  }

  return (
    <PortalShell>
      <div className="p-6 max-w-2xl">
        <div className="mb-6">
          <h1 className="text-white text-xl font-semibold">Maintenance</h1>
          <p className="text-gray-400 text-sm mt-0.5">
            Platform-wide operational tasks.
          </p>
        </div>

        <div className="bg-gray-900 rounded-xl border border-gray-800 p-5">
          <h2 className="text-white text-sm font-semibold mb-1">Apply tenant migrations</h2>
          <p className="text-gray-400 text-xs leading-relaxed mb-4">
            Runs any pending EF Core migrations against every tenant's PostgreSQL schema. Safe to
            run repeatedly — schemas already up to date are left untouched. Use this after a
            release that adds a migration, or to reconcile a tenant schema that's fallen behind.
          </p>

          <button
            onClick={handleRun}
            disabled={running}
            className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors"
          >
            {running ? 'Running…' : 'Run tenant migrations'}
          </button>

          {error && (
            <p className="text-red-400 text-sm mt-4">{error}</p>
          )}

          {result && (
            <div className="mt-4 pt-4 border-t border-gray-800">
              <p className="text-sm">
                <span className="text-emerald-400 font-medium">{result.migrated}</span>
                <span className="text-gray-400"> tenant{result.migrated === 1 ? '' : 's'} migrated successfully</span>
                {ranAt && <span className="text-gray-600"> · {ranAt.toLocaleTimeString()}</span>}
              </p>
              {result.errors.length > 0 && (
                <div className="mt-3">
                  <p className="text-red-400 text-xs font-medium uppercase tracking-wide mb-1.5">
                    {result.errors.length} error{result.errors.length === 1 ? '' : 's'}
                  </p>
                  <ul className="space-y-1">
                    {result.errors.map((e, i) => (
                      <li key={i} className="text-red-300 text-xs bg-red-950/30 border border-red-900/50 rounded px-2.5 py-1.5">
                        {e}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
              {result.errors.length === 0 && (
                <p className="text-emerald-400 text-xs mt-1">No errors.</p>
              )}
            </div>
          )}
        </div>
      </div>
    </PortalShell>
  )
}
