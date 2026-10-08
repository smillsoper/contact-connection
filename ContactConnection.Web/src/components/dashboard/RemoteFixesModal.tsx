import { useCallback, useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { api } from '../../api/client'
import { useAuthStore } from '../../stores/authStore'
import { CheckIcon, CloseIcon, WarningIcon, WrenchIcon } from '../icons/Icons'

/**
 * Remote fixes (S184): fix an agent's portal from the dashboard. Diagnostics and the extension re-check only look
 * (monitor); re-registering the softphone, clearing a stuck call screen and refreshing change something (override).
 * Results come back live from the agent's portal; the agent sees a notice each time.
 */

interface RemoteAction {
  id: string
  requestedByName: string
  action: string
  requestedAt: string
  completedAt: string | null
  ok: boolean | null
  detail: string | null
}

const FIXES: { action: string; label: string; what: string; disruptive: boolean }[] = [
  { action: 'diagnostics', label: 'Run diagnostics', what: 'Softphone, microphone, devices, extension, connections, network — problems highlighted.', disruptive: false },
  { action: 'extension', label: 'Re-check extension', what: 'Asks the browser extension to report in again.', disruptive: false },
  { action: 'reregister', label: 'Re-register softphone', what: 'Drops and redoes the phone registration. Not during a call.', disruptive: true },
  { action: 'clear-call', label: 'Clear stuck call screen', what: 'Resets a call panel stuck with no call connected.', disruptive: true },
  { action: 'refresh', label: 'Refresh portal', what: 'Reloads their portal page — the cure-all. Asks first during a live call.', disruptive: true },
]

const ACTION_LABEL: Record<string, string> = Object.fromEntries(FIXES.map((f) => [f.action, f.label]))

/** Diagnostics → a checklist: [label, value, problem?]. */
function checklist(d: Record<string, unknown>): [string, string, 'ok' | 'warn' | 'bad'][] {
  const v = (x: unknown) => (x === null || x === undefined || x === '' ? '—' : String(x))
  return [
    ['Softphone', d.softphone === 'registered' ? `Registered${d.extensionNumber ? ` (ext ${d.extensionNumber})` : ''}` : `Not registered (${v(d.softphone)})`, d.softphone === 'registered' ? 'ok' : 'bad'],
    ['Microphone permission', v(d.micPermission), d.micPermission === 'granted' ? 'ok' : d.micPermission === 'prompt' ? 'warn' : 'bad'],
    ['Audio devices', `${v(d.audioInputs)} input · ${v(d.audioOutputs)} output`, Number(d.audioInputs) > 0 ? 'ok' : 'bad'],
    ['Call quality', v(d.callQuality), String(d.callQuality).startsWith('poor') ? 'bad' : String(d.callQuality).startsWith('fair') ? 'warn' : 'ok'],
    ['Call screen', v(d.callScreen), 'ok'],
    ['Browser extension', d.extensionInstalled ? `Installed (${v(d.extensionVersion)})` : 'Not answering', d.extensionInstalled ? 'ok' : 'warn'],
    ['Screen share', v(d.screenShare), 'ok'],
    ['Team chat', v(d.teamChat), d.teamChat === 'ready' || d.teamChat === 'disabled' ? 'ok' : 'warn'],
    ['Network', `${d.online ? 'Online' : 'OFFLINE'}${d.network ? ` · ${v(d.network)}` : ''}${d.downlinkMbps ? ` · ${v(d.downlinkMbps)} Mbps` : ''}`, d.online ? 'ok' : 'bad'],
    ['Portal tab', `${d.pageVisible ? 'In front' : 'In the background'} · open ${v(d.portalOpenMinutes)} min`, 'ok'],
    ['Browser', v(d.browser), 'ok'],
  ]
}

export default function RemoteFixesModal({ agentId, agentName, onClose }: { agentId: string; agentName: string; onClose: () => void }) {
  const [rows, setRows] = useState<RemoteAction[]>([])
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirmRefresh, setConfirmRefresh] = useState(false)
  const canChange = useAuthStore((s) => s.hasPermission('supervisor.override'))
  const [, tick] = useState(0)

  const load = useCallback(() => { api.get<RemoteAction[]>(`/api/v1/remote-actions?agentId=${agentId}`).then(setRows).catch(() => {}) }, [agentId])
  useEffect(() => {
    load()
    const on = (e: Event) => {
      const d = (e as CustomEvent<{ id: string; ok: boolean; detail: string | null }>).detail
      setRows((list) => list.map((r) => (r.id === d.id ? { ...r, ok: d.ok, detail: d.detail, completedAt: new Date().toISOString() } : r)))
    }
    window.addEventListener('cc:remote-result', on)
    const t = setInterval(() => tick((n) => n + 1), 1000)
    return () => { window.removeEventListener('cc:remote-result', on); clearInterval(t) }
  }, [load])

  async function run(action: string, force = false) {
    setBusy(action); setError(null)
    try {
      const r = await api.post<RemoteAction>('/api/v1/remote-actions', { agentId, action, force })
      setRows((list) => [r, ...list])
      setConfirmRefresh(false)
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Could not send the fix.'
      if (action === 'refresh' && /live call/i.test(msg)) setConfirmRefresh(true)
      else setError(msg)
    } finally { setBusy(null) }
  }

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div className="w-full max-w-2xl max-h-[88vh] overflow-y-auto rounded-xl border border-gray-700 bg-gray-900 shadow-2xl">
        <div className="flex items-center gap-2 px-5 py-3 border-b border-gray-800">
          <WrenchIcon size={16} className="text-sky-300" />
          <h2 className="text-white font-semibold flex-1">Fix {agentName}'s portal</h2>
          <button onClick={onClose} className="text-gray-500 hover:text-white"><CloseIcon size={16} /></button>
        </div>
        <div className="p-5 space-y-4 text-sm">
          <div className="grid gap-2 sm:grid-cols-2">
            {FIXES.map((f) => {
              const allowed = !f.disruptive || canChange
              return (
                <button key={f.action} disabled={!allowed || busy !== null} onClick={() => void run(f.action)}
                  title={allowed ? f.what : 'Needs override permission'}
                  className={`text-left rounded-lg border px-3 py-2 transition-colors disabled:opacity-40 ${f.action === 'refresh' ? 'border-amber-800 hover:border-amber-600' : 'border-gray-700 hover:border-sky-600'}`}>
                  <div className="text-white text-sm">{busy === f.action ? 'Sending…' : f.label}</div>
                  <div className="text-[11px] text-gray-500 leading-snug mt-0.5">{f.what}</div>
                </button>
              )
            })}
          </div>
          {confirmRefresh && (
            <div className="rounded-lg border border-amber-700 bg-amber-950/40 px-3 py-2 text-xs text-amber-100 flex items-center gap-3">
              <WarningIcon size={14} />
              <span className="flex-1">{agentName} is on a live call — refreshing will drop it.</span>
              <button onClick={() => void run('refresh', true)} className="bg-amber-600 hover:bg-amber-500 text-white rounded px-2.5 py-1">Refresh anyway</button>
              <button onClick={() => setConfirmRefresh(false)} className="text-amber-300 hover:text-white">Cancel</button>
            </div>
          )}
          {error && <p className="text-xs text-red-400">{error}</p>}
          <p className="text-[11px] text-gray-500">{agentName} sees a notice each time. Every fix is recorded.</p>

          {rows.length > 0 && (
            <ul className="space-y-2">
              {rows.map((r) => {
                const waiting = r.completedAt === null
                const slow = waiting && Date.now() - new Date(r.requestedAt).getTime() > 15000
                let diag: Record<string, unknown> | null = null
                if (r.action === 'diagnostics' && r.detail) { try { diag = JSON.parse(r.detail) } catch { /* plain text */ } }
                return (
                  <li key={r.id} className="rounded-lg border border-gray-800 bg-gray-950/60 px-3 py-2">
                    <div className="flex items-center gap-2 text-xs">
                      {waiting ? <span className={slow ? 'text-amber-300' : 'text-gray-400'}>{slow ? 'No answer from their portal — is it open?' : 'Waiting for their portal…'}</span>
                        : r.ok ? <CheckIcon size={13} className="text-emerald-400" /> : <WarningIcon size={13} className="text-red-400" />}
                      <span className="text-gray-200 font-medium">{ACTION_LABEL[r.action] ?? r.action}</span>
                      <span className="text-gray-500 ml-auto">{new Date(r.requestedAt).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' })} · {r.requestedByName}</span>
                    </div>
                    {diag ? (
                      <table className="mt-2 w-full text-xs">
                        <tbody>
                          {checklist(diag).map(([k, v, s]) => (
                            <tr key={k} className="border-t border-gray-800/60">
                              <td className="py-1 pr-3 text-gray-400 whitespace-nowrap">{k}</td>
                              <td className={`py-1 ${s === 'bad' ? 'text-red-300' : s === 'warn' ? 'text-amber-300' : 'text-gray-200'}`}>
                                {s !== 'ok' && <WarningIcon size={11} className="inline -mt-0.5 mr-1" />}{v}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    ) : r.detail && <p className={`mt-1 text-xs ${r.ok ? 'text-gray-300' : 'text-red-300'}`}>{r.detail}</p>}
                  </li>
                )
              })}
            </ul>
          )}
        </div>
      </div>
    </div>,
    document.body,
  )
}
