import { useCallback, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'
import { useSipStore } from '../stores/sipStore'
import { useSessionTimeout } from '../hooks/useSessionTimeout'
import { authApi } from '../api/auth'
import { api } from '../api/client'
import SessionTimeoutModal from './SessionTimeoutModal'
import SoftphonePanel from './SoftphonePanel'
import FlowPanel from './FlowPanel'
import ChatPanel from './ChatPanel'

export default function AgentShell() {
  const clearAuth = useAuthStore((s) => s.clearAuth)
  const setAuth = useAuthStore((s) => s.setAuth)
  const clearSip = useSipStore((s) => s.clear)
  const sipExtension = useSipStore((s) => s.sipExtension)
  const setSipCredentials = useSipStore((s) => s.setSipCredentials)
  const navigate = useNavigate()

  // On page refresh the SIP store is empty (non-persisted by design).
  // Fire a token refresh to get a fresh SIP password without requiring re-login.
  useEffect(() => {
    if (sipExtension) return
    authApi.refresh().then((res) => {
      setAuth(res.token, res.agentId, res.tenantSubdomain, res.role, res.firstName, res.lastName, res.permissions ?? [], res.landingPage ?? undefined)
      if (res.sipExtension && res.sipPassword) {
        setSipCredentials(res.sipExtension, res.sipPassword)
      }
    }).catch(() => {
      // Token expired or invalid — session timeout handler will redirect to login
    })
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const handleLogout = useCallback(() => {
    // Fire-and-forget — must fire before clearAuth() wipes the token used for this request.
    // Feeds agent_state_history so future reporting can show logout timestamps/durations.
    api.put('/api/v1/agent-state', { code: 'logged_out', customCodeId: null, customLabel: null }).catch(() => {})
    clearAuth()
    clearSip()
    navigate('/login', { replace: true })
  }, [clearAuth, clearSip, navigate])

  const { showWarning, secondsLeft, keepAlive } = useSessionTimeout(handleLogout)

  return (
    <div className="h-screen flex flex-col bg-gray-950 text-white overflow-hidden">
      {showWarning && (
        <SessionTimeoutModal
          secondsLeft={secondsLeft}
          onKeepAlive={keepAlive}
          onLogout={handleLogout}
        />
      )}

      {/* Top bar */}
      <header className="flex items-stretch bg-gray-900 border-b border-gray-800 shrink-0">
        <img src="/cc-navbar-dark.svg" alt="Contact Connection" className="shrink-0 block" />
        <div className="flex items-center justify-end flex-1 gap-4 px-4">
          <button
            onClick={() => navigate('/flows')}
            className="text-xs text-gray-400 hover:text-indigo-300 transition-colors"
          >
            Flows
          </button>
          <button
            onClick={handleLogout}
            className="text-xs text-gray-400 hover:text-white transition-colors"
          >
            Sign out
          </button>
        </div>
      </header>

      {/* 3-panel body */}
      <div className="flex flex-1 overflow-hidden">
        {/* Left — Softphone (~240px) */}
        <div className="w-60 shrink-0 border-r border-gray-800 overflow-y-auto">
          <SoftphonePanel />
        </div>

        {/* Center — Flow/Script (flex grow) */}
        <div className="flex-1 overflow-y-auto">
          <FlowPanel />
        </div>

        {/* Right — Chat (~300px) */}
        <div className="w-75 shrink-0 border-l border-gray-800 overflow-y-auto">
          <ChatPanel />
        </div>
      </div>
    </div>
  )
}
