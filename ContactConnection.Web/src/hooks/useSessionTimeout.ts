import { useCallback, useEffect, useRef, useState } from 'react'
import { useAuthStore } from '../stores/authStore'
import { authApi } from '../api/auth'

const WARN_BEFORE_MS = 5 * 60 * 1000  // show warning 5 min before expiry

function getTokenExpiry(token: string): number | null {
  try {
    const payload = JSON.parse(atob(token.split('.')[1]))
    return typeof payload.exp === 'number' ? payload.exp * 1000 : null
  } catch {
    return null
  }
}

export function useSessionTimeout(onLogout: () => void) {
  const token = useAuthStore((s) => s.token)
  const setAuth = useAuthStore((s) => s.setAuth)
  const tenantSubdomain = useAuthStore((s) => s.tenantSubdomain)

  const [showWarning, setShowWarning] = useState(false)
  const [secondsLeft, setSecondsLeft] = useState(0)

  const warnTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const logoutTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const countdownRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const clearAllTimers = useCallback(() => {
    if (warnTimerRef.current) clearTimeout(warnTimerRef.current)
    if (logoutTimerRef.current) clearTimeout(logoutTimerRef.current)
    if (countdownRef.current) clearInterval(countdownRef.current)
    warnTimerRef.current = null
    logoutTimerRef.current = null
    countdownRef.current = null
  }, [])

  const scheduleTimers = useCallback((expiryMs: number) => {
    clearAllTimers()
    const now = Date.now()
    const msUntilExpiry = expiryMs - now
    const msUntilWarn = msUntilExpiry - WARN_BEFORE_MS

    if (msUntilExpiry <= 0) {
      onLogout()
      return
    }

    if (msUntilWarn > 0) {
      warnTimerRef.current = setTimeout(() => {
        const remaining = Math.round((expiryMs - Date.now()) / 1000)
        setSecondsLeft(remaining)
        setShowWarning(true)
        countdownRef.current = setInterval(() => {
          setSecondsLeft((s) => {
            if (s <= 1) {
              clearInterval(countdownRef.current!)
              return 0
            }
            return s - 1
          })
        }, 1000)
      }, msUntilWarn)
    } else {
      // Already inside the warning window — show immediately
      const remaining = Math.round(msUntilExpiry / 1000)
      setSecondsLeft(remaining)
      setShowWarning(true)
      countdownRef.current = setInterval(() => {
        setSecondsLeft((s) => {
          if (s <= 1) {
            clearInterval(countdownRef.current!)
            return 0
          }
          return s - 1
        })
      }, 1000)
    }

    logoutTimerRef.current = setTimeout(() => {
      setShowWarning(false)
      onLogout()
    }, msUntilExpiry)
  }, [clearAllTimers, onLogout])

  // Re-schedule whenever the token changes
  useEffect(() => {
    if (!token) return
    const expiry = getTokenExpiry(token)
    if (!expiry) return
    scheduleTimers(expiry)
    return clearAllTimers
  }, [token, scheduleTimers, clearAllTimers])

  const keepAlive = useCallback(async () => {
    try {
      const res = await authApi.refresh()
      setAuth(res.token, res.agentId, tenantSubdomain ?? res.tenantSubdomain, res.role, res.firstName, res.lastName, res.permissions ?? [], res.landingPage ?? undefined)
      setShowWarning(false)
      // scheduleTimers will fire via the token useEffect above
    } catch {
      onLogout()
    }
  }, [setAuth, tenantSubdomain, onLogout])

  return { showWarning, secondsLeft, keepAlive }
}
