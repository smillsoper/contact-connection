import { useEffect, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import { listAdminAgents, resetAgentPassword, updateAgent, type AgentRecord } from '../../api/adminAgents'

const ROLE_STYLES: Record<string, string> = {
  admin:      'bg-indigo-900/50 text-indigo-300',
  supervisor: 'bg-sky-900/50 text-sky-300',
  agent:      'bg-gray-700/60 text-gray-300',
}

export default function AdminAgentsPage() {
  const [agents, setAgents] = useState<AgentRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Per-row reset password state
  const [resetId, setResetId] = useState<string | null>(null)
  const [resetPw, setResetPw] = useState('')
  const [resetError, setResetError] = useState<string | null>(null)
  const [resetSaving, setResetSaving] = useState(false)

  // Per-row update (role/status) state
  const [updatingId, setUpdatingId] = useState<string | null>(null)

  useEffect(() => {
    listAdminAgents()
      .then(setAgents)
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

  return (
    <AdminShell>
      <div className="p-6 max-w-5xl">
        <div className="mb-6">
          <h1 className="text-white text-xl font-semibold">Agents</h1>
          <p className="text-gray-500 text-sm mt-0.5">All users in your workspace.</p>
        </div>

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
                        <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${ROLE_STYLES[agent.role] ?? ROLE_STYLES.agent}`}>
                          {agent.role}
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        <button
                          onClick={() => handleToggleActive(agent)}
                          disabled={updatingId === agent.id}
                          className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium transition-colors disabled:opacity-50 ${
                            agent.isActive
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
                        <td colSpan={6} className="px-4 py-3">
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
