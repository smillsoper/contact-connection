import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useAuthStore } from './stores/authStore'
import { usePortalAuthStore } from './stores/portalAuthStore'
import { getSubdomainFromHostname } from './utils/subdomain'
import LoginPage from './pages/LoginPage'
import MfaSetupPage from './pages/MfaSetupPage'
import MfaVerifyPage from './pages/MfaVerifyPage'
import AgentPage from './pages/AgentPage'
import FlowDesignerPage from './pages/FlowDesignerPage'
import FlowsPage from './pages/FlowsPage'
import TelephonyDesignerPage from './pages/TelephonyDesignerPage'
import TelephonyFlowsPage from './pages/TelephonyFlowsPage'
import PortalLoginPage from './pages/portal/PortalLoginPage'
import PortalAuthCallbackPage from './pages/portal/PortalAuthCallbackPage'
import TenantListPage from './pages/portal/TenantListPage'
import TenantDetailPage from './pages/portal/TenantDetailPage'
import ProvisionTenantPage from './pages/portal/ProvisionTenantPage'
import OnboardingPage from './pages/OnboardingPage'
import TenantAdminInviteAcceptPage from './pages/TenantAdminInviteAcceptPage'
import TenantAdminPage from './pages/admin/TenantAdminPage'
import AdminAgentsPage from './pages/admin/AdminAgentsPage'
import AdminApiDefinitionsPage from './pages/admin/AdminApiDefinitionsPage'
import AdminApiDefinitionDetailPage from './pages/admin/AdminApiDefinitionDetailPage'
import AdminCredentialsPage from './pages/admin/AdminCredentialsPage'
import TelephonyPage from './pages/admin/TelephonyPage'
import SipGatewaysPage from './pages/admin/SipGatewaysPage'
import PortalApiDefinitionsPage from './pages/portal/PortalApiDefinitionsPage'
import PortalApiDefinitionDetailPage from './pages/portal/PortalApiDefinitionDetailPage'
import PortalCredentialsPage from './pages/portal/PortalCredentialsPage'

function RequireAuth({ children }: { children: React.ReactNode }) {
  const token = useAuthStore((s) => s.token)
  return token ? <>{children}</> : <Navigate to="/login" replace />
}

const isAdminSubdomain = getSubdomainFromHostname() === 'admin'

function RequirePortalAuth({ children }: { children: React.ReactNode }) {
  const token = usePortalAuthStore((s) => s.token)
  return token ? <>{children}</> : <Navigate to={isAdminSubdomain ? '/login' : '/portal/login'} replace />
}

function RequireAdminAuth({ children }: { children: React.ReactNode }) {
  const token = useAuthStore((s) => s.token)
  const role = useAuthStore((s) => s.role)
  if (!token) return <Navigate to="/login" replace />
  if (role !== 'admin' && role !== 'supervisor') return <Navigate to="/agent" replace />
  return <>{children}</>
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        {/* ── Agent routes ── */}
        <Route path="/login" element={isAdminSubdomain ? <PortalLoginPage /> : <LoginPage />} />
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
          path="/telephony-flows"
          element={
            <RequireAuth>
              <TelephonyFlowsPage />
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
          path="/portal/credentials"
          element={
            <RequirePortalAuth>
              <PortalCredentialsPage />
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
          path="/admin/credentials"
          element={
            <RequireAdminAuth>
              <AdminCredentialsPage />
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

        <Route path="*" element={<Navigate to={isAdminSubdomain ? '/login' : '/agent'} replace />} />
      </Routes>
    </BrowserRouter>
  )
}
