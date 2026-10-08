import { useCallback, useEffect, useState } from 'react'
import { endSupportSession, listSupportSessions, startSupportSession, type SupportSessionRecord } from '../../api/portal'

/**
 * Open the tenant's portal (S184): give a reason, and the tenant's portal opens in a new tab signed in as your own
 * support account there — every permission (card-data exports only while the tenant's switch is on), 60 minutes.
 * Every session is listed here and on the tenant's own Support Access page.
 */

const PLATFORM_DOMAINS = ['contactconnection.cc', 'contactconnection.io', 'cc.local']

/** The tenant's portal address: its own subdomain in production, ?subdomain= on localhost. */
function tenantPortalUrl(subdomain: string, code: string) {
  const host = window.location.hostname
  const domain = PLATFORM_DOMAINS.find((d) => host === d || host.endsWith(`.${d}`))
  const base = domain ? `${window.location.protocol}//${subdomain}.${domain}${window.location.port ? `:${window.location.port}` : ''}`
    : window.location.origin
  // The single-use code rides in the fragment, which never reaches a server or its logs.
  return `${base}/support-login${domain ? '' : `?subdomain=${encodeURIComponent(subdomain)}`}#code=${encodeURIComponent(code)}`
}

const fmt = (s: string) => new Date(s).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
const minutes = (a: string, b: string) => Math.max(1, Math.round((new Date(b).getTime() - new Date(a).getTime()) / 60000))

export default function TenantSupportCard({ tenantId, tenantActive }: { tenantId: string; tenantActive: boolean }) {
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [sessions, setSessions] = useState<SupportSessionRecord[]>([])
  const [showAll, setShowAll] = useState(false)

  const load = useCallback(() => { listSupportSessions(tenantId).then(setSessions).catch(() => {}) }, [tenantId])
  useEffect(() => { load() }, [load])

  async function open() {
    if (reason.trim().length < 3) { setError('Say briefly why you\'re opening this portal (it\'s shown to the tenant).'); return }
    // Open the tab now (inside the click) so the browser doesn't block it, then point it at the portal.
    const tab = window.open('about:blank', '_blank')
    setBusy(true); setError(null)
    try {
      const s = await startSupportSession(tenantId, reason.trim())
      const url = tenantPortalUrl(s.subdomain, s.code)
      if (tab) { tab.opener = null; tab.location.href = url } else window.open(url, '_blank', 'noopener')
      setReason('')
      load()
    } catch (e) {
      tab?.close()
      setError(e instanceof Error ? e.message : 'Could not open the portal.')
    } finally { setBusy(false) }
  }

  async function end(id: string) {
    try { await endSupportSession(id); load() } catch (e) { setError(e instanceof Error ? e.message : 'Could not end it.') }
  }

  const shown = showAll ? sessions : sessions.slice(0, 5)
  return (
    <section className="bg-gray-900 rounded-xl border border-sky-900/60 p-5 mb-4">
      <h2 className="text-white text-sm font-semibold mb-1">Support access</h2>
      <p className="text-gray-500 text-xs mb-3">
        Opens this tenant's portal in a new tab as your own support account, with full rights, for 60 minutes. The tenant's
        admins can see every session and its reason.
      </p>
      {!tenantActive && <p className="text-amber-400 text-xs mb-2">This tenant is inactive — its users can't sign in, but you can still look.</p>}
      <div className="flex items-center gap-2">
        <input value={reason} onChange={(e) => { setReason(e.target.value); setError(null) }} maxLength={300}
          onKeyDown={(e) => { if (e.key === 'Enter') void open() }}
          placeholder="Reason — e.g. Ticket 1042: campaign hours not saving"
          className="flex-1 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-sky-500" />
        <button onClick={() => void open()} disabled={busy}
          className="bg-sky-600 hover:bg-sky-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors shrink-0">
          {busy ? 'Opening…' : 'Open tenant portal'}
        </button>
      </div>
      {error && <p className="text-red-400 text-xs mt-2">{error}</p>}

      {sessions.length > 0 && (
        <div className="mt-4 pt-3 border-t border-gray-800">
          <p className="text-gray-500 text-xs mb-2">Recent sessions</p>
          <div className="flex flex-col gap-1.5">
            {shown.map((s) => (
              <div key={s.id} className="flex items-center gap-3 text-xs">
                <span className={`w-2 h-2 rounded-full shrink-0 ${s.active ? 'bg-emerald-400' : 'bg-gray-600'}`} />
                <span className="text-gray-300 shrink-0 w-28">{fmt(s.startedAt)}</span>
                <span className="text-white shrink-0">{s.name}</span>
                {s.platformRole === 'support' && <span className="text-sky-400 shrink-0">support</span>}
                <span className="text-gray-400 truncate flex-1" title={s.reason}>{s.reason}</span>
                <span className="text-gray-500 shrink-0">
                  {s.active ? 'active' : s.endedAt ? `${minutes(s.startedAt, s.endedAt)} min` : ''}
                </span>
                {s.active && (
                  <button onClick={() => void end(s.id)} className="text-red-400 hover:text-red-300 shrink-0">End</button>
                )}
              </div>
            ))}
          </div>
          {sessions.length > 5 && (
            <button onClick={() => setShowAll((v) => !v)} className="text-sky-400 hover:text-sky-300 text-xs mt-2">
              {showAll ? 'Show fewer' : `Show all ${sessions.length}`}
            </button>
          )}
        </div>
      )}
    </section>
  )
}
