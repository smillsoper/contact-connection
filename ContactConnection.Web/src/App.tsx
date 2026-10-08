import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useAuthStore } from './stores/authStore'
import { usePortalAuthStore } from './stores/portalAuthStore'
import { getSubdomainFromHostname, isPublicSiteHost } from './utils/subdomain'
import LoginPage from './pages/LoginPage'
import MfaSetupPage from './pages/MfaSetupPage'
import MfaVerifyPage from './pages/MfaVerifyPage'
import AgentPage from './pages/AgentPage'
import FlowDesignerPage from './pages/FlowDesignerPage'
import FlowsPage from './pages/FlowsPage'
import TelephonyDesignerPage from './pages/TelephonyDesignerPage'
import PortalLoginPage from './pages/portal/PortalLoginPage'
import PortalAuthCallbackPage from './pages/portal/PortalAuthCallbackPage'
import TenantListPage from './pages/portal/TenantListPage'
import TenantDetailPage from './pages/portal/TenantDetailPage'
import InvoiceDetailPage from './pages/portal/InvoiceDetailPage'
import ProvisionTenantPage from './pages/portal/ProvisionTenantPage'
import OnboardingPage from './pages/OnboardingPage'
import TenantAdminInviteAcceptPage from './pages/TenantAdminInviteAcceptPage'
import TenantAdminPage from './pages/admin/TenantAdminPage'
import AdminAgentsPage from './pages/admin/AdminAgentsPage'
import AdminRolesPage from './pages/admin/AdminRolesPage'
import AdminCustomFieldDefinitionsPage from './pages/admin/AdminCustomFieldDefinitionsPage'
import AdminProductsPage from './pages/admin/AdminProductsPage'
import AdminMediaAgenciesPage from './pages/admin/AdminMediaAgenciesPage'
import AdminCommissionsPage from './pages/admin/AdminCommissionsPage'
import BillingPage from './pages/admin/BillingPage'
import AdminChatPage from './pages/admin/AdminChatPage'
import AdminHelpdesksPage from './pages/admin/AdminHelpdesksPage'
import AdminSupportAccessPage from './pages/admin/AdminSupportAccessPage'
import SupportLoginPage from './pages/SupportLoginPage'
import SupportSessionBanner from './components/support/SupportSessionBanner'
import AdminIconsPage from './pages/admin/AdminIconsPage'
import ExtensionPage from './pages/ExtensionPage'
import CommissionReportPage from './pages/CommissionReportPage'
import AdminExportsPage from './pages/admin/AdminExportsPage'
import AdminDispositionsPage from './pages/admin/AdminDispositionsPage'
import AdminClientUsersPage from './pages/admin/AdminClientUsersPage'
import { ClientLoginPage, ClientInvitePage, ClientMfaPage } from './pages/client/ClientAuthPages'
import ClientPortalPage from './pages/client/ClientPortalPage'
import AdminExportEditorPage from './pages/admin/AdminExportEditorPage'
import AdminProductOffersPage from './pages/admin/AdminProductOffersPage'
import AdminApiDefinitionsPage from './pages/admin/AdminApiDefinitionsPage'
import AdminApiDefinitionDetailPage from './pages/admin/AdminApiDefinitionDetailPage'
import AdminApiPreferencesPage from './pages/admin/AdminApiPreferencesPage'
import AdminCredentialsPage from './pages/admin/AdminCredentialsPage'
import AdminWebhooksPage from './pages/admin/AdminWebhooksPage'
import TelephonyPage from './pages/admin/TelephonyPage'
import SipGatewaysPage from './pages/admin/SipGatewaysPage'
import AdminBlockListPage from './pages/admin/AdminBlockListPage'
import CampaignDetailPage from './pages/admin/CampaignDetailPage'
import AdminCallsPage from './pages/admin/AdminCallsPage'
import AdminCallDetailPage from './pages/admin/AdminCallDetailPage'
import PortalApiDefinitionsPage from './pages/portal/PortalApiDefinitionsPage'
import PortalApiDefinitionDetailPage from './pages/portal/PortalApiDefinitionDetailPage'
import PortalCredentialsPage from './pages/portal/PortalCredentialsPage'
import HealthPage from './pages/portal/HealthPage'
import MaintenancePage from './pages/portal/MaintenancePage'
import LandingPage from './pages/public/LandingPage'
import { PrivacyPage, TermsPage, AcceptableUsePage } from './pages/public/LegalPages'
import CallTraceWindowPage from './pages/CallTraceWindowPage'
import DashboardsPage from './pages/DashboardsPage'
import DashboardBuilderPage from './pages/DashboardBuilderPage'

