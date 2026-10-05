import { Fragment, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import * as signalR from '@microsoft/signalr'
import AdminShell from '../../components/admin/AdminShell'
import CallCommissionsPanel from '../../components/admin/CallCommissionsPanel'
import AiSummaryPanel from '../../components/admin/AiSummaryPanel'
import { useAuthStore } from '../../stores/authStore'
import {
  abandonLabel,
  callReviewApi,
  type ApiCallNodeSummary,
  type ApiCallRerunResult,
  type CallAddress,
  type CallCustomField,
  type CallDetail,
  type CallSessionView,
} from '../../api/callReview'

const inputCls = 'bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 disabled:opacity-60 w-full'
const btnPrimary = 'bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded-lg px-3 py-1.5 text-sm font-medium'
const btnGhost = 'text-gray-400 hover:text-white text-sm px-3 py-1.5'

const money = (n: number | null | undefined) => (n == null ? '—' : `$${n.toFixed(2)}`)
const fmtDate = (iso: string | null | undefined) => (iso ? new Date(iso).toLocaleString() : '—')
function fmtPhone(p: string | null | undefined) {
  if (!p) return '—'
  const d = p.replace(/\D/g, '').replace(/^1(?=\d{10}$)/, '')
  return d.length === 10 ? `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}` : p
}

function Section({ title, children, right }: { title: string; children: React.ReactNode; right?: React.ReactNode }) {
  return (
    <section className="bg-gray-900 border border-gray-800 rounded-xl p-5">
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-white text-sm font-semibold">{title}</h2>
        {right}
      </div>
      {children}
    </section>
  )
}

/**
 * An editable copy of server data that survives live refreshes (S165): while the reviewer has
 * unsaved changes, a refresh doesn't overwrite them — `conflict` says the server value moved on
 * underneath (the agent changed it) so the section can say so. With no unsaved changes the form
 * simply follows the server. `markSaved` after a successful save makes the next refresh adopt the
 * saved values; `reset` (Undo) takes the latest server value.
 */
function useDraft<T>(server: T) {
  const serverKey = JSON.stringify(server)
  const [base, setBase] = useState<T>(server)
  const [form, setForm] = useState<T>(server)
  const dirty = JSON.stringify(form) !== JSON.stringify(base)
  const conflict = dirty && JSON.stringify(base) !== serverKey
  useEffect(() => {
    if (!dirty) { setBase(server); setForm(server) }
    // Only when the server data changes — not on every keystroke.
  }, [serverKey])
  return {
    form, setForm, base, dirty, conflict,
    reset: () => { setBase(server); setForm(server) },
    markSaved: () => setBase(form),
  }
}

function ConflictNote({ show }: { show: boolean }) {
  if (!show) return null
  return (
    <p className="text-amber-300 text-xs mt-2">
      The agent changed this since you started editing. Save keeps your version; Undo shows theirs.
    </p>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      <label className="text-gray-500 text-xs">{label}</label>
      {children}
    </div>
  )
}

/**
 * One call, end to end (S165): what the agent captured, what the flow's API calls returned, and —
 * for calls.manage — correcting the data and re-running an API call. Built for the order-failure
 * loop: the flow emails a reviewer when the Order API rejects an order; the reviewer opens the call
 * here, fixes what the error points at, and resubmits. Resubmit re-runs the flow's own API Call
 * node, so the order is sent exactly as the script sends it, and a node marked "once per call"
 * never posts twice.
 */
export default function AdminCallDetailPage() {
  const { id } = useParams<{ id: string }>()
  const canManage = useAuthStore((s) => s.hasPermission('calls.manage'))
  const [call, setCall] = useState<CallDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [live, setLive] = useState<'connecting' | 'live' | 'offline'>('connecting')
  const [updatedAt, setUpdatedAt] = useState<number | null>(null)
  const loadedRef = useRef(false)

  // A failed background refresh keeps showing the last good data — only the first load can fail
  // the page.
  const load = useCallback(() => {
    if (!id) return
    callReviewApi.get(id)
      .then((c) => { loadedRef.current = true; setCall(c) })
      .catch((e: Error) => { if (!loadedRef.current) setError(e.message) })
  }, [id])
  useEffect(load, [load])

  // Live: the server pushes receiveCallChanged for this call after every script step, cart/payment
  // change or review edit (FlowHub group "call:{id}"). A burst of steps collapses into one refresh.
  const token = useAuthStore((s) => s.token)
  const tenantSubdomain = useAuthStore((s) => s.tenantSubdomain)
  useEffect(() => {
    if (!id || !token) return
    let timer: ReturnType<typeof setTimeout> | null = null
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/flow?access_token=${token}`, { headers: { 'X-Tenant-Subdomain': tenantSubdomain ?? '' } })
      .withAutomaticReconnect()
      .build()
    connection.on('receiveCallChanged', (callRecordId: string) => {
      if (callRecordId.toLowerCase() !== id.toLowerCase()) return
      if (timer) clearTimeout(timer)
      timer = setTimeout(() => { load(); setUpdatedAt(Date.now()) }, 400)
    })
    const join = () => connection.invoke('JoinCallReview', id)
    // Groups are per connection — rejoin after an automatic reconnect, and catch up on anything missed.
    connection.onreconnecting(() => setLive('connecting'))
    connection.onreconnected(() => { join().then(() => { setLive('live'); load() }).catch(() => setLive('offline')) })
    connection.onclose(() => setLive('offline'))
    connection.start()
      .then(join)
      .then(() => setLive('live'))
      .catch((err) => { console.error('[SignalR] call review connection failed:', err); setLive('offline') })
    return () => { if (timer) clearTimeout(timer); connection.stop() }
  }, [id, token, tenantSubdomain, load])

  // "Updated just now" fades after a few seconds.
  const [, setTick] = useState(0)
  useEffect(() => {
    if (!updatedAt) return
    const t = setTimeout(() => setTick((n) => n + 1), 4000)
    return () => clearTimeout(t)
  }, [updatedAt])
  const justUpdated = updatedAt != null && Date.now() - updatedAt < 4000

  if (error) return <AdminShell><div className="p-6 text-red-400 text-sm">{error}</div></AdminShell>
  if (!call) return <AdminShell><div className="p-6 text-gray-500 text-sm">Loading…</div></AdminShell>

  const cartTotal = call.cart?.cartTotal ?? null
  const amountMismatch = call.authorizedAmount != null && cartTotal != null
    && Math.abs(call.authorizedAmount - cartTotal) >= 0.005
  const customerName = [call.contact.firstName, call.contact.lastName].filter(Boolean).join(' ')
  const agentInScript = call.sessions.some((s) => s.isLive)

  return (
    <AdminShell>
      <div className="p-6 max-w-6xl space-y-5">
        <div>
          <div className="flex items-center justify-between">
            <Link to="/admin/calls" className="text-gray-500 hover:text-gray-300 text-xs">← Call Records</Link>
            {/* "Live" = an agent still has a script open on this call. The page keeps listening
                either way (another reviewer's edit still refreshes it) — the connection state only
                shows when it's broken. */}
            <span className="text-xs flex items-center gap-1.5">
              {justUpdated && <span className="text-sky-300 mr-2">Updated just now</span>}
              {live !== 'live' ? (
                <span className="flex items-center gap-1.5 text-gray-500"
                  title={live === 'connecting' ? 'Connecting for live updates…' : 'Live updates unavailable — reload the page to refresh'}>
                  <span className={`w-2 h-2 rounded-full ${live === 'connecting' ? 'bg-amber-500' : 'bg-gray-600'}`} />
                  {live === 'connecting' ? 'Connecting…' : 'Not updating'}
                </span>
              ) : agentInScript ? (
                <span className="flex items-center gap-1.5 text-emerald-400" title="An agent has this call's script open — this page updates as they work it">
                  <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
                  Live — agent in script
                </span>
              ) : (
                <span className="text-gray-500" title="No agent has this call's script open">Script finished</span>
              )}
            </span>
          </div>
          <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1 mt-1">
            <h1 className="text-white text-xl font-semibold">{customerName || 'Call'} — {fmtDate(call.callStartAt ?? call.createdAt)}</h1>
            {call.orderNumber && <span className="text-gray-400 text-sm font-mono">Order #{call.orderNumber}</span>}
          </div>
          <div className="flex flex-wrap gap-x-5 gap-y-1 mt-2 text-xs text-gray-400">
            <span>Client: <span className="text-gray-200">{call.clientName ?? '—'}</span></span>
            <span>Campaign: <span className="text-gray-200">{call.campaignName ?? '—'}</span></span>
            <span>Agent: <span className="text-gray-200">{call.agentName ?? '—'}</span></span>
            <span>Caller ID: <span className="text-gray-200">{fmtPhone(call.callerId)}</span></span>
            <span>Billing phone: <span className="text-gray-200">{fmtPhone(call.contact.billingPhone)}</span></span>
            <span>Shipping phone: <span className="text-gray-200">{fmtPhone(call.contact.shippingPhone)}</span></span>
            <span>DNIS: <span className="text-gray-200">{fmtPhone(call.dnis)}</span></span>
            <span>Source: <span className="text-gray-200">{call.source}</span></span>
            <span>Status: <span className="text-gray-200">{call.overallStatus}</span></span>
            {call.abandon && (
              <span>Abandoned: <span className="text-amber-300">{abandonLabel(call.abandon).replace(/^Abandoned · /, '')}</span>
                <span className="text-gray-500"> at {fmtDate(call.abandon.at)}</span></span>
            )}
            {call.handleTimeSeconds != null && <span>Handle time: <span className="text-gray-200">{Math.floor(call.handleTimeSeconds / 60)}m {call.handleTimeSeconds % 60}s</span></span>}
          </div>
        </div>

        {call.mediaAttribution && (() => {
          const m = call.mediaAttribution
          const rows: [string, string | null][] = [
            ['Agency', m.agency], ['Station', m.station],
            ['Media type', m.mediaType], ['Ad type', m.adType],
            ['Assignment start', m.startDate], ['Number', fmtPhone(m.phoneNumber)],
            ['Placed by', m.locationSource && m.distanceMiles != null
              ? `${m.locationSource === 'zip' ? 'caller zip' : 'caller area code'} ${m.locationKey} · ${m.distanceMiles} mi from station`
              : null],
            ...Object.entries(m.fields),
          ]
          return (
            <div className="bg-gray-900 border border-gray-800 rounded-xl px-4 py-3">
              <p className="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-2">
                Media attribution <span className="normal-case font-normal">— {m.marketType}, as assigned to this call</span>
              </p>
              <div className="flex flex-wrap gap-x-5 gap-y-1 text-xs text-gray-400">
                {rows.filter(([, v]) => v).map(([k, v]) => (
                  <span key={k}>{k}: <span className="text-gray-200">{v}</span></span>
                ))}
              </div>
            </div>
          )
        })()}

        {amountMismatch && (
          <div className="bg-amber-950/40 border border-amber-800 text-amber-200 rounded-lg px-4 py-3 text-sm">
            The cart total ({money(cartTotal)}) no longer matches the authorized payment ({money(call.authorizedAmount)}).
            {call.cardData.onFile
              ? ' Re-authorize the card (below) before resubmitting the order.'
              : " The card is no longer on file, so an order resubmitted now reports the authorized amount — confirm with the client that this is acceptable, or have the customer's card re-authorized."}
          </div>
        )}

        <FinalizePanel call={call} canManage={canManage} onChanged={load} />

        <CallCommissionsPanel callId={call.id} canManage={canManage} version={call} />

        <AiSummaryPanel callId={call.id} canManage={canManage} onChanged={load} />

        <CardOnFileNote call={call} />

        {call.sessions.map((s) => (
          <ApiCallsPanel key={s.id} call={call} session={s} canManage={canManage} onChanged={load} />
        ))}

        <ContactPanel call={call} canManage={canManage} onChanged={load} />

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
          <AddressPanel call={call} role="billing" canManage={canManage} onChanged={load} />
          <AddressPanel call={call} role="shipping" canManage={canManage} onChanged={load} />
        </div>

        <CartPanel call={call} canManage={canManage} onChanged={load} />

        <CustomFieldsPanel call={call} canManage={canManage} onChanged={load} />

        {call.sessions.map((s) => (
          <VariablesPanel key={s.id} callId={call.id} session={s} canManage={canManage} onChanged={load} />
        ))}

        <PaymentsPanel call={call} />
        <OtherPanel call={call} />
        <AuditPanel call={call} />
      </div>
    </AdminShell>
  )
}

// ── API calls (order post) ─────────────────────────────────────────────────

function statusBadge(a: ApiCallNodeSummary) {
  if (a.success === 'true') return <span className="bg-emerald-900/50 text-emerald-300 border border-emerald-800 rounded px-1.5 py-0.5 text-xs">Succeeded</span>
  if (a.success === 'false') return <span className="bg-red-900/50 text-red-300 border border-red-800 rounded px-1.5 py-0.5 text-xs">Failed</span>
  return <span className="bg-gray-800 text-gray-400 border border-gray-700 rounded px-1.5 py-0.5 text-xs">Not run</span>
}

// ── Finalize ───────────────────────────────────────────────────────────────

/**
 * Supervisor close-out (S166): the agent's script went away and the call needs closing out, or an
 * agent has to be relieved / terminated mid-call. Hangs up a still-connected caller (only once the
 * supervisor ticks the confirmation), closes any open script on the agent's screen (checkmark, tab
 * closes), marks the call complete with who/when/why, and can lock the agent.
 */
function FinalizePanel({ call, canManage, onChanged }: { call: CallDetail; canManage: boolean; onChanged: () => void }) {
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const [lock, setLock] = useState<'none' | 'status' | 'sign_in'>('none')
  const [lockReason, setLockReason] = useState('')
  const [confirmLive, setConfirmLive] = useState(false)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)

  if (call.finalized) {
    return (
      <div className="bg-gray-900 border border-gray-700 rounded-lg px-4 py-3 text-sm">
        <span className="text-gray-200 font-medium">Finalized</span>
        <span className="text-gray-400"> by {call.finalized.byName ?? 'a supervisor'} · {fmtDate(call.finalized.at)}</span>
        {call.finalized.reason && <p className="text-gray-300 mt-1">{call.finalized.reason}</p>}
        {call.agentLock?.statusLocked && (
          <p className="text-red-300 text-xs mt-1">
            🔒 {call.agentName ?? 'The agent'} is {call.agentLock.signInLocked ? 'signed out and sign-in locked' : 'status locked'} — unlock from Users or the dashboard's Agent List.
          </p>
        )}
      </div>
    )
  }
  if (!canManage || !call.canFinalize) return null
  const hasOpenScript = call.sessions.some((s) => s.status === 'active')
  if (!open) {
    return (
      <div className="flex items-center justify-between bg-gray-900 border border-gray-800 rounded-lg px-4 py-2.5">
        <span className="text-sm text-gray-400">
          {call.liveCall ? <span className="text-amber-300">Caller still connected. </span> : null}
          {hasOpenScript ? 'A script is still open on this call.' : 'Close this call out with a reason.'}
        </span>
        <button onClick={() => setOpen(true)} className="border border-red-800 text-red-300 hover:bg-red-950/50 rounded-lg px-3 py-1.5 text-sm font-medium">
          Finalize call…
        </button>
      </div>
    )
  }

  const agentName = call.agentName ?? 'the agent'
  const canSubmit = reason.trim().length > 0 && (!call.liveCall || confirmLive) && !busy

  async function submit() {
    setBusy(true)
    setMsg(null)
    try {
      const r = await callReviewApi.finalize(call.id, {
        reason: reason.trim(), agentLock: lock, lockReason: lockReason.trim() || null, confirmLiveCall: confirmLive,
      })
      if (r.hangupError) setMsg(`Finalized, but hanging up failed: ${r.hangupError}`)
      setOpen(false)
      onChanged()
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Finalize failed.')
      onChanged()   // e.g. the caller connected meanwhile — show the live warning
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="bg-gray-900 border border-red-900/70 rounded-xl p-5">
      <div className="flex items-center justify-between mb-3">
        <h2 className="text-white text-sm font-semibold">Finalize call</h2>
        <button className={btnGhost} onClick={() => { setOpen(false); setMsg(null) }}>Cancel</button>
      </div>

      {call.liveCall && (
        <div className="bg-red-950/50 border border-red-800 rounded-lg px-4 py-3 mb-4">
          <p className="text-red-200 text-sm font-medium">
            The caller ({fmtPhone(call.liveCall.callerNumber)}) is still connected{call.liveCall.withAgent ? ` and on the line with ${agentName}` : ''}.
          </p>
          <label className="flex items-center gap-2 text-sm text-red-100 mt-2 cursor-pointer">
            <input type="checkbox" checked={confirmLive} onChange={(e) => setConfirmLive(e.target.checked)} />
            Hang up the call and finalize
          </label>
        </div>
      )}

      <p className="text-gray-400 text-xs mb-3">
        Correct the call's details and dispositions above first — finalizing closes any open script on the agent's
        screen and marks the call complete.
      </p>

      <Field label="Reason (required — recorded on the call)">
        <textarea rows={2} value={reason} onChange={(e) => setReason(e.target.value)} className={inputCls}
          placeholder="e.g. Agent's script closed unexpectedly — order confirmed with the customer by phone" />
      </Field>

      <div className="mt-4">
        <p className="text-gray-500 text-xs mb-1.5">Agent{call.agentName ? ` (${call.agentName})` : ''}</p>
        <div className="space-y-1.5">
          {([
            ['none', 'Leave the agent as they are'],
            ['status', `Lock ${agentName} as Unavailable — can't change status or take calls until unlocked`],
            ['sign_in', `Sign ${agentName} out now and lock their sign-in — until unlocked they're told to contact their supervisor`],
          ] as const).map(([value, label]) => (
            <label key={value} className="flex items-start gap-2 text-sm text-gray-300 cursor-pointer">
              <input type="radio" name="finalize-lock" className="mt-1" checked={lock === value} onChange={() => setLock(value)} />
              <span className={value === 'sign_in' ? 'text-red-200' : undefined}>{label}</span>
            </label>
          ))}
        </div>
        {lock !== 'none' && (
          <div className="mt-2">
            <Field label="Lock note (shown to the agent and on Users — defaults to the reason)">
              <input value={lockReason} onChange={(e) => setLockReason(e.target.value)} className={inputCls} />
            </Field>
          </div>
        )}
      </div>

      <div className="flex items-center gap-3 mt-5">
        <button disabled={!canSubmit} onClick={submit}
          className="bg-red-600 hover:bg-red-500 disabled:opacity-40 text-white rounded-lg px-4 py-2 text-sm font-medium">
          {busy ? 'Finalizing…' : call.liveCall ? 'Hang up & finalize' : 'Finalize call'}
        </button>
        {msg && <span className="text-red-300 text-xs">{msg}</span>}
      </div>
    </section>
  )
}

