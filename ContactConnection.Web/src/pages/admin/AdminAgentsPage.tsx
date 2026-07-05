import { useEffect, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import { listAdminAgents, resetAgentPassword, updateAgent, inviteUsers, type AgentRecord, type InviteResult } from '../../api/adminAgents'
import { rolesApi, type Role } from '../../api/roles'
import { TIMEZONE_GROUPS } from '../../utils/timezones'

export default function AdminAgentsPage() {
  const [agents, setAgents] = useState<AgentRecord[]>([])
  const [roles, setRoles] = useState<Role[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Per-row reset password state
  const [resetId, setResetId] = useState<string | null>(null)
  const [resetPw, setResetPw] = useState('')
  const [resetError, setResetError] = useState<string | null>(null)
  const [resetSaving, setResetSaving] = useState(false)

  // Per-row update (role/status) state
  const [updatingId, setUpdatingId] = useState<string | null>(null)

  // Invite state
  const [showInvite, setShowInvite] = useState(false)
  const [inviteEmails, setInviteEmails] = useState('')
  const [inviteRoleId, setInviteRoleId] = useState('')
  const [inviteResult, setInviteResult] = useState<InviteResult | null>(null)
  const [inviteSending, setInviteSending] = useState(false)

  useEffect(() => {
    Promise.all([listAdminAgents(), rolesApi.getAll()])
      .then(([agentList, roleList]) => {
        setAgents(agentList)
        setRoles(roleList)
        // Default to first role
        if (roleList.length > 0) setInviteRoleId(roleList[0].id)
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  function openReset(id: string) {
    setResetId(id)
    setResetPw('')
    setResetError(null)
  }

  function cancelReset() {
    setResetId(null)
    setResetPw('')
    setResetError(null)
  }

  async function handleReset(id: string) {
    if (resetPw.length < 8) { setResetError('Password must be at least 8 characters.'); return }
    setResetSaving(true)
    setResetError(null)
    try {
      const updated = await resetAgentPassword(id, resetPw)
      setAgents((prev) => prev.map((a) => a.id === id ? updated : a))
      setResetId(null)
      setResetPw('')
    } catch (e) {
      setResetError(e instanceof Error ? e.message : 'Reset failed.')
    } finally {
      setResetSaving(false)
    }
  }

  async function handleInvite() {
    if (!inviteEmails.trim() || !inviteRoleId) return
    setInviteSending(true)
    setInviteResult(null)
    try {
      const result = await inviteUsers(inviteEmails.trim(), inviteRoleId)
      setInviteResult(result)
      if (result.failed.length === 0) {
        setInviteEmails('')
        setShowInvite(false)
      }
    } catch (e) {
      setInviteResult({ sent: 0, failed: [], message: e instanceof Error ? e.message : 'Failed to send invitations.' })
    } finally {
      setInviteSending(false)
    }
  }

  function closeInvite() {
    setShowInvite(false)
    setInviteEmails('')
    setInviteResult(null)
  }

  async function handleToggleActive(agent: AgentRecord) {
    setUpdatingId(agent.id)
    try {
      const updated = await updateAgent(agent.id, { isActive: !agent.isActive })
      setAgents((prev) => prev.map((a) => a.id === agent.id ? updated : a))
    } catch {
      // swallow — no global error UI for quick toggles
    } finally {
      setUpdatingId(null)
    }
  }

  async function handleRoleChange(agent: AgentRecord, roleId: string) {
    setUpdatingId(agent.id)
    try {
      const updated = await updateAgent(agent.id, { roleId: roleId || null })
      setAgents((prev) => prev.map((a) => a.id === agent.id ? updated : a))
    } catch {
      // swallow
    } finally {
      setUpdatingId(null)
    }
  }

  async function handleTimezoneChange(agent: AgentRecord, timezone: string) {
    setUpdatingId(agent.id)
    try {
      const updated = await updateAgent(agent.id, { timezone: timezone || null })
      setAgents((prev) => prev.map((a) => a.id === agent.id ? updated : a))
    } catch {
      // swallow
    } finally {
      setUpdatingId(null)
    }
  }

  return (
    <AdminShell>
      <div className="p-6 max-w-5xl">
        <div className="flex items-start justify-between mb-6">
          <div>
            <h1 className="text-white text-xl font-semibold">Users</h1>
            <p className="text-gray-500 text-sm mt-0.5">All users in your workspace.</p>
          </div>
          <button
            onClick={() => { setShowInvite((v) => !v); setInviteResult(null) }}
            className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
          >
            Invite Users
          </button>
        </div>

        {/* Invite users form */}
        {showInvite && (
          <div className="bg-gray-900 border border-gray-800 rounded-xl p-5 mb-6">
            <p className="text-gray-200 text-sm font-medium mb-1">Invite Users</p>
            <p className="text-gray-500 text-xs mb-4">Enter one or more email addresses separated by commas or new lines.</p>
            <div className="flex gap-4">
              <div className="flex-1">
                <textarea
                  autoFocus
                  value={inviteEmails}
                  onChange={(e) => { setInviteEmails(e.target.value); setInviteResult(null) }}
                  placeholder={"alice@example.com\nbob@example.com, carol@example.com"}
                  rows={4}
                  className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 resize-none"
                />
              </div>
              <div className="flex flex-col gap-3 w-44 flex-shrink-0">
                <div>
                  <label className="block text-xs text-gray-400 mb-1">Role</label>
                  <select
                    value={inviteRoleId}
                    onChange={e => setInviteRoleId(e.target.value)}
                    className="w-full bg-gray-800 border border-gray-600 text-gray-200 text-sm rounded-lg px-2 py-2"
                  >
                    {roles.map(r => (
                      <option key={r.id} value={r.id}>{r.name}</option>
                    ))}
                  </select>
                </div>
                <div className="flex flex-col gap-2 mt-auto">
                  <button
                    onClick={handleInvite}
                    disabled={inviteSending || !inviteEmails.trim() || !inviteRoleId}
                    className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors w-full"
                  >
                    {inviteSending ? 'Sending…' : 'Send invites'}
                  </button>
                  <button
                    onClick={closeInvite}
                    className="text-gray-500 hover:text-white text-sm transition-colors text-center"
                  >
                    Cancel
                  </button>
                </div>
              </div>
            </div>
            {inviteResult && (
              <div className={`mt-3 text-xs ${inviteResult.failed.length > 0 ? 'text-amber-400' : 'text-emerald-400'}`}>
                <p>{inviteResult.message}</p>
                {inviteResult.failed.length > 0 && (
                  <p className="mt-1 text-red-400">Failed: {inviteResult.failed.join(', ')}</p>
                )}
              </div>
            )}
          </div>
        )}

        {/* Success flash when invite panel is closed */}
        {!showInvite && inviteResult?.sent != null && inviteResult.sent > 0 && (
          <p className="text-emerald-400 text-sm mb-4">{inviteResult.message}</p>
        )}

        {loading && <p className="text-gray-400 text-sm">Loading…</p>}
        {error && <p className="text-red-400 text-sm">{error}</p>}

        {!loading && !error && agents.length === 0 && (
          <p className="text-gray-500 text-sm">No agents found.</p>
        )}

        {agents.length > 0 && (
          <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 text-gray-400 text-left">
                  <th className="px-4 py-3 font-medium">Name</th>
                  <th className="px-4 py-3 font-medium">Email</th>
                  <th className="px-4 py-3 font-medium">Role</th>
                  <th className="px-4 py-3 font-medium">Timezone</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="px-4 py-3 font-medium">Last login</th>
                  <th className="px-4 py-3 font-medium"></th>
                </tr>
              </thead>
              <tbody>
                {agents.map((agent) => (
                  <>
                    <tr
                      key={agent.id}
                      className="border-b border-gray-800 last:border-0 hover:bg-gray-800/40 transition-colors"
                    >
                      <td className="px-4 py-3 text-white font-medium">
                        {agent.firstName} {agent.lastName}
                      </td>
                      <td className="px-4 py-3 text-gray-300">{agent.email}</td>
                      <td className="px-4 py-3">
                        <select
                          value={agent.roleId ?? ''}
                          onChange={e => handleRoleChange(agent, e.target.value)}
                          disabled={updatingId === agent.id}
                          className="bg-gray-800 border border-gray-600 text-gray-200 text-xs rounded px-2 py-1 disabled:opacity-50"
                        >
                          <option value="">{agent.roleName ?? agent.role} (legacy)</option>
                          {roles.map(r => (
                            <option key={r.id} value={r.id}>{r.name}</option>
                          ))}
                        </select>
                      </td>
                      <td className="px-4 py-3">
                        <select
                          value={agent.timezone ?? ''}
                          onChange={e => handleTimezoneChange(agent, e.target.value)}
                          disabled={updatingId === agent.id}
                          className="bg-gray-800 border border-gray-600 text-gray-200 text-xs rounded px-2 py-1 disabled:opacity-50"
                        >
                          <option value="">Tenant default</option>
                          {TIMEZONE_GROUPS.map(grp => (
                            <optgroup key={grp.group} label={grp.group}>
                              {grp.options.map(tz => (
                                <option key={tz.value} value={tz.value}>{tz.label}</option>
                              ))}
                            </optgroup>
                          ))}
                        </select>
                      </td>
                      <td className="px-4 py-3">
                        <button
                          onClick={() => handleToggleActive(agent)}
                          disabled={updatingId === agent.id}
                          className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium transition-colors disabled:opacity-50 ${agent.isActive
                              ? 'bg-emerald-900/50 text-emerald-400 hover:bg-emerald-900/80'
                              : 'bg-red-900/50 text-red-400 hover:bg-red-900/80'
                            }`}
                        >
                          {agent.isActive ? 'Active' : 'Inactive'}
                        </button>
                      </td>
                      <td className="px-4 py-3 text-gray-400">
                        {agent.lastLoginAt
                          ? new Date(agent.lastLoginAt).toLocaleDateString()
                          : <span className="text-gray-600">Never</span>}
                      </td>
                      <td className="px-4 py-3 text-right">
                        {resetId === agent.id ? (
                          <button
                            onClick={cancelReset}
                            className="text-gray-500 hover:text-white text-xs transition-colors"
                          >
                            Cancel
                          </button>
                        ) : (
                          <button
                            onClick={() => openReset(agent.id)}
                            className="text-indigo-400 hover:text-indigo-300 text-xs font-medium transition-colors"
                          >
                            Reset password
                          </button>
                        )}
                      </td>
                    </tr>

                    {/* Inline reset password row */}
                    {resetId === agent.id && (
                      <tr key={`${agent.id}-reset`} className="border-b border-gray-800 bg-gray-800/30">
                        <td colSpan={7} className="px-4 py-3">
                          <div className="flex items-center gap-3">
                            <input
                              type="password"
                              value={resetPw}
                              onChange={(e) => { setResetPw(e.target.value); setResetError(null) }}
                              placeholder="New password (min 8 characters)"
                              autoFocus
                              className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-72"
                            />
                            <button
                              onClick={() => handleReset(agent.id)}
                              disabled={resetSaving}
                              className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-1.5 text-sm font-medium transition-colors"
                            >
                              {resetSaving ? 'Saving…' : 'Save password'}
                            </button>
                            {resetError && (
                              <span className="text-red-400 text-xs">{resetError}</span>
                            )}
                          </div>
                        </td>
                      </tr>
                    )}
                  </>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </AdminShell>
  )
}