function RequireAuth({ children }: { children: React.ReactNode }) {
  const token = useAuthStore((s) => s.token)
  return token ? <>{children}</> : <Navigate to="/login" replace />
}

const isAdminSubdomain = getSubdomainFromHostname() === 'admin'

function RequirePortalAuth({ children }: { children: React.ReactNode }) {
  const token = usePortalAuthStore((s) => s.token)
  return token ? <>{children}</> : <Navigate to={isAdminSubdomain ? '/login' : '/portal/login'} replace />
}

const ADMIN_PERMISSIONS = ['agents.view', 'agents.manage', 'roles.manage', 'flows.view', 'flows.manage', 'telephony.view', 'telephony.manage', 'integrations.view', 'integrations.manage', 'reports.view', 'supervisor.monitor', 'blocklist.view', 'blocklist.manage', 'calls.manage', 'billing.manage', 'chat.manage', 'helpdesk.manage']

function RequireAdminAuth({ children }: { children: React.ReactNode }) {
  const token = useAuthStore((s) => s.token)
  const permissions = useAuthStore((s) => s.permissions)
  if (!token) return <Navigate to="/login" replace />
  if (ADMIN_PERMISSIONS.some(p => permissions.includes(p))) return <>{children}</>
  return <Navigate to="/agent" replace />
}

// Gates a route behind one specific permission, not just "is generically an admin" —
// RequireAdminAuth only checks that a user has ANY admin-ish permission, so it can't stop a user
// with e.g. only blocklist.manage from reaching a reports.view-gated page by URL. Falls back to
// /admin (itself RequireAdminAuth-gated) rather than /agent, so a user who still has some other
// admin permission lands on the dashboard instead of being bounced all the way out of the admin
// area for lacking just this one permission.
// Call Records: viewing needs calls.view or calls.manage (calls.view alone is also held by agents,
// who are kept out by RequireAdminAuth around it).
function RequireCallsAccess({ children }: { children: React.ReactNode }) {
  const hasPermission = useAuthStore((s) => s.hasPermission)
  if (hasPermission('calls.view') || hasPermission('calls.manage')) return <>{children}</>
  return <Navigate to="/admin" replace />
}