const WIPE_REASONS: Record<string, string> = {
  flow_completed: 'when the script finished',
  committed: 'at the Commit Point',
  order_submitted: 'when the order was submitted',
  retention_expired: 'by the retention period',
}

/** Whether a card is still on file (never the card itself) — decides if re-authorization is possible. */
function CardOnFileNote({ call }: { call: CallDetail }) {
  const c = call.cardData
  if (c.onFile) {
    return (
      <div className="bg-gray-900 border border-gray-800 rounded-lg px-4 py-2.5 text-sm text-gray-300 flex flex-wrap gap-x-4 gap-y-1">
        <span className="text-emerald-400 font-medium">Card on file</span>
        <span className="text-gray-400">captured {fmtDate(c.storedAt)}</span>
        {c.expiresAt && <span className="text-gray-400">wiped by {fmtDate(c.expiresAt)} if the order isn't submitted</span>}
        <span className="text-gray-500">— re-authorization is available below.</span>
      </div>
    )
  }
  if (!c.wipedAt) return null
  return (
    <div className="bg-gray-900 border border-gray-800 rounded-lg px-4 py-2.5 text-sm text-gray-400">
      Card data wiped {c.wipeReason ? (WIPE_REASONS[c.wipeReason] ?? `(${c.wipeReason})`) : ''} · {fmtDate(c.wipedAt)}
      {c.retention !== 'until_order_submitted' && c.wipeReason !== 'order_submitted' && (
        <span className="text-gray-500"> — to keep cards for post-call re-authorization, set the campaign's card data retention to "when the order is submitted".</span>
      )}
    </div>
  )
}

function ApiCallsPanel({ call, session, canManage, onChanged }: {
  call: CallDetail; session: CallSessionView; canManage: boolean; onChanged: () => void
}) {
  const callId = call.id
  const [confirming, setConfirming] = useState<string | null>(null)
  const [running, setRunning] = useState<string | null>(null)
  const [results, setResults] = useState<Record<string, ApiCallRerunResult | string>>({})
  const [showResponse, setShowResponse] = useState<Record<string, boolean>>({})

  if (session.apiCalls.length === 0) return null

  async function rerun(nodeId: string) {
    setConfirming(null)
    setRunning(nodeId)
    try {
      const r = await callReviewApi.rerunApiCall(callId, session.id, nodeId)
      setResults((prev) => ({ ...prev, [nodeId]: r }))
      onChanged()
    } catch (e) {
      setResults((prev) => ({ ...prev, [nodeId]: e instanceof Error ? e.message : 'Resubmit failed.' }))
    } finally {
      setRunning(null)
    }
  }

  return (
    <Section title={`Payment & API calls — ${session.flowName ?? 'flow'}`} right={
      <span className="text-xs text-gray-500">Session {session.status}{session.isLive ? ' · agent still in script' : ''}</span>
    }>
      {session.isLive && (
        <p className="text-amber-300 text-xs mb-3">
          An agent still has this script open. Changes here apply to their session too.
        </p>
      )}
      <div className="space-y-3">
        {session.apiCalls.map((a) => {
          const isPayment = a.nodeType === 'authorize_payment'
          const posted = !isPayment && a.oncePerCall && a.success === 'true'
          const noCard = isPayment && !call.cardData.onFile
          const r = results[a.nodeId]
          return (
            <div key={a.nodeId} className="border border-gray-800 rounded-lg p-4">
              <div className="flex flex-wrap items-center gap-3">
                <span className="text-white text-sm font-medium">{a.label}</span>
                {isPayment && <span className="text-gray-500 text-xs">payment authorization</span>}
                {a.releasesCardData && <span className="text-gray-500 text-xs" title="Wipes the captured card when it succeeds">order submission</span>}
                {statusBadge(a)}
                {a.statusCode && <span className="text-gray-500 text-xs">HTTP {a.statusCode}</span>}
                <span className="text-gray-600 text-xs">
                  {a.runCount === 0 ? 'never ran on this call' : `ran ${a.runCount}× · last ${fmtDate(a.lastRunAt)}`}
                </span>
                {canManage && (
                  <div className="ml-auto flex items-center gap-2">
                    {confirming === a.nodeId ? (
                      <>
                        <span className="text-gray-300 text-xs">
                          {isPayment
                            ? `Re-authorize ${money(call.cart?.cartTotal)} on the card on file? A different amount voids the current authorization first.`
                            : "Send it again with the call's current data?"}
                        </span>
                        <button className={btnGhost} onClick={() => setConfirming(null)}>Cancel</button>
                        <button className="bg-red-600 hover:bg-red-500 text-white rounded-lg px-3 py-1.5 text-sm font-medium" onClick={() => rerun(a.nodeId)}>
                          {isPayment ? 'Yes, re-authorize' : 'Yes, resubmit'}
                        </button>
                      </>
                    ) : (
                      <button
                        className={btnPrimary}
                        disabled={posted || noCard || running !== null}
                        title={posted ? 'Already succeeded — this node is set to run once per call, so a resubmit would only replay the stored result.'
                          : noCard ? 'No card on file — it has been wiped (see the note above).' : undefined}
                        onClick={() => setConfirming(a.nodeId)}
                      >
                        {running === a.nodeId ? (isPayment ? 'Authorizing…' : 'Submitting…')
                          : posted ? 'Already posted'
                          : isPayment ? (a.runCount === 0 ? 'Authorize' : 'Re-authorize')
                          : a.runCount === 0 ? 'Submit' : 'Resubmit'}
                      </button>
                    )}
                  </div>
                )}
              </div>

              {a.error && a.success !== 'true' && (
                <p className="text-red-300 text-sm mt-2 whitespace-pre-wrap break-words">{a.error}</p>
              )}

              {r && (
                <p className={`text-sm mt-2 ${typeof r !== 'string' && r.success ? 'text-emerald-300' : 'text-red-300'}`}>
                  {typeof r === 'string'
                    ? r
                    : r.nodeType === 'authorize_payment'
                      ? r.success
                        ? r.replayed
                          ? `Already authorized for this amount — nothing sent (${r.response ?? ''}).`
                          : `Authorized — ${r.response ?? ''}.`
                        : `Authorization ${r.transition}: ${r.error ?? ''}`
                    : r.replayed
                      ? 'Already succeeded on this call — the stored result was replayed; nothing was sent.'
                      : r.success
                        ? `Resubmitted — succeeded${r.statusCode ? ` (HTTP ${r.statusCode})` : ''}.`
                        : `Resubmit ${r.transition === 'timeout' ? 'timed out' : 'failed'}${r.statusCode ? ` (HTTP ${r.statusCode})` : ''}: ${r.error ?? ''}`}
                </p>
              )}

              {isPayment && a.response && <p className="text-gray-400 text-xs mt-2">{a.response}</p>}

              {!isPayment && a.response && (
                <div className="mt-2">
                  <button className="text-indigo-400 hover:text-indigo-300 text-xs"
                    onClick={() => setShowResponse((p) => ({ ...p, [a.nodeId]: !p[a.nodeId] }))}>
                    {showResponse[a.nodeId] ? 'Hide response' : 'Show response'}
                  </button>
                  {showResponse[a.nodeId] && (
                    <pre className="mt-2 bg-gray-950 border border-gray-800 rounded p-3 text-xs text-gray-300 overflow-x-auto max-h-64">{prettyJson(a.response)}</pre>
                  )}
                </div>
              )}
            </div>
          )
        })}
      </div>
      <p className="text-gray-600 text-xs mt-3">
        Resubmit runs the flow's own API Call node against the call's current customer details, addresses,
        cart and flow variables — correct those below first. If the cart total changed, re-authorize before
        resubmitting so the order carries the new authorization.
      </p>
    </Section>
  )
}

function prettyJson(s: string) {
  try { return JSON.stringify(JSON.parse(s), null, 2) } catch { return s }
}

// ── Contact ────────────────────────────────────────────────────────────────

function ContactPanel({ call, canManage, onChanged }: { call: CallDetail; canManage: boolean; onChanged: () => void }) {
  const initial = useMemo(() => ({
    firstName: call.contact.firstName ?? '',
    lastName: call.contact.lastName ?? '',
    email: call.contact.email ?? '',
    billingPhone: call.contact.billingPhone ?? '',
    shippingPhone: call.contact.shippingPhone ?? '',
  }), [call])
  const { form, setForm, dirty, conflict, reset, markSaved } = useDraft(initial)
  const [saving, setSaving] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)

  async function save() {
    setSaving(true)
    setMsg(null)
    try {
      const n = (v: string) => (v.trim() === '' ? null : v.trim())
      await callReviewApi.updateContact(call.id, {
        firstName: n(form.firstName), lastName: n(form.lastName), email: n(form.email),
        billingPhone: n(form.billingPhone), shippingPhone: n(form.shippingPhone),
      })
      markSaved()
      onChanged()
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Section title="Customer" right={canManage && dirty && (
      <div className="flex gap-2">
        <button className={btnGhost} onClick={reset}>Undo</button>
        <button className={btnPrimary} disabled={saving} onClick={save}>{saving ? 'Saving…' : 'Save'}</button>
      </div>
    )}>
      <div className="grid grid-cols-2 md:grid-cols-5 gap-3">
        {([
          ['firstName', 'First name'], ['lastName', 'Last name'], ['email', 'Email'],
          ['billingPhone', 'Billing phone'], ['shippingPhone', 'Shipping phone'],
        ] as const).map(([k, label]) => (
          <Field key={k} label={label}>
            <input value={form[k]} disabled={!canManage} onChange={(e) => setForm({ ...form, [k]: e.target.value })} className={inputCls} />
          </Field>
        ))}
      </div>
      <ConflictNote show={conflict} />
      {msg && <p className="text-red-400 text-xs mt-2">{msg}</p>}
    </Section>
  )
}

// ── Addresses ──────────────────────────────────────────────────────────────

const ADDRESS_FIELDS: { key: keyof CallAddress; label: string; span?: number }[] = [
  { key: 'firstName', label: 'First name' },
  { key: 'lastName', label: 'Last name' },
  { key: 'company', label: 'Company', span: 2 },
  { key: 'prefix', label: 'Dir. (N, SW)' },
  { key: 'street', label: 'Street', span: 3 },
  { key: 'unitPrefix', label: 'Unit type (Apt)' },
  { key: 'unit', label: 'Unit #' },
  { key: 'city', label: 'City', span: 2 },
  { key: 'state', label: 'State' },
  { key: 'zip', label: 'ZIP' },
  { key: 'zip4', label: 'ZIP+4' },
  { key: 'country', label: 'Country' },
]

function AddressPanel({ call, role, canManage, onChanged }: {
  call: CallDetail; role: 'billing' | 'shipping'; canManage: boolean; onChanged: () => void
}) {
  const current = (role === 'billing' ? call.addresses?.billing : call.addresses?.shipping) ?? null
  const { form, setForm, dirty, conflict, reset, markSaved } = useDraft<CallAddress>(current ?? {})
  const [saving, setSaving] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)

  async function save() {
    setSaving(true)
    setMsg(null)
    try {
      const totalBefore = call.cart?.cartTotal
      const { cart } = await callReviewApi.updateAddress(call.id, role, form)
      markSaved()
      if (cart && totalBefore != null && Math.abs(cart.cartTotal - totalBefore) >= 0.005)
        setMsg(`Cart re-priced for the new address: ${money(totalBefore)} → ${money(cart.cartTotal)}.`)
      onChanged()
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Section title={role === 'billing' ? 'Billing address' : 'Shipping address'} right={
      <div className="flex items-center gap-2">
        {current?.isVerified && !dirty && <span className="text-emerald-400 text-xs">verified{current.verificationSource ? ` (${current.verificationSource})` : ''}</span>}
        {canManage && dirty && <>
          <button className={btnGhost} onClick={reset}>Undo</button>
          <button className={btnPrimary} disabled={saving} onClick={save}>{saving ? 'Saving…' : 'Save'}</button>
        </>}
      </div>
    }>
      {!current && !canManage ? (
        <p className="text-gray-500 text-sm">{role === 'shipping' ? 'Same as billing (none captured).' : 'None captured.'}</p>
      ) : (
        <>
          {!current && role === 'shipping' && <p className="text-gray-500 text-xs mb-3">None captured — orders ship to the billing address.</p>}
          <div className="grid grid-cols-4 gap-3">
            {ADDRESS_FIELDS.map((f) => (
              <div key={f.key} style={{ gridColumn: `span ${f.span ?? 1}` }}>
                <Field label={f.label}>
                  <input value={(form[f.key] as string | null | undefined) ?? ''} disabled={!canManage}
                    onChange={(e) => setForm({ ...form, [f.key]: e.target.value || null })} className={inputCls} />
                </Field>
              </div>
            ))}
            <label className="col-span-4 flex items-center gap-4 text-xs text-gray-400">
              <span className="flex items-center gap-1.5">
                <input type="checkbox" disabled={!canManage} checked={!!form.isCanada} onChange={(e) => setForm({ ...form, isCanada: e.target.checked })} /> Canada
              </span>
              <span className="flex items-center gap-1.5">
                <input type="checkbox" disabled={!canManage} checked={!!form.isPOBox} onChange={(e) => setForm({ ...form, isPOBox: e.target.checked })} /> PO Box
              </span>
              <span className="flex items-center gap-1.5">
                <input type="checkbox" disabled={!canManage} checked={!!form.isMilitary} onChange={(e) => setForm({ ...form, isMilitary: e.target.checked })} /> Military
              </span>
            </label>
          </div>
          {role === 'shipping' && canManage && <p className="text-gray-600 text-xs mt-2">Changing the shipping address re-calculates tax on the cart.</p>}
        </>
      )}
      <ConflictNote show={conflict} />
      {msg && <p className={`text-xs mt-2 ${msg.startsWith('Cart re-priced') ? 'text-amber-300' : 'text-red-400'}`}>{msg}</p>}
    </Section>
  )
}

// ── Cart ───────────────────────────────────────────────────────────────────

function CartPanel({ call, canManage, onChanged }: { call: CallDetail; canManage: boolean; onChanged: () => void }) {
  const cart = call.cart
  const [qty, setQty] = useState<Record<number, string>>({})
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const [confirmRemove, setConfirmRemove] = useState<number | null>(null)

  // Quantity drafts are per line position — if the cart itself changes (the agent added/removed an
  // item, or a live refresh after our own change), they no longer line up, so drop them. Say so
  // only when unsaved drafts were actually lost to someone else's change.
  const cartSig = JSON.stringify(cart?.items.map((i) => [i.sku, i.quantity]) ?? [])
  const ownChangeRef = useRef(false)
  const prevSigRef = useRef(cartSig)
  useEffect(() => {
    if (prevSigRef.current === cartSig) return
    prevSigRef.current = cartSig
    setQty((prev) => {
      if (Object.keys(prev).length > 0 && !ownChangeRef.current)
        setMsg('The agent changed the cart — your unsaved quantity changes were cleared.')
      return {}
    })
    setConfirmRemove(null)
    ownChangeRef.current = false
  }, [cartSig])

  async function run(fn: () => Promise<unknown>) {
    setBusy(true)
    setMsg(null)
    ownChangeRef.current = true
    try { await fn(); onChanged() } catch (e) { ownChangeRef.current = false; setMsg(e instanceof Error ? e.message : 'Cart update failed.') } finally { setBusy(false) }
  }

  return (
    <Section title="Cart">
      {!cart || cart.items.length === 0 ? (
        <p className="text-gray-500 text-sm">No items.</p>
      ) : (
        <>
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-gray-500 text-xs border-b border-gray-800">
                <th className="py-2 font-medium">SKU</th>
                <th className="py-2 font-medium">Item</th>
                <th className="py-2 font-medium w-28">Qty</th>
                <th className="py-2 font-medium text-right">Price</th>
                <th className="py-2 font-medium text-right">Extended</th>
                {canManage && <th className="py-2" />}
              </tr>
            </thead>
            <tbody>
              {cart.items.map((item, i) => {
                const edited = qty[i] !== undefined && Number(qty[i]) !== item.quantity
                return (
                  <tr key={i} className="border-b border-gray-800/60">
                    <td className="py-2 text-gray-400 font-mono text-xs">{item.sku}</td>
                    <td className="py-2 text-gray-200">{item.description}</td>
                    <td className="py-2">
                      {canManage ? (
                        <div className="flex items-center gap-1">
                          <input type="number" min={1} value={qty[i] ?? String(item.quantity)} disabled={busy}
                            onChange={(e) => setQty({ ...qty, [i]: e.target.value })}
                            className="bg-gray-800 text-white rounded px-2 py-1 text-sm w-16 outline-none focus:ring-2 focus:ring-indigo-500" />
                          {edited && Number(qty[i]) >= 1 && (
                            <button className="text-indigo-400 hover:text-indigo-300 text-xs" disabled={busy}
                              onClick={() => run(() => callReviewApi.updateCartQuantity(call.id, i, Number(qty[i])))}>Apply</button>
                          )}
                        </div>
                      ) : item.quantity}
                    </td>
                    <td className="py-2 text-right text-gray-300">{money(item.fullPrice)}</td>
                    <td className="py-2 text-right text-gray-300">{money(item.extendedPrice)}</td>
                    {canManage && (
                      <td className="py-2 text-right">
                        {confirmRemove === i ? (
                          <span className="text-xs">
                            <button className="text-gray-400 hover:text-white mr-2" onClick={() => setConfirmRemove(null)}>Keep</button>
                            <button className="text-red-400 hover:text-red-300" disabled={busy}
                              onClick={() => run(() => callReviewApi.removeCartItem(call.id, i))}>Remove</button>
                          </span>
                        ) : (
                          <button className="text-gray-500 hover:text-red-400 text-xs" onClick={() => setConfirmRemove(i)}>Remove</button>
                        )}
                      </td>
                    )}
                  </tr>
                )
              })}
            </tbody>
          </table>
          <div className="flex justify-end mt-3">
            <dl className="text-sm grid grid-cols-2 gap-x-6 gap-y-0.5">
              <dt className="text-gray-500">Subtotal</dt><dd className="text-right text-gray-300">{money(cart.cartSubtotal)}</dd>
              <dt className="text-gray-500">Shipping</dt><dd className="text-right text-gray-300">{money(cart.shipping)}</dd>
              <dt className="text-gray-500">Tax</dt><dd className="text-right text-gray-300">{money(cart.salesTax)}</dd>
              {(cart.fees ?? []).map((f) => (
                <Fragment key={f.code}><dt className="text-gray-500">{f.description}</dt><dd className="text-right text-gray-300">{money(f.amount)}</dd></Fragment>
              ))}
              <dt className="text-white font-medium">Total</dt><dd className="text-right text-white font-medium">{money(cart.cartTotal)}</dd>
              {call.authorizedAmount != null && <>
                <dt className="text-gray-500">Authorized</dt><dd className="text-right text-gray-300">{money(call.authorizedAmount)}</dd>
              </>}
            </dl>
          </div>
          {cart.taxStatus && cart.taxStatus !== 'calculated' && (
            <p className="text-amber-300 text-xs mt-2">Tax: {cart.taxStatus}{cart.taxMessage ? ` — ${cart.taxMessage}` : ''}</p>
          )}
        </>
      )}
      {msg && <p className={`text-xs mt-2 ${msg.startsWith('The agent') ? 'text-amber-300' : 'text-red-400'}`}>{msg}</p>}
    </Section>
  )
}

// ── Flow variables ─────────────────────────────────────────────────────────

function VariablesPanel({ callId, session, canManage, onChanged }: {
  callId: string; session: CallSessionView; canManage: boolean; onChanged: () => void
}) {
  // API results are shown (and re-run) in the API calls panel — keep their output variables out of here.
  const outputs = session.apiCalls.map((a) => a.outputVariable).filter((o): o is string => !!o)
  const isOutput = (k: string) => outputs.some((o) => k === o || k.startsWith(`${o}.`))
  const vars = Object.entries(session.flowVars).filter(([k]) => !isOutput(k)).sort(([a], [b]) => a.localeCompare(b))

  // Unsaved edits survive live refreshes. editBase remembers each edited variable's value when the
  // reviewer started changing it, so a change by the agent underneath can be flagged per row.
  const [edits, setEdits] = useState<Record<string, string | null>>({})
  const [editBase, setEditBase] = useState<Record<string, string | undefined>>({})
  const [newKey, setNewKey] = useState('')
  const [newValue, setNewValue] = useState('')
  const [saving, setSaving] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const [showInputs, setShowInputs] = useState(false)

  function setEdit(k: string, v: string | null | undefined) {
    if (!(k in editBase)) setEditBase((b) => ({ ...b, [k]: session.flowVars[k] }))
    setEdits((prev) => {
      const next = { ...prev }
      if (v === undefined) delete next[k]; else next[k] = v
      return next
    })
  }
  function clearEdits() { setEdits({}); setEditBase({}); setNewKey(''); setNewValue('') }
  const conflictKeys = Object.keys(edits).filter((k) => k in editBase && session.flowVars[k] !== editBase[k])

  const changes: Record<string, string | null> = { ...edits }
  if (newKey.trim()) changes[newKey.trim()] = newValue
  const dirty = Object.keys(changes).length > 0

  async function save() {
    setSaving(true)
    setMsg(null)
    try {
      await callReviewApi.updateVariables(callId, session.id, changes)
      clearEdits()
      onChanged()
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  const inputs = Object.entries(session.inputs).sort(([a], [b]) => a.localeCompare(b))

  return (
    <Section title={`Flow variables — ${session.flowName ?? 'flow'}`} right={canManage && dirty && (
      <div className="flex gap-2">
        <button className={btnGhost} onClick={clearEdits}>Undo</button>
        <button className={btnPrimary} disabled={saving} onClick={save}>{saving ? 'Saving…' : 'Save'}</button>
      </div>
    )}>
      {vars.length === 0 && !canManage ? (
        <p className="text-gray-500 text-sm">None.</p>
      ) : (
        <div className="space-y-1.5">
          {vars.map(([k, v]) => {
            const removed = edits[k] === null
            return (
              <div key={k} className="grid grid-cols-[minmax(0,14rem)_1fr_auto] gap-3 items-center">
                <span className={`font-mono text-xs truncate ${removed ? 'text-gray-600 line-through' : conflictKeys.includes(k) ? 'text-amber-300' : 'text-gray-400'}`}
                  title={conflictKeys.includes(k) ? `${k} — the agent changed this to "${v}" since you started editing` : k}>
                  {conflictKeys.includes(k) && '\u26A0 '}{k}
                </span>
                <input value={removed ? '' : (edits[k] ?? v)} disabled={!canManage || removed}
                  onChange={(e) => setEdit(k, e.target.value)} className={inputCls} />
                {canManage ? (
                  <button className="text-gray-500 hover:text-red-400 text-xs w-14 text-right"
                    onClick={() => setEdit(k, removed ? undefined : null)}>{removed ? 'Restore' : 'Remove'}</button>
                ) : <span />}
              </div>
            )
          })}
          {canManage && (
            <div className="grid grid-cols-[minmax(0,14rem)_1fr_auto] gap-3 items-center pt-2">
              <input value={newKey} onChange={(e) => setNewKey(e.target.value)} placeholder="new variable name" className={`${inputCls} font-mono text-xs`} />
              <input value={newValue} onChange={(e) => setNewValue(e.target.value)} placeholder="value" className={inputCls} />
              <span className="w-14" />
            </div>
          )}
        </div>
      )}
      {conflictKeys.length > 0 && (
        <p className="text-amber-300 text-xs mt-2">
          The agent changed {conflictKeys.join(', ')} since you started editing (hover the name to see their value).
          Save keeps your version; Undo shows theirs.
        </p>
      )}
      {msg && <p className="text-red-400 text-xs mt-2">{msg}</p>}
      {inputs.length > 0 && (
        <div className="mt-4">
          <button className="text-indigo-400 hover:text-indigo-300 text-xs" onClick={() => setShowInputs((v) => !v)}>
            {showInputs ? 'Hide' : 'Show'} agent answers ({inputs.length})
          </button>
          {showInputs && (
            <div className="mt-2 grid grid-cols-[minmax(0,14rem)_1fr] gap-x-3 gap-y-1 text-xs">
              {inputs.map(([k, v]) => (
                <Fragment key={k}><span className="font-mono text-gray-500 truncate" title={k}>{k}</span><span className="text-gray-300 break-words">{v}</span></Fragment>
              ))}
            </div>
          )}
        </div>
      )}
    </Section>
  )
}

// ── Custom fields ──────────────────────────────────────────────────────────

/** Editor value for a stored custom field value — datetime inputs need local "YYYY-MM-DDTHH:mm". */
function toEditor(f: CallCustomField): string {
  if (f.value == null) return ''
  if (f.dataTypeName === 'datetime') {
    const d = new Date(f.value)
    if (isNaN(d.getTime())) return f.value
    const pad = (n: number) => String(n).padStart(2, '0')
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
  }
  return f.value
}

function fromEditor(f: CallCustomField, v: string): string | null {
  if (v.trim() === '') return null
  if (f.dataTypeName === 'datetime') {
    const d = new Date(v)
    return isNaN(d.getTime()) ? v : d.toISOString()
  }
  return v
}

function CustomFieldsPanel({ call, canManage, onChanged }: { call: CallDetail; canManage: boolean; onChanged: () => void }) {
  const initial = useMemo(
    () => Object.fromEntries(call.customFields.map((f) => [f.definitionId, toEditor(f)])) as Record<string, string>,
    [call])
  const { form, setForm, base, conflict, reset, markSaved } = useDraft<Record<string, string>>(initial)
  const [saving, setSaving] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)

  const changed = call.customFields.filter((f) => (form[f.definitionId] ?? '') !== (base[f.definitionId] ?? ''))

  async function save() {
    setSaving(true)
    setMsg(null)
    try {
      for (const f of changed)
        await callReviewApi.updateCustomField(call.id, f.definitionId, fromEditor(f, form[f.definitionId] ?? ''))
      markSaved()
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
      onChanged()
    }
  }

  function editor(f: CallCustomField) {
    const value = form[f.definitionId] ?? ''
    const set = (v: string) => setForm({ ...form, [f.definitionId]: v })
    const disabled = !canManage || !f.inScope
    switch (f.dataTypeName) {
      case 'boolean':
        return (
          <select value={value} disabled={disabled} onChange={(e) => set(e.target.value)} className={inputCls}>
            <option value="">—</option><option value="true">Yes</option><option value="false">No</option>
          </select>
        )
      case 'integer':
      case 'decimal':
      case 'currency':
        return <input type="number" step={f.dataTypeName === 'integer' ? 1 : 'any'} value={value} disabled={disabled} onChange={(e) => set(e.target.value)} className={inputCls} />
      case 'date':
        return <input type="date" value={value} disabled={disabled} onChange={(e) => set(e.target.value)} className={inputCls} />
      case 'datetime':
        return <input type="datetime-local" value={value} disabled={disabled} onChange={(e) => set(e.target.value)} className={inputCls} />
      case 'json':
        return <textarea rows={2} value={value} disabled={disabled} onChange={(e) => set(e.target.value)} className={`${inputCls} font-mono text-xs`} />
      default:
        return <input value={value} disabled={disabled} onChange={(e) => set(e.target.value)} className={inputCls} />
    }
  }

  return (
    <Section title="Custom fields" right={canManage && changed.length > 0 && (
      <div className="flex gap-2">
        <button className={btnGhost} onClick={reset}>Undo</button>
        <button className={btnPrimary} disabled={saving} onClick={save}>
          {saving ? 'Saving…' : changed.length === 1 ? 'Save' : `Save (${changed.length})`}
        </button>
      </div>
    )}>
      {call.customFields.length === 0 ? (
        <p className="text-gray-500 text-sm">No custom fields apply to this call's campaign.</p>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-x-6 gap-y-3">
          {call.customFields.map((f) => (
            <div key={f.definitionId} className="grid grid-cols-[minmax(0,12rem)_1fr] gap-3 items-start">
              <div className="pt-1.5">
                <p className="text-gray-300 text-sm truncate" title={f.displayLabel}>
                  {f.displayLabel}{f.isRequired && <span className="text-red-400"> *</span>}
                  {!f.isActive && <span className="text-gray-600 text-xs"> (inactive)</span>}
                  {f.isActive && !f.inScope && <span className="text-gray-600 text-xs"> (not in this campaign)</span>}
                </p>
                <p className="text-gray-600 text-xs font-mono truncate" title={`${f.fieldName} · ${f.dataTypeName} · ${f.scope}`}>{f.fieldName}</p>
              </div>
              <div>
                {editor(f)}
                <p className="text-gray-600 text-xs mt-0.5">{f.storedAt ? `stored ${fmtDate(f.storedAt)}` : 'not set'}</p>
              </div>
            </div>
          ))}
        </div>
      )}
      <ConflictNote show={conflict} />
      {msg && <p className="text-red-400 text-xs mt-2">{msg}</p>}
    </Section>
  )
}

// ── Read-only panels ───────────────────────────────────────────────────────

function PaymentsPanel({ call }: { call: CallDetail }) {
  if (call.payments.length === 0) return null
  return (
    <Section title="Payments">
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left text-gray-500 text-xs border-b border-gray-800">
            <th className="py-2 font-medium">When</th><th className="py-2 font-medium">Type</th><th className="py-2 font-medium">Status</th>
            <th className="py-2 font-medium text-right">Amount</th><th className="py-2 font-medium">Card</th>
            <th className="py-2 font-medium">Transaction</th><th className="py-2 font-medium">Message</th>
          </tr>
        </thead>
        <tbody>
          {call.payments.map((p) => (
            <tr key={p.id} className="border-b border-gray-800/60">
              <td className="py-2 text-gray-400 text-xs whitespace-nowrap">{fmtDate(p.createdAt)}</td>
              <td className="py-2 text-gray-300">{p.gateway} · {p.transactionType}</td>
              <td className="py-2 text-gray-300">{p.voidedAt ? 'voided' : p.status}</td>
              <td className="py-2 text-right text-gray-300">{money(p.amount)}</td>
              <td className="py-2 text-gray-300">{p.cardType ?? ''} {p.cardLast4 ? `…${p.cardLast4}` : ''}</td>
              <td className="py-2 text-gray-400 font-mono text-xs">{p.gatewayTransactionId ?? '—'}</td>
              <td className="py-2 text-gray-400 text-xs">{p.responseReasonText ?? ''}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Section>
  )
}

function OtherPanel({ call }: { call: CallDetail }) {
  if (call.dispositions.length === 0 && call.commitmentEvents.length === 0) return null
  return (
    <Section title="Interactions & commitments">
      <div className="grid grid-cols-1 md:grid-cols-2 gap-5 text-sm">
        <div className="space-y-2">
          {call.dispositions.map((d) => (
            <div key={d.interactionNumber} className="flex gap-3">
              <span className="text-gray-500 text-xs w-6 shrink-0 pt-0.5">#{d.interactionNumber}</span>
              <div className="min-w-0">
                <div className="text-gray-200">{d.disposition ?? <span className="text-gray-500">No disposition</span>}</div>
                <div className="text-gray-500 text-xs">
                  {[d.campaignName, d.agentName, fmtDate(d.startedAt), d.status].filter(Boolean).join(' · ')}
                </div>
              </div>
            </div>
          ))}
        </div>
        <div className="space-y-1">
          {call.commitmentEvents.map((e, i) => (
            <div key={i} className="text-gray-400 text-xs">
              <span className="text-gray-200">{String(e.eventName ?? 'commitment')}</span>{e.timestamp ? ` · ${fmtDate(String(e.timestamp))}` : ''}
            </div>
          ))}
        </div>
      </div>
    </Section>
  )
}

function AuditPanel({ call }: { call: CallDetail }) {
  return (
    <Section title="Change history">
      {call.audit.length === 0 ? (
        <p className="text-gray-500 text-sm">No changes since the call.</p>
      ) : (
        <ul className="space-y-1.5 text-sm">
          {call.audit.map((a) => (
            <li key={a.id} className="flex gap-3">
              <span className="text-gray-500 text-xs whitespace-nowrap w-40 shrink-0">{fmtDate(a.createdAt)}</span>
              <span className="text-gray-400 text-xs w-32 shrink-0 truncate">{a.actorName}</span>
              <span className="text-gray-200">{a.summary}</span>
            </li>
          ))}
        </ul>
      )}
    </Section>
  )
}
