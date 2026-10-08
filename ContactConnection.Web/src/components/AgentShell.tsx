import { useCallback, useEffect, useRef, useState } from 'react'
import { supervisorApi } from '../api/supervisor'
import { LOGIN_NOTICE_KEY } from '../api/agentLock'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'
import { useSipStore } from '../stores/sipStore'
import { useSessionTimeout } from '../hooks/useSessionTimeout'
import { authApi } from '../api/auth'
import { api } from '../api/client'
import SessionTimeoutModal from './SessionTimeoutModal'
import SoftphonePanel from './SoftphonePanel'
import MyQueuePanel from './MyQueuePanel'
import FlowPanel from './FlowPanel'
import ChatPanel from './ChatPanel'
import CartPanel from './cart/CartPanel'
import MyCommissions from './MyCommissions'
import WrapUpSummaries from './WrapUpSummaries'
import ScreenRecordingBar from './ScreenRecordingBar'
import ScreenViewAgent from './ScreenViewAgent'
import CoachingNotesAgent from './CoachingNotesAgent'
import RemoteFixesAgent from './RemoteFixesAgent'
import ExtensionSetupPrompt from './ExtensionSetupPrompt'
import { requestPortalFocus } from '../lib/extensionBridge'

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
    authApi.refreshWithSipOnce().then((res) => {
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
    api.put('/api/v1/agent-state', { code: 'logged_out', customCodeId: null, customLabel: null }).catch(() => { })
    clearAuth()
    clearSip()
    navigate('/login', { replace: true })
  }, [clearAuth, clearSip, navigate])

  // Supervisor sign-in lock (FlowPanel relays receiveForceSignOut): the server has already recorded
  // the sign-out and rejects this token, so just drop the session and explain on the login page.
  useEffect(() => {
    const onForced = (e: Event) => {
      try { sessionStorage.setItem(LOGIN_NOTICE_KEY, (e as CustomEvent<string>).detail) } catch { /* private mode */ }
      clearAuth()
      clearSip()
      navigate('/login', { replace: true })
    }
    window.addEventListener('cc:force-signout', onForced)
    return () => window.removeEventListener('cc:force-signout', onForced)
  }, [clearAuth, clearSip, navigate])

  // Supervisor Take Over (S167): the dashboard opens the portal as /agent?takeover=<agentId>. Once
  // this window's softphone is registered (the call is about to be bridged to it), ask the server to
  // move the call + script here. Script-only take-overs (no phone call) don't need the softphone,
  // so give up waiting for registration after a few seconds and try anyway.
  const registrationStatus = useSipStore((s) => s.registrationStatus)
  const [takeOverNote, setTakeOverNote] = useState<string | null>(null)
  const takeOverDone = useRef(false)
  const [takeOverWaitOver, setTakeOverWaitOver] = useState(false)
  useEffect(() => { const t = setTimeout(() => setTakeOverWaitOver(true), 6000); return () => clearTimeout(t) }, [])
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const agentId = params.get('takeover')
    const callAgentId = params.get('callagent')
    if ((!agentId && !callAgentId) || takeOverDone.current) return
    if (callAgentId) {
      // Supervisor → agent internal call: needs this window's softphone.
      if (registrationStatus !== 'registered') return
      takeOverDone.current = true
      window.history.replaceState(null, '', '/agent')
      supervisorApi.callAgent(callAgentId).catch((e: Error) => setTakeOverNote(`Call failed: ${e.message}`))
      return
    }
    if (!agentId) return
    if (registrationStatus !== 'registered' && !takeOverWaitOver) return
    takeOverDone.current = true
    window.history.replaceState(null, '', '/agent')
    requestPortalFocus('take over')   // the dashboard opened us in the background — come forward (S183)
    setTakeOverNote('Taking over the call…')
    supervisorApi.takeOver(agentId)
      .then((r) => setTakeOverNote(r.phone ? 'You have the call and the script.' : 'You have the script.'))
      .catch((e: Error) => setTakeOverNote(`Take over failed: ${e.message}`))
  }, [registrationStatus, takeOverWaitOver])
  useEffect(() => {
    if (!takeOverNote || takeOverNote.endsWith('…')) return
    const t = setTimeout(() => setTakeOverNote(null), 8000)
    return () => clearTimeout(t)
  }, [takeOverNote])

  const { showWarning, secondsLeft, keepAlive } = useSessionTimeout(handleLogout)

  return (
    <div className="h-screen flex flex-col bg-gray-950 text-white overflow-hidden">
      {takeOverNote && (
        <div className="fixed top-3 left-1/2 -translate-x-1/2 z-50 bg-sky-950/95 border border-sky-700 text-sky-100 text-sm rounded-lg px-4 py-2 shadow-lg">
          {takeOverNote}
        </div>
      )}
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
          <MyCommissions />
          <button
            onClick={handleLogout}
            className="text-xs text-gray-400 hover:text-white transition-colors"
          >
            Sign out
          </button>
        </div>
      </header>
      <ScreenRecordingBar />
      {/* Live screen view (S183): "X is viewing your screen" + the supervisor's pointer */}
      <ScreenViewAgent />
      {/* In-call coaching notes (S183), pinned until "Got it" */}
      <CoachingNotesAgent />
      {/* Remote fixes from a supervisor (S184) */}
      <RemoteFixesAgent />
      <ExtensionSetupPrompt />

      {/* 3-panel body */}
      <div className="flex flex-1 overflow-hidden">
        {/* Left — Softphone (~240px) */}
        <div className="w-60 shrink-0 border-r border-gray-800 flex flex-col">
          <div className="flex-1 min-h-0 overflow-y-auto">
            <SoftphonePanel />
          </div>
          {/* Personal queue (S183), pinned to the bottom of the softphone */}
          <MyQueuePanel />
        </div>

        {/* Center — Flow/Script (flex grow) */}
        <div className="flex-1 flex flex-col overflow-hidden">
          <CartPanel />
          <WrapUpSummaries />
          <div className="flex-1 overflow-y-auto">
            <FlowPanel />
          </div>
        </div>

        {/* Right — Chat (~300px) */}
        <div className="w-75 xl:w-[600px] 2xl:w-[680px] shrink-0 border-l border-gray-800 flex flex-col overflow-hidden">
          <ChatPanel />
        </div>
      </div>
    </div>
  )
}
