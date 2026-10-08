import { useEffect, useState } from 'react'
import { api } from '../../api/client'
import { useAuthStore } from '../../stores/authStore'

/**
 * Shown on every page while this sign-in is a ContactConnection support session (S184): whose portal, time left, and
 * End. Ending (or the 60 minutes running out) signs the support account out — the server stops the token at once.
 */
export default function SupportSessionBanner() {
  const session = useAuthStore((s) => s.supportSession)
  const [now, setNow] = useState(() => Date.now())
  const [ending, setEnding] = useState(false)

  useEffect(() => {
    if (!session) return
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [session])

  const left = session ? new Date(session.expiresAt).getTime() - now : 0
  useEffect(() => {
    if (session && left <= 0) { useAuthStore.getState().clearAuth(); window.location.assign('/login') }
  }, [session, left])

  if (!session || left <= 0) return null

  async function end() {
    setEnding(true)
    try { await api.post('/api/v1/auth/support-end') } catch { /* signing out regardless */ }
    useAuthStore.getState().clearAuth()
    window.close()
    window.location.assign('/login')
  }

  const mins = Math.floor(left / 60000)
  const secs = Math.floor((left % 60000) / 1000)
  return (
    <div className="fixed bottom-3 left-1/2 -translate-x-1/2 z-[200] flex items-center gap-3 bg-sky-950/95 border border-sky-600 text-sky-100 text-xs rounded-full pl-4 pr-1.5 py-1.5 shadow-lg shadow-black/40">
      <span className="w-2 h-2 rounded-full bg-sky-400 animate-pulse" />
      <span><span className="font-semibold">ContactConnection support session</span> · {session.tenantName}</span>
      <span className="tabular-nums text-sky-300">{mins}:{String(secs).padStart(2, '0')} left</span>
      <button onClick={() => void end()} disabled={ending}
        className="bg-sky-600 hover:bg-sky-500 disabled:opacity-50 text-white rounded-full px-3 py-1 font-medium">
        {ending ? 'Ending…' : 'End session'}
      </button>
    </div>
  )
}