function RequirePermission({ permission, children }: { permission: string; children: React.ReactNode }) {
  const token = useAuthStore((s) => s.token)
  const hasPermission = useAuthStore((s) => s.hasPermission)
  if (!token) return <Navigate to="/login" replace />
  if (hasPermission(permission)) return <>{children}</>
  return <Navigate to="/admin" replace />
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        {/* ── Public website (S171): www / bare domain; legal pages on every host. /site previews it. ── */}
        {isPublicSiteHost() && <Route path="/" element={<LandingPage />} />}
        <Route path="/site" element={<LandingPage />} />
        <Route path="/privacy" element={<PrivacyPage />} />
        <Route path="/terms" element={<TermsPage />} />
        <Route path="/acceptable-use" element={<AcceptableUsePage />} />
        <Route path="/extension" element={<ExtensionPage />} />

        {/* ── Agent routes ── */}
        <Route path="/login" element={isAdminSubdomain ? <PortalLoginPage /> : <LoginPage />} />
        {/* ContactConnection support opening this tenant's portal from the Platform Portal (S184) */}
        <Route path="/support-login" element={<SupportLoginPage />} />
        {/* Client portal (S181) — client users, separate session from agents */}
        <Route path="/client/login" element={<ClientLoginPage />} />
        <Route path="/client/invite/:token" element={<ClientInvitePage />} />
        <Route path="/client/mfa" element={<ClientMfaPage />} />
        <Route path="/client" element={<ClientPortalPage />} />
        <Route path="/client/d/:id" element={<ClientPortalPage />} />
        <Route path="/mfa/setup" element={<MfaSetupPage />} />
        <Route path="/mfa/verify" element={<MfaVerifyPage />} />
        <Route
          path="/agent"
          element={
            <RequireAuth>
              <AgentPage />
            </RequireAuth>
          }
        />
        <Route
          path="/designer"
          element={
            <RequireAuth>
              <FlowDesignerPage />
            </RequireAuth>
          }
        />
        <Route
          path="/designer/:id"
          element={
            <RequireAuth>
              <FlowDesignerPage />
            </RequireAuth>
          }
        />
        <Route
          path="/flows"
          element={
            <RequireAuth>
              <FlowsPage />
            </RequireAuth>
          }
        />
        <Route
          path="/telephony-designer"
          element={
            <RequireAuth>
              <TelephonyDesignerPage />
            </RequireAuth>
          }
        />
        <Route
          path="/telephony-designer/:id"
          element={
            <RequireAuth>
              <TelephonyDesignerPage />
            </RequireAuth>
          }
        />
        <Route
          path="/call-trace-window"
          element={
            <RequireAuth>
              <CallTraceWindowPage />
            </RequireAuth>
          }
        />
        {/* ── Platform portal routes ── */}
        <Route path="/portal/login" element={<PortalLoginPage />} />
        <Route path="/portal/auth/callback" element={<PortalAuthCallbackPage />} />
        <Route
          path="/portal/tenants"
          element={
            <RequirePortalAuth>
              <TenantListPage />
            </RequirePortalAuth>
          }
        />
        <Route
          path="/portal/tenants/new"
          element={
            <RequirePortalAuth>
              <ProvisionTenantPage />
            </RequirePortalAuth>
          }
        />
        <Route
          path="/portal/tenants/:id"
          element={
            <RequirePortalAuth>
              <TenantDetailPage />
            </RequirePortalAuth>
          }
        />
        <Route
          path="/portal/invoices/:id"
          element={
            <RequirePortalAuth>
              <InvoiceDetailPage />
            </RequirePortalAuth>
          }
        />
        <Route
          path="/portal/api-definitions"
          element={
            <RequirePortalAuth>
              <PortalApiDefinitionsPage />
            </RequirePortalAuth>
          }
        />
        <Route
          path="/portal/api-definitions/:id"
          element={
            <RequirePortalAuth>
              <PortalApiDefinitionDetailPage />
            </RequirePortalAuth>
          }
        />
        <Route
          path="/portal/health"
          element={
            <RequirePortalAuth>
              <HealthPage />
            </RequirePortalAuth>
          }
        />
        <Route
          path="/portal/credentials"
          element={
            <RequirePortalAuth>
              <PortalCredentialsPage />
            </RequirePortalAuth>
          }
        />
        <Route
          path="/portal/maintenance"
          element={
            <RequirePortalAuth>
              <MaintenancePage />
            </RequirePortalAuth>
          }
        />
        <Route path="/portal" element={<Navigate to="/portal/tenants" replace />} />

        {/* ── Tenant onboarding and agent invite acceptance (public) ── */}
        <Route path="/onboarding/:token" element={<OnboardingPage />} />
        <Route path="/admin-invite/:token" element={<TenantAdminInviteAcceptPage />} />

        {/* ── Tenant admin portal ── */}
        <Route
          path="/admin"
          element={
            <RequireAdminAuth>
              <TenantAdminPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/agents"
          element={
            <RequireAdminAuth>
              <AdminAgentsPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/roles"
          element={
            <RequireAdminAuth>
              <AdminRolesPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/client-users"
          element={
            <RequireAdminAuth>
              <AdminClientUsersPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/dispositions"
          element={
            <RequireAdminAuth>
              <AdminDispositionsPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/custom-field-definitions"
          element={
            <RequireAdminAuth>
              <AdminCustomFieldDefinitionsPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/support-access"
          element={
            <RequireAdminAuth>
              <AdminSupportAccessPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/helpdesks"
          element={
            <RequirePermission permission="helpdesk.manage">
              <AdminHelpdesksPage />
            </RequirePermission>
          }
        />
        <Route
          path="/admin/chat"
          element={
            <RequirePermission permission="chat.manage">
              <AdminChatPage />
            </RequirePermission>
          }
        />
        <Route
          path="/admin/billing"
          element={
            <RequirePermission permission="billing.manage">
              <BillingPage />
            </RequirePermission>
          }
        />
        <Route
          path="/admin/commissions"
          element={
            <RequireAdminAuth>
              <AdminCommissionsPage />
            </RequireAdminAuth>
          }
        />
        {/* Commission report — reports.view, so supervisors can see their agents' earnings. */}
        <Route
          path="/commissions"
          element={
            <RequirePermission permission="reports.view">
              <CommissionReportPage />
            </RequirePermission>
          }
        />
        <Route
          path="/admin/media-agencies"
          element={
            <RequireAdminAuth>
              <AdminMediaAgenciesPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/products"
          element={
            <RequireAdminAuth>
              <AdminProductsPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/products/:id/offers"
          element={
            <RequireAdminAuth>
              <AdminProductOffersPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/api-definitions"
          element={
            <RequireAdminAuth>
              <AdminApiDefinitionsPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/api-definitions/:id"
          element={
            <RequireAdminAuth>
              <AdminApiDefinitionDetailPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/api-preferences"
          element={
            <RequireAdminAuth>
              <AdminApiPreferencesPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/credentials"
          element={
            <RequireAdminAuth>
              <AdminCredentialsPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/webhooks"
          element={
            <RequireAdminAuth>
              <AdminWebhooksPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/telephony"
          element={
            <RequireAdminAuth>
              <TelephonyPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/sip-gateways"
          element={
            <RequireAdminAuth>
              <SipGatewaysPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/block-list"
          element={
            <RequireAdminAuth>
              <AdminBlockListPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/icons"
          element={
            <RequireAdminAuth>
              <AdminIconsPage />
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/calls"
          element={
            <RequireAdminAuth>
              <RequireCallsAccess>
                <AdminCallsPage />
              </RequireCallsAccess>
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/calls/:id"
          element={
            <RequireAdminAuth>
              <RequireCallsAccess>
                <AdminCallDetailPage />
              </RequireCallsAccess>
            </RequireAdminAuth>
          }
        />
        <Route
          path="/admin/campaigns/:id"
          element={
            <RequireAdminAuth>
              <CampaignDetailPage />
            </RequireAdminAuth>
          }
        />

        {/* Supervisor Dashboards — moved out of the agent portal (Session 92); linked from the
            admin dashboard's Reporting section instead of the agent shell's top bar. Route paths
            kept as /dashboards, not /admin/dashboards, to avoid touching every internal
            navigate() call in DashboardsPage/DashboardBuilderPage. Gated by the specific
            reports.view permission (not just RequireAdminAuth's "any admin permission" check) —
            mutating actions (create/edit/delete a dashboard) are further gated by reports.manage
            inside the pages themselves, same convention as AdminBlockListPage's canManage. */}
        {/* Data Exports (S180) — reports.manage: export files carry customer details. */}
        <Route path="/admin/exports" element={<RequirePermission permission="reports.manage"><AdminExportsPage /></RequirePermission>} />
        <Route path="/admin/exports/new" element={<RequirePermission permission="reports.manage"><AdminExportEditorPage /></RequirePermission>} />
        <Route path="/admin/exports/:id" element={<RequirePermission permission="reports.manage"><AdminExportEditorPage /></RequirePermission>} />
        <Route
          path="/dashboards"
          element={
            <RequirePermission permission="reports.view">
              <DashboardsPage />
            </RequirePermission>
          }
        />
        <Route
          path="/dashboard-builder"
          element={
            <RequirePermission permission="reports.view">
              <DashboardBuilderPage />
            </RequirePermission>
          }
        />
        <Route
          path="/dashboard-builder/:id"
          element={
            <RequirePermission permission="reports.view">
              <DashboardBuilderPage />
            </RequirePermission>
          }
        />

        <Route path="*" element={<Navigate to={isAdminSubdomain ? '/login' : '/agent'} replace />} />
      </Routes>
      <SupportSessionBanner />
    </BrowserRouter>
  )
}
