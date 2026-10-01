import { useEffect, useState } from 'react'
import { flowsApi, type ApiRequestPreview, type PreviewSession } from '../../api/flows'

// API Call node → "Preview request" (S169). Renders the request — URL, query, headers, body (Liquid or
// simple) — against a real past session's data exactly as it would be sent, and sends nothing. Lets a
// client integration's payload be checked before any live order goes out. Credentials are never shown.

function prettyBody(body: string | null): string {
  if (!body) return ''
  try { return JSON.stringify(JSON.parse(body), null, 2) } catch { return body }
}

export default function ApiRequestPreviewModal({
  flowId, nodeId, node, onClose,
}: {
  flowId: string | null
  nodeId: string
  node: Record<string, unknown>
  onClose: () => void
}) {
  const [sessions, setSessions] = useState<PreviewSession[] | null>(null)
  const [sessionId, setSessionId] = useState('')
  const [preview, setPreview] = useState<ApiRequestPreview | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    flowsApi.previewSessions(flowId)
      .then((s) => { setSessions(s); if (s[0]) setSessionId(s[0].sessionId) })
      .catch((e: Error) => setError(e.message))
  }, [flowId])

  async function run() {
    if (!sessionId) return
    setBusy(true); setError(null); setPreview(null)
    try { setPreview(await flowsApi.previewApiCall(flowId, sessionId, nodeId, node)) }
    catch (e) { setError(e instanceof Error ? e.message : 'Preview failed.') }
    finally { setBusy(false) }
  }

  const label = (s: PreviewSession) =>
    `${new Date(s.startedAt).toLocaleString()} · ${s.callerName || s.callerId || 'unknown caller'} · ${s.status}`

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4" onClick={onClose}>
      <div className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-3xl max-h-[90vh] flex flex-col" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between px-5 py-3 border-b border-gray-700">
          <div>
            <h2 className="text-white font-semibold">Preview request</h2>
            <p className="text-xs text-gray-400">Rendered against a real call's data. Nothing is sent.</p>
          </div>
          <button onClick={onClose} className="text-gray-400 hover:text-white text-xl leading-none">&times;</button>
        </div>

        <div className="px-5 py-3 border-b border-gray-800 flex flex-wrap items-end gap-2">
          <label className="flex-1 min-w-[16rem]">
            <span className="block text-xs text-gray-400 mb-1">
              Call data from {sessions?.[0]?.otherFlow ? 'a recent session of another flow (this flow has none yet)' : 'a recent session of this flow'}
            </span>
            <select value={sessionId} onChange={(e) => setSessionId(e.target.value)}
              className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm">
              {sessions === null && <option>Loading…</option>}
              {sessions?.length === 0 && <option value="">No sessions yet — run the flow once first</option>}
              {sessions?.map((s) => <option key={s.sessionId} value={s.sessionId}>{label(s)}</option>)}
            </select>
          </label>
          <button onClick={run} disabled={busy || !sessionId}
            className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white text-sm rounded-lg">
            {busy ? 'Rendering…' : 'Preview'}
          </button>
        </div>

        <div className="overflow-y-auto flex-1 px-5 py-4 space-y-4 text-sm">
          {error && <p className="text-red-400">{error}</p>}
          {preview && (
            <>
              {preview.error && (
                <div className="bg-red-950/50 border border-red-800 rounded-lg px-3 py-2 text-red-300 text-xs">
                  This request would NOT be sent: {preview.error}
                </div>
              )}
              {preview.endpointName && <p className="text-xs text-gray-400">{preview.endpointName}</p>}
              {preview.method && (
                <div className="font-mono text-xs break-all">
                  <span className="text-emerald-400 font-semibold mr-2">{preview.method}</span>
                  <span className="text-gray-200">{preview.url}</span>
                </div>
              )}
              <div>
                <p className="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-1">Headers</p>
                {Object.keys(preview.headers).length === 0 ? (
                  <p className="text-xs text-gray-500 italic">None configured.</p>
                ) : (
                  <table className="text-xs font-mono">
                    <tbody>
                      {Object.entries(preview.headers).map(([k, v]) => (
                        <tr key={k}><td className="text-gray-400 pr-3 align-top">{k}</td><td className="text-gray-200 break-all">{v}</td></tr>
                      ))}
                    </tbody>
                  </table>
                )}
                <p className="text-[11px] text-gray-500 mt-1">
                  Auth: {preview.authType && preview.authType !== 'none' ? `${preview.authType} — added when sent, never shown here` : 'none'}
                </p>
              </div>
              <div>
                <p className="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-1">
                  Body {preview.bodyTemplateType ? `(${preview.bodyTemplateType} template)` : ''}
                </p>
                {preview.body ? (
                  <pre className="bg-gray-950 border border-gray-800 rounded-lg p-3 text-xs text-gray-200 overflow-x-auto whitespace-pre">{prettyBody(preview.body)}</pre>
                ) : (
                  <p className="text-xs text-gray-500 italic">No body.</p>
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
