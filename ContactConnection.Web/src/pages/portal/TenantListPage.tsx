import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import PortalShell from '../../components/portal/PortalShell'
import { listTenants, resendTenantInvite, type TenantRecord } from '../../api/portal'

export default function TenantListPage() {
  const [tenants, setTenants] = useState<TenantRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [resendingId, setResendingId] = useState<string | null>(null)
  const [resendMsg, setResendMsg] = useState<{ id: string; text: string } | null>(null)

  async function handleResend(id: string) {
    setResendingId(id)
    setResendMsg(null)
    try {
      await resendTenantInvite(id)
      setResendMsg({ id, text: 'Sent!' })
      setTimeout(() => setResendMsg(null), 3000)
    } catch {
      setResendMsg({ id, text: 'Failed.' })
      setTimeout(() => setResendMsg(null), 3000)
    } finally {
      setResendingId(null)
    }
  }

  useEffect(() => {
    listTenants()
      .then(setTenants)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  return (
    <PortalShell>
      <div className="p-6">
        <div className="flex items-center justify-between mb-6">
          <h1 className="text-white text-xl font-semibold">Tenants</h1>
          <Link
            to="/portal/tenants/new"
            className="bg-indigo-600 hover:bg-indigo-500 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors"
          >
            + Provision tenant
          </Link>
        </div>

        {loading && <p className="text-gray-400 text-sm">Loading…</p>}
        {error && <p className="text-red-400 text-sm">{error}</p>}

        {!loading && !error && tenants.length === 0 && (
          <p className="text-gray-500 text-sm">No tenants yet. Provision one to get started.</p>
        )}

        {tenants.length > 0 && (
          <div className="bg-gray-900 rounded-xl overflow-hidden border border-gray-800">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 text-gray-400 text-left">
                  <th className="px-4 py-3 font-medium">Name</th>
                  <th className="px-4 py-3 font-medium">Subdomain</th>
                  <th className="px-4 py-3 font-medium">Onboarding</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="px-4 py-3 font-medium">Created</th>
                  <th className="px-4 py-3 font-medium"></th>
                </tr>
              </thead>
              <tbody>
                {tenants.map((t) => (
                  <tr
                    key={t.id}
                    className="border-b border-gray-800 last:border-0 hover:bg-gray-800/50 transition-colors"
                  >
                    <td className="px-4 py-3 text-white font-medium">{t.name}</td>
                    <td className="px-4 py-3 text-gray-300">{t.subdomain}</td>
                    <td className="px-4 py-3">
                      <span className={`text-xs font-medium ${t.onboardingComplete ? 'text-emerald-400' : 'text-amber-400'}`}>
                        {t.onboardingComplete ? 'Complete' : 'Pending'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${
                          t.isActive
                            ? 'bg-emerald-900/50 text-emerald-400'
                            : 'bg-red-900/50 text-red-400'
                        }`}
                      >
                        {t.isActive ? 'Active' : 'Inactive'}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-gray-400">
                      {new Date(t.createdAt).toLocaleDateString()}
                    </td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex items-center justify-end gap-3">
                        {!t.onboardingComplete && t.inviteEmail && (
                          <button
                            onClick={() => handleResend(t.id)}
                            disabled={resendingId === t.id}
                            className="text-amber-400 hover:text-amber-300 disabled:opacity-50 text-xs font-medium transition-colors"
                          >
                            {resendingId === t.id
                              ? 'Sending…'
                              : resendMsg?.id === t.id
                              ? resendMsg.text
                              : 'Resend invite'}
                          </button>
                        )}
                        <Link
                          to={`/portal/tenants/${t.id}`}
                          className="text-indigo-400 hover:text-indigo-300 text-xs font-medium transition-colors"
                        >
                          Manage →
                        </Link>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </PortalShell>
  )
}
