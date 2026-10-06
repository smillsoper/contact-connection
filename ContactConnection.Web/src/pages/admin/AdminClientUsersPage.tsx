import { Fragment, useEffect, useMemo, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import { clientUsersApi, type ClientUser, type ClientUserAudit, type ClientUserInput } from '../../api/clientUsers'
import { dashboardsApi, type DashboardSummary } from '../../api/dashboards'
import { listClients } from '../../api/telephony'

// Client users (S181, docs/client-dashboards-plan.md §B). People outside the tenant — a client, or a vendor such as a media
// agency — who sign in to the client portal to view the client dashboards assigned to them. Separate from agents
// entirely; recording access is decided per person here.

const input = 'w-full bg-gray-900 border border-gray-700 rounded px-2 py-1.5 text-sm text-gray-100 focus:outline-none focus:border-indigo-500'
const label = 'block text-xs text-gray-400 mb-1'
const btn = 'px-3 py-1.5 rounded text-sm disabled:opacity-50'

const ACTION_LABEL: Record<string, string> = {
  invited: 'Link sent', invite_accepted: 'Set password', sign_in: 'Signed in', sign_in_failed: 'Sign-in failed',
  updated: 'Changed', mfa_reset: 'Two-step reset', mfa_enabled: 'Two-step set up', mfa_disabled: 'Two-step turned off', dashboard_viewed: 'Opened dashboard',
  recording_played: 'Played recording', exported: 'Exported',
}

const fmt = (iso: string | null) => (iso ? new Date(iso).toLocaleString() : '—')

function UserForm({ value, onChange, dashboards, clientNames, isNew }: {
  value: ClientUserInput; onChange: (v: ClientUserInput) => void; dashboards: DashboardSummary[]
  clientNames: Record<string, string>; isNew: boolean
}) {
  const set = (p: Partial<ClientUserInput>) => onChange({ ...value, ...p })
  return (
    <div className="space-y-3">
      <div className="grid sm:grid-cols-3 gap-3">
        {isNew && (
          <div>
            <label className={label}>Email</label>
            <input className={input} type="email" value={value.email ?? ''} onChange={(e) => set({ email: e.target.value })} />
          </div>
        )}
        <div>
          <label className={label}>First name</label>
          <input className={input} value={value.firstName} onChange={(e) => set({ firstName: e.target.value })} />
        </div>
        <div>
          <label className={label}>Last name</label>
          <input className={input} value={value.lastName} onChange={(e) => set({ lastName: e.target.value })} />
        </div>
      </div>
      <label className="flex items-start gap-2 text-sm text-gray-200">
        <input type="checkbox" className="mt-0.5" checked={value.canPlayRecordings} onChange={(e) => set({ canPlayRecordings: e.target.checked })} />
        <span>
          Can play call recordings
          <span className="block text-xs text-gray-500">Leave off for vendors who only need the numbers (e.g. a media agency).</span>
        </span>
      </label>
      {!isNew && (
        <label className="flex items-center gap-2 text-sm text-gray-200">
          <input type="checkbox" checked={value.isActive ?? true} onChange={(e) => set({ isActive: e.target.checked })} />
          Active (unticking signs them out at once)
        </label>
      )}
      <div>
        <label className={label}>Dashboards they can open</label>
        {dashboards.length === 0 ? (
          <p className="text-xs text-gray-500">
            No client dashboards yet — open a dashboard in the Dashboard Builder, choose <b>Make client dashboard…</b> and pick the client.
          </p>
        ) : (
          <div className="border border-gray-700 rounded p-2 space-y-1 max-h-48 overflow-y-auto">
            {dashboards.map((d) => (
              <label key={d.id} className="flex items-center gap-2 text-xs text-gray-300">
                <input type="checkbox" checked={value.dashboardIds.includes(d.id)}
                  onChange={(e) => set({ dashboardIds: e.target.checked ? [...value.dashboardIds, d.id] : value.dashboardIds.filter((x) => x !== d.id) })} />
                {d.name} <span className="text-gray-500">· {clientNames[d.scope_client_id ?? ''] ?? 'client'}</span>
              </label>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

export default function AdminClientUsersPage() {
  const [users, setUsers] = useState<ClientUser[]>([])
  const [dashboards, setDashboards] = useState<DashboardSummary[]>([])
  const [clientNames, setClientNames] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [newUser, setNewUser] = useState<ClientUserInput | null>(null)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [edit, setEdit] = useState<ClientUserInput | null>(null)
  const [audit, setAudit] = useState<ClientUserAudit[] | null>(null)
  const [auditFor, setAuditFor] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null)

  const load = () => clientUsersApi.list().then(setUsers).catch((e) => setError(e.message))
  useEffect(() => {
    load()
    dashboardsApi.list().then((d) => setDashboards(d.filter((x) => x.is_client_dashboard))).catch(() => {})
    listClients().then((c) => setClientNames(Object.fromEntries(c.map((x) => [x.id, x.name])))).catch(() => {})
  }, [])

  const dashName = useMemo(() => Object.fromEntries(dashboards.map((d) => [d.id, d.name])), [dashboards])

  async function run(fn: () => Promise<unknown>, ok?: string) {
    setBusy(true); setError(null); setNotice(null)
    try { await fn(); if (ok) setNotice(ok); await load() } catch (e) { setError(e instanceof Error ? e.message : 'Failed') } finally { setBusy(false) }
  }

  const showAudit = (id: string | null) => {
    setAuditFor(id)
    clientUsersApi.audit(id ?? undefined).then(setAudit).catch((e) => setError(e.message))
  }

  return (
    <AdminShell>
      <div className="max-w-6xl mx-auto px-4 sm:px-6 py-6">
        <div className="flex items-start justify-between gap-4 mb-4 flex-wrap">
          <div>
            <h1 className="text-xl font-semibold text-white mb-1">Client users</h1>
            <p className="text-sm text-gray-400 max-w-3xl">
              People outside your team — a client, or a vendor such as a media agency — who sign in to the client portal to see the
              client dashboards you give them. They are not agents and can't reach anything else. Recording access is off unless you
              allow it.
            </p>
          </div>
          <div className="flex gap-2">
            <button className={`${btn} border border-gray-700 text-gray-300 hover:border-gray-500`} onClick={() => showAudit(null)}>Activity</button>
            <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white`}
              onClick={() => setNewUser({ email: '', firstName: '', lastName: '', canPlayRecordings: false, dashboardIds: [] })}>
              + Invite client user
            </button>
          </div>
        </div>

        {error && <div className="mb-3 text-sm text-red-400">{error}</div>}
        {notice && <div className="mb-3 text-sm text-emerald-400">{notice}</div>}

        {newUser && (
          <div className="bg-gray-800/60 border border-gray-700 rounded-lg p-4 mb-4">
            <h2 className="text-sm font-semibold text-white mb-3">Invite a client user</h2>
            <UserForm value={newUser} onChange={setNewUser} dashboards={dashboards} clientNames={clientNames} isNew />
            <div className="flex justify-end gap-2 mt-4">
              <button className={`${btn} border border-gray-700 text-gray-300`} onClick={() => setNewUser(null)}>Cancel</button>
              <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white`} disabled={busy || !newUser.email || !newUser.firstName}
                onClick={() => run(async () => {
                  const r = await clientUsersApi.invite(newUser)
                  setNewUser(null)
                  if (!r.emailSent) throw new Error('Account created, but the email failed to send — use "Send link" to retry.')
                }, `Invitation sent to ${newUser.email}.`)}>
                Send invitation
              </button>
            </div>
          </div>
        )}

        <div className="border border-gray-800 rounded-lg overflow-x-auto">
          <table className="w-full text-sm min-w-[48rem]">
            <thead className="text-xs text-gray-500 uppercase tracking-wide border-b border-gray-800">
              <tr>
                <th className="text-left px-3 py-2">Name</th>
                <th className="text-left px-3 py-2">Dashboards</th>
                <th className="text-left px-3 py-2">Recordings</th>
                <th className="text-left px-3 py-2">Status</th>
                <th className="text-left px-3 py-2">Last sign-in</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody>
              {users.length === 0 && (
                <tr><td colSpan={6} className="px-3 py-6 text-center text-gray-500">No client users yet.</td></tr>
              )}
              {users.map((u) => (
                <Fragment key={u.id}>
                  <tr className="border-b border-gray-800/60 align-top">
                    <td className="px-3 py-2">
                      <div className="text-gray-100">{u.firstName} {u.lastName}</div>
                      <div className="text-xs text-gray-500">{u.email}</div>
                    </td>
                    <td className="px-3 py-2 text-xs text-gray-300">
                      {u.dashboardIds.length === 0 ? <span className="text-gray-600">None</span> : u.dashboardIds.map((d) => dashName[d] ?? 'Not a client dashboard').join(', ')}
                    </td>
                    <td className="px-3 py-2 text-xs">{u.canPlayRecordings ? <span className="text-amber-300">Allowed</span> : <span className="text-gray-500">No</span>}</td>
                    <td className="px-3 py-2 text-xs">
                      {!u.isActive ? <span className="text-red-400">Deactivated</span>
                        : !u.hasPassword ? <span className="text-sky-300">Invited{u.linkPending ? '' : ' — link expired'}</span>
                        : <span className="text-emerald-400">Active</span>}
                      {u.mfaEnabled && <span className="block text-gray-500">Two-step on</span>}
                    </td>
                    <td className="px-3 py-2 text-xs text-gray-400">{fmt(u.lastLoginAt)}</td>
                    <td className="px-3 py-2 text-right whitespace-nowrap">
                      <button className="text-xs text-indigo-300 hover:text-indigo-200 mr-3" onClick={() => {
                        setEditingId(u.id)
                        setEdit({ firstName: u.firstName, lastName: u.lastName, isActive: u.isActive, canPlayRecordings: u.canPlayRecordings, dashboardIds: u.dashboardIds })
                      }}>Edit</button>
                      <button className="text-xs text-indigo-300 hover:text-indigo-200 mr-3" disabled={busy || !u.isActive}
                        onClick={() => run(async () => {
                          const r = await clientUsersApi.sendLink(u.id)
                          if (!r.emailSent) throw new Error('The email failed to send — try again shortly.')
                        }, u.hasPassword ? `Password link sent to ${u.email}.` : `Invitation re-sent to ${u.email}.`)}>
                        {u.hasPassword ? 'Send password link' : 'Resend invite'}
                      </button>
                      {u.mfaEnabled && (
                        <button className="text-xs text-indigo-300 hover:text-indigo-200 mr-3" disabled={busy}
                          title="Lost phone or authenticator? They'll set two-step up again at their next sign-in (or can leave it off unless required)."
                          onClick={() => run(() => clientUsersApi.resetMfa(u.id), `Two-step sign-in reset for ${u.email}.`)}>
                          Reset two-step
                        </button>
                      )}
                      <button className="text-xs text-indigo-300 hover:text-indigo-200" onClick={() => showAudit(u.id)}>Activity</button>
                    </td>
                  </tr>
                  {editingId === u.id && edit && (
                    <tr className="border-b border-gray-800/60 bg-gray-900/60">
                      <td colSpan={6} className="px-3 py-4">
                        <UserForm value={edit} onChange={setEdit} dashboards={dashboards} clientNames={clientNames} isNew={false} />
                        <div className="flex justify-between gap-2 mt-4 flex-wrap">
                          <div className="flex gap-2">
                            {confirmDelete === u.id ? (
                              <>
                                <span className="text-xs text-red-300 self-center">Delete this account? The activity log is kept.</span>
                                <button className={`${btn} bg-red-600 hover:bg-red-500 text-white`} disabled={busy}
                                  onClick={() => run(async () => { await clientUsersApi.remove(u.id); setEditingId(null); setConfirmDelete(null) }, 'Client user deleted.')}>
                                  Delete
                                </button>
                                <button className={`${btn} border border-gray-700 text-gray-300`} onClick={() => setConfirmDelete(null)}>Keep</button>
                              </>
                            ) : (
                              <button className={`${btn} border border-red-900 text-red-300`} onClick={() => setConfirmDelete(u.id)}>Delete…</button>
                            )}
                          </div>
                          <div className="flex gap-2">
                            <button className={`${btn} border border-gray-700 text-gray-300`} onClick={() => { setEditingId(null); setConfirmDelete(null) }}>Cancel</button>
                            <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white`} disabled={busy}
                              onClick={() => run(async () => { await clientUsersApi.update(u.id, edit); setEditingId(null) }, 'Saved.')}>
                              Save
                            </button>
                          </div>
                        </div>
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>

        {audit && (
          <div className="mt-6">
            <div className="flex items-center justify-between mb-2">
              <h2 className="text-sm font-semibold text-white">
                Activity{auditFor ? ` — ${users.find((u) => u.id === auditFor)?.email ?? ''}` : ' — all client users'}
              </h2>
              <button className="text-xs text-gray-400 hover:text-gray-200" onClick={() => setAudit(null)}>Close</button>
            </div>
            <div className="border border-gray-800 rounded-lg overflow-x-auto">
              <table className="w-full text-xs min-w-[40rem]">
                <thead className="text-gray-500 uppercase tracking-wide border-b border-gray-800">
                  <tr>
                    <th className="text-left px-3 py-2">When</th>
                    {!auditFor && <th className="text-left px-3 py-2">Client user</th>}
                    <th className="text-left px-3 py-2">What</th>
                    <th className="text-left px-3 py-2">Detail</th>
                    <th className="text-left px-3 py-2">By</th>
                    <th className="text-left px-3 py-2">IP</th>
                  </tr>
                </thead>
                <tbody>
                  {audit.length === 0 && <tr><td colSpan={6} className="px-3 py-4 text-center text-gray-500">Nothing yet.</td></tr>}
                  {audit.map((a) => (
                    <tr key={a.id} className="border-b border-gray-800/60">
                      <td className="px-3 py-1.5 text-gray-400 whitespace-nowrap">{fmt(a.at)}</td>
                      {!auditFor && <td className="px-3 py-1.5 text-gray-300">{a.clientUser ?? '(deleted)'}</td>}
                      <td className={`px-3 py-1.5 ${a.action === 'sign_in_failed' ? 'text-red-300' : 'text-gray-200'}`}>{ACTION_LABEL[a.action] ?? a.action}</td>
                      <td className="px-3 py-1.5 text-gray-400">{a.detail ?? ''}</td>
                      <td className="px-3 py-1.5 text-gray-400">{a.byAgent ?? (a.clientUserId ? 'Themselves' : '')}</td>
                      <td className="px-3 py-1.5 text-gray-500">{a.ipAddress ?? ''}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>
    </AdminShell>
  )
}
