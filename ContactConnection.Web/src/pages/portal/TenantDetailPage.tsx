import { useEffect, useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import PortalShell from '../../components/portal/PortalShell'
import {
  getTenant,
  updateTenant,
  updateFeatureFlags,
  activateTenant,
  deactivateTenant,
  resendTenantInvite,
  resetTenantOnboarding,
  inviteTenantAdmin,
  listTenantAgents,
  resetTenantAgentPassword,
  type TenantRecord,
  type TenantFeatureFlags,
  type TenantAgentRecord,
} from '../../api/portal'

const FLAG_LABELS: Record<keyof TenantFeatureFlags, string> = {
  telephony: 'Telephony',
  omsBuiltIn: 'Commerce Engine (OMS)',
  shopifyAdapter: 'Shopify Adapter',
  tenantChat: 'Tenant Chat',
}

export default function TenantDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [tenant, setTenant] = useState<TenantRecord | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [resending, setResending] = useState(false)
  const [statusMsg, setStatusMsg] = useState<string | null>(null)

  // Agents section
  const [agents, setAgents] = useState<TenantAgentRecord[]>([])
  const [agentsLoading, setAgentsLoading] = useState(false)
  const [resetAgentId, setResetAgentId] = useState<string | null>(null)
  const [resetPw, setResetPw] = useState('')
  const [resetPwSaving, setResetPwSaving] = useState(false)
  const [resetPwError, setResetPwError] = useState<string | null>(null)

  // Invite new admin
  const [newAdminEmail, setNewAdminEmail] = useState('')
  const [inviteSending, setInviteSending] = useState(false)
  const [inviteError, setInviteError] = useState<string | null>(null)

  // Edit form state
  const [billingContact, setBillingContact] = useState('')
  const [customDomain, setCustomDomain] = useState('')
  const [inviteEmail, setInviteEmail] = useState('')

  // Feature flag state (local copy for toggle UI)
  const [flags, setFlags] = useState<TenantFeatureFlags>({
    telephony: false,
    omsBuiltIn: false,
    shopifyAdapter: false,
    tenantChat: false,
  })

  useEffect(() => {
    if (!id) return
    getTenant(id)
      .then((t) => {
        setTenant(t)
        setBillingContact(t.billingContact ?? '')
        setCustomDomain(t.customDomain ?? '')
        setInviteEmail(t.inviteEmail ?? '')
        setFlags(t.featureFlags)
        if (t.onboardingComplete) {
          setAgentsLoading(true)
          listTenantAgents(id)
            .then(setAgents)
            .catch(() => {})
            .finally(() => setAgentsLoading(false))
        }
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [id])

  function flash(msg: string) {
    setStatusMsg(msg)
    setTimeout(() => setStatusMsg(null), 3000)
  }

  async function handleSaveDetails() {
    if (!id) return
    setSaving(true)
    try {
      const updated = await updateTenant(id, {
        billingContact: billingContact || undefined,
        customDomain: customDomain || undefined,
        inviteEmail: inviteEmail || undefined,
      })
      setTenant(updated)
      flash('Details saved.')
    } catch (e) {
      flash(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  async function handleSaveFlags() {
    if (!id) return
    setSaving(true)
    try {
      const updated = await updateFeatureFlags(id, flags)
      setTenant(updated)
      flash('Feature flags saved.')
    } catch (e) {
      flash(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  async function handleResetOnboarding() {
    if (!id) return
    if (!confirm('This will delete all agents and admin invites for this tenant and mark onboarding as incomplete. Continue?')) return
    setSaving(true)
    try {
      const updated = await resetTenantOnboarding(id)
      setTenant(updated)
      setAgents([])
      flash('Onboarding reset. You can now resend the invite.')
    } catch (e) {
      flash(e instanceof Error ? e.message : 'Reset failed.')
    } finally {
      setSaving(false)
    }
  }

  async function handleResendInvite() {
    if (!id) return
    setResending(true)
    try {
      await resendTenantInvite(id)
      flash('Invitation resent successfully.')
    } catch (e) {
      flash(e instanceof Error ? e.message : 'Failed to resend invitation.')
    } finally {
      setResending(false)
    }
  }

  async function handleInviteAdmin() {
    if (!id || !newAdminEmail.trim()) return
    setInviteSending(true)
    setInviteError(null)
    try {
      await inviteTenantAdmin(id, newAdminEmail.trim())
      flash(`Invite sent to ${newAdminEmail.trim()}.`)
      setNewAdminEmail('')
    } catch (e) {
      setInviteError(e instanceof Error ? e.message : 'Failed to send invite.')
    } finally {
      setInviteSending(false)
    }
  }

  async function handleResetAgentPassword(agentId: string) {
    if (!id) return
    if (resetPw.length < 8) { setResetPwError('Password must be at least 8 characters.'); return }
    setResetPwSaving(true)
    setResetPwError(null)
    try {
      await resetTenantAgentPassword(id, agentId, resetPw)
      flash('Password reset successfully.')
      setResetAgentId(null)
      setResetPw('')
    } catch (e) {
      setResetPwError(e instanceof Error ? e.message : 'Reset failed.')
    } finally {
      setResetPwSaving(false)
    }
  }

  async function handleToggleActive() {
    if (!id || !tenant) return
    setSaving(true)
    try {
      const updated = tenant.isActive
        ? await deactivateTenant(id)
        : await activateTenant(id)
      setTenant(updated)
      flash(updated.isActive ? 'Tenant activated.' : 'Tenant deactivated.')
    } catch (e) {
      flash(e instanceof Error ? e.message : 'Action failed.')
    } finally {
      setSaving(false)
    }
  }

  function toggleFlag(key: keyof TenantFeatureFlags) {
    setFlags((prev) => ({ ...prev, [key]: !prev[key] }))
  }

  if (loading) {
    return (
      <PortalShell>
        <div className="p-6 text-gray-400 text-sm">Loading…</div>
      </PortalShell>
    )
  }

  if (error || !tenant) {
    return (
      <PortalShell>
        <div className="p-6 text-red-400 text-sm">{error ?? 'Tenant not found.'}</div>
      </PortalShell>
    )
  }

  return (
    <PortalShell>
      <div className="p-6 max-w-2xl">
        {/* Header */}
        <div className="flex items-center gap-3 mb-2">
          <Link to="/portal/tenants" className="text-gray-400 hover:text-white text-sm transition-colors">
            ← Tenants
          </Link>
          <span className="text-gray-600">/</span>
          <h1 className="text-white text-xl font-semibold">{tenant.name}</h1>
          <span
            className={`ml-2 inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${
              tenant.isActive
                ? 'bg-emerald-900/50 text-emerald-400'
                : 'bg-red-900/50 text-red-400'
            }`}
          >
            {tenant.isActive ? 'Active' : 'Inactive'}
          </span>
        </div>
        <p className="text-gray-500 text-sm mb-6">
          {tenant.subdomain}.contactconnection.local &nbsp;·&nbsp; schema: {tenant.schemaName}
        </p>

        {statusMsg && (
          <div className="mb-4 bg-indigo-900/40 border border-indigo-700 text-indigo-300 text-sm px-4 py-2 rounded-lg">
            {statusMsg}
          </div>
        )}

        {/* Onboarding */}
        <section className="bg-gray-900 rounded-xl border border-gray-800 p-5 mb-4">
          <h2 className="text-white text-sm font-semibold mb-3">Onboarding</h2>
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <span className={`text-xs font-medium ${tenant.onboardingComplete ? 'text-emerald-400' : 'text-amber-400'}`}>
                {tenant.onboardingComplete ? '● Complete' : '● Pending'}
              </span>
              {tenant.inviteEmail && (
                <span className="text-gray-500 text-xs">{tenant.inviteEmail}</span>
              )}
            </div>
            {!tenant.onboardingComplete && tenant.inviteEmail && (
              <button
                onClick={handleResendInvite}
                disabled={resending}
                className="bg-gray-800 hover:bg-gray-700 disabled:opacity-50 text-gray-200 text-xs font-medium rounded-lg px-3 py-1.5 transition-colors"
              >
                {resending ? 'Sending…' : 'Resend invite'}
              </button>
            )}
            {!tenant.onboardingComplete && !tenant.inviteEmail && (
              <span className="text-gray-600 text-xs">Set an invite email in Details to resend.</span>
            )}
            <button
              onClick={handleResetOnboarding}
              disabled={saving}
              className="bg-gray-800 hover:bg-gray-700 disabled:opacity-50 text-amber-400 text-xs font-medium rounded-lg px-3 py-1.5 transition-colors"
            >
              {tenant.onboardingComplete ? 'Reset onboarding' : 'Clear agents & invites'}
            </button>
          </div>
        </section>

        {/* Admin users */}
        {tenant.onboardingComplete && (
          <section className="bg-gray-900 rounded-xl border border-gray-800 p-5 mb-4">
            <h2 className="text-white text-sm font-semibold mb-3">Admin users</h2>
            {agentsLoading && <p className="text-gray-500 text-sm">Loading…</p>}
            {!agentsLoading && agents.length === 0 && (
              <p className="text-gray-500 text-sm">No agents found.</p>
            )}
            {agents.length > 0 && (
              <div className="flex flex-col gap-2">
                {agents.map((agent) => (
                  <div key={agent.id} className="flex flex-col gap-2">
                    <div className="flex items-center justify-between py-1.5">
                      <div className="flex items-center gap-3">
                        <span className={`inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium ${
                          agent.role === 'admin' ? 'bg-indigo-900/50 text-indigo-300' :
                          agent.role === 'supervisor' ? 'bg-sky-900/50 text-sky-300' :
                          'bg-gray-700/60 text-gray-300'
                        }`}>{agent.role}</span>
                        <span className="text-white text-sm">{agent.firstName} {agent.lastName}</span>
                        <span className="text-gray-400 text-sm">{agent.email}</span>
                        {!agent.isActive && (
                          <span className="text-xs text-red-400">Inactive</span>
                        )}
                      </div>
                      <button
                        onClick={() => {
                          if (resetAgentId === agent.id) { setResetAgentId(null); setResetPw(''); setResetPwError(null) }
                          else { setResetAgentId(agent.id); setResetPw(''); setResetPwError(null) }
                        }}
                        className="text-indigo-400 hover:text-indigo-300 text-xs font-medium transition-colors"
                      >
                        {resetAgentId === agent.id ? 'Cancel' : 'Reset password'}
                      </button>
                    </div>
                    {resetAgentId === agent.id && (
                      <div className="flex items-center gap-3 pl-2 pb-2">
                        <input
                          type="password"
                          value={resetPw}
                          onChange={(e) => { setResetPw(e.target.value); setResetPwError(null) }}
                          placeholder="New password (min 8 characters)"
                          autoFocus
                          className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-72"
                        />
                        <button
                          onClick={() => handleResetAgentPassword(agent.id)}
                          disabled={resetPwSaving}
                          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-1.5 text-sm font-medium transition-colors"
                        >
                          {resetPwSaving ? 'Saving…' : 'Save password'}
                        </button>
                        {resetPwError && <span className="text-red-400 text-xs">{resetPwError}</span>}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
            <div className="mt-4 pt-4 border-t border-gray-800">
              <p className="text-gray-500 text-xs mb-2">Invite an additional admin</p>
              <div className="flex items-center gap-3">
                <input
                  type="email"
                  value={newAdminEmail}
                  onChange={(e) => { setNewAdminEmail(e.target.value); setInviteError(null) }}
                  placeholder="admin@example.com"
                  className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-64"
                />
                <button
                  onClick={handleInviteAdmin}
                  disabled={inviteSending || !newAdminEmail.trim()}
                  className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-1.5 text-sm font-medium transition-colors"
                >
                  {inviteSending ? 'Sending…' : 'Send invite'}
                </button>
                {inviteError && <span className="text-red-400 text-xs">{inviteError}</span>}
              </div>
            </div>
          </section>
        )}

        {/* Details */}
        <section className="bg-gray-900 rounded-xl border border-gray-800 p-5 mb-4">
          <h2 className="text-white text-sm font-semibold mb-4">Details</h2>
          <div className="flex flex-col gap-4">
            <div>
              <label className="block mb-1.5 text-sky-400 text-xs font-medium">Billing contact</label>
              <input
                type="text"
                value={billingContact}
                onChange={(e) => setBillingContact(e.target.value)}
                placeholder="billing@example.com"
                className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
            <div>
              <label className="block mb-1.5 text-sky-400 text-xs font-medium">Invite email</label>
              <input
                type="email"
                value={inviteEmail}
                onChange={(e) => setInviteEmail(e.target.value)}
                placeholder="admin@acmecorp.com"
                className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
            <div>
              <label className="block mb-1.5 text-sky-400 text-xs font-medium">Custom domain</label>
              <input
                type="text"
                value={customDomain}
                onChange={(e) => setCustomDomain(e.target.value)}
                placeholder="app.acmecorp.com"
                className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
            <div className="flex items-center gap-3">
              <button
                onClick={handleSaveDetails}
                disabled={saving}
                className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
              >
                Save details
              </button>
              <button
                onClick={handleToggleActive}
                disabled={saving}
                className={`rounded-lg px-4 py-2 text-sm font-medium transition-colors disabled:opacity-50 ${
                  tenant.isActive
                    ? 'bg-red-900/50 hover:bg-red-800/60 text-red-400'
                    : 'bg-emerald-900/50 hover:bg-emerald-800/60 text-emerald-400'
                }`}
              >
                {tenant.isActive ? 'Deactivate tenant' : 'Activate tenant'}
              </button>
            </div>
          </div>
        </section>

        {/* Feature flags */}
        <section className="bg-gray-900 rounded-xl border border-gray-800 p-5">
          <h2 className="text-white text-sm font-semibold mb-4">Feature flags</h2>
          <div className="grid grid-cols-2 gap-3 mb-5">
            {(Object.keys(FLAG_LABELS) as (keyof TenantFeatureFlags)[]).map((key) => (
              <label
                key={key}
                className="flex items-center gap-3 cursor-pointer group"
                onClick={() => toggleFlag(key)}
              >
                <div
                  className={`w-9 h-5 rounded-full relative transition-colors ${
                    flags[key] ? 'bg-indigo-600' : 'bg-gray-700'
                  }`}
                >
                  <div
                    className={`absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-transform ${
                      flags[key] ? 'translate-x-4' : 'translate-x-0.5'
                    }`}
                  />
                </div>
                <span className="text-gray-300 text-sm group-hover:text-white transition-colors">
                  {FLAG_LABELS[key]}
                </span>
              </label>
            ))}
          </div>
          <button
            onClick={handleSaveFlags}
            disabled={saving}
            className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
          >
            Save feature flags
          </button>
        </section>
      </div>
    </PortalShell>
  )
}
