import { useEffect, useState } from 'react'
import type { CredentialAuditEntrySummary } from '../../api/credentialAudit'

interface CredentialAuditPanelProps {
  keyName: string
  listAudit: (keyName: string) => Promise<CredentialAuditEntrySummary[]>
  onClose: () => void
}

const ACTION_LABEL: Record<string, string> = { set: 'Set', delete: 'Deleted' }
const ACTION_COLOR: Record<string, string> = {
  set: 'bg-emerald-900/50 text-emerald-400',
  delete: 'bg-red-900/50 text-red-400',
}

// Read-only audit trail for a single credential's Set/Delete history — who changed it and when,
// never the secret value (there is nothing to revert to, unlike VersionHistoryPanel; the actual
// value only ever lives in Key Vault). See API_HARDENING_CHECKLIST.md Tier 1.
export default function CredentialAuditPanel({ keyName, listAudit, onClose }: CredentialAuditPanelProps) {
  const [entries, setEntries] = useState<CredentialAuditEntrySummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    listAudit(keyName)
      .then(setEntries)
      .catch((e: unknown) => setError((e as Error).message))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [keyName])

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4" onClick={onClose}>
      <div
        className="bg-gray-900 border border-gray-700 rounded-2xl w-full max-w-lg shadow-2xl flex flex-col max-h-[80vh]"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="px-5 py-4 border-b border-gray-800 flex items-start justify-between shrink-0">
          <div className="min-w-0">
            <h2 className="text-white text-sm font-semibold">Credential Audit Trail</h2>
            <p className="text-gray-500 text-xs mt-0.5 truncate font-mono">{keyName}</p>
          </div>
          <button onClick={onClose} className="text-gray-500 hover:text-white text-lg leading-none px-1 shrink-0">×</button>
        </div>

        <div className="flex-1 overflow-y-auto px-5 py-4">
          <p className="text-gray-600 text-xs mb-3">
            Records who changed this credential and when — the secret value itself is never logged.
          </p>
          {error && <p className="text-red-400 text-xs mb-3">{error}</p>}
          {entries === null && !error && <p className="text-gray-500 text-sm">Loading…</p>}
          {entries !== null && entries.length === 0 && (
            <p className="text-gray-500 text-sm">No audit history yet.</p>
          )}
          {entries !== null && entries.length > 0 && (
            <div className="space-y-2">
              {entries.map((e, i) => (
                <div key={i} className="bg-gray-800/40 border border-gray-700/50 rounded-lg px-3 py-2.5">
                  <div className="flex items-center gap-2">
                    <span className={`text-xs px-1.5 py-0.5 rounded font-medium shrink-0 ${ACTION_COLOR[e.action] ?? 'bg-gray-800 text-gray-300'}`}>
                      {ACTION_LABEL[e.action] ?? e.action}
                    </span>
                    <span className="text-gray-300 text-sm truncate">{e.actorName}</span>
                  </div>
                  <p className="text-gray-500 text-[11px] mt-1">
                    {new Date(e.createdAt).toLocaleString()}
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
