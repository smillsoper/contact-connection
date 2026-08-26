import { useEffect, useState } from 'react'
import type { EntityVersionSummary } from '../../api/versioning'

interface VersionHistoryPanelProps {
  title: string
  subtitle?: string
  listVersions: () => Promise<EntityVersionSummary[]>
  /** Applies the given version's snapshot back onto the live entity. The caller is responsible
   *  for updating its own state with whatever the revert response returns. */
  onRevert: (versionNumber: number) => Promise<void>
  onClose: () => void
}

// Reusable version-history modal — shared by API Definition/Endpoint detail (Admin + Portal)
// and the Flow Designer. Every version is retained forever (nothing is ever deleted); reverting
// creates a brand-new version rather than rewinding history, so the list only ever grows.
export default function VersionHistoryPanel({ title, subtitle, listVersions, onRevert, onClose }: VersionHistoryPanelProps) {
  const [versions, setVersions] = useState<EntityVersionSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [revertingVersion, setRevertingVersion] = useState<number | null>(null)

  useEffect(() => {
    load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function load() {
    setError(null)
    try {
      const list = await listVersions()
      setVersions([...list].sort((a, b) => b.versionNumber - a.versionNumber))
    } catch (e: unknown) {
      setError((e as Error).message)
    }
  }

  async function handleRevert(v: EntityVersionSummary) {
    if (!confirm(`Revert to version ${v.versionNumber}? This creates a new version with that content — nothing is deleted.`)) return
    setRevertingVersion(v.versionNumber)
    setError(null)
    try {
      await onRevert(v.versionNumber)
      await load()
    } catch (e: unknown) {
      setError((e as Error).message)
    } finally {
      setRevertingVersion(null)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4" onClick={onClose}>
      <div
        className="bg-gray-900 border border-gray-700 rounded-2xl w-full max-w-lg shadow-2xl flex flex-col max-h-[80vh]"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="px-5 py-4 border-b border-gray-800 flex items-start justify-between shrink-0">
          <div className="min-w-0">
            <h2 className="text-white text-sm font-semibold">{title}</h2>
            {subtitle && <p className="text-gray-500 text-xs mt-0.5 truncate">{subtitle}</p>}
          </div>
          <button onClick={onClose} className="text-gray-500 hover:text-white text-lg leading-none px-1 shrink-0">×</button>
        </div>

        <div className="flex-1 overflow-y-auto px-5 py-4">
          {error && <p className="text-red-400 text-xs mb-3">{error}</p>}
          {versions === null && !error && <p className="text-gray-500 text-sm">Loading…</p>}
          {versions !== null && versions.length === 0 && (
            <p className="text-gray-500 text-sm">No version history yet.</p>
          )}
          {versions !== null && versions.length > 0 && (
            <div className="space-y-2">
              {versions.map((v) => (
                <div key={v.versionNumber} className="bg-gray-800/40 border border-gray-700/50 rounded-lg px-3 py-2.5">
                  <div className="flex items-center justify-between gap-2">
                    <div className="flex items-center gap-2 min-w-0">
                      <span className="text-white text-sm font-medium shrink-0">v{v.versionNumber}</span>
                      {v.isActive && (
                        <span className="text-xs px-1.5 py-0.5 rounded bg-emerald-900/50 text-emerald-400 font-medium shrink-0">Current</span>
                      )}
                      {v.changeSummary && (
                        <span className="text-gray-400 text-xs truncate">{v.changeSummary}</span>
                      )}
                    </div>
                    {!v.isActive && (
                      <button
                        onClick={() => handleRevert(v)}
                        disabled={revertingVersion !== null}
                        className="text-indigo-400 hover:text-indigo-300 text-xs font-medium transition-colors disabled:opacity-50 shrink-0"
                      >
                        {revertingVersion === v.versionNumber ? 'Reverting…' : 'Revert'}
                      </button>
                    )}
                  </div>
                  <p className="text-gray-500 text-[11px] mt-1">
                    {v.createdByName} · {new Date(v.createdAt).toLocaleString()}
                  </p>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
