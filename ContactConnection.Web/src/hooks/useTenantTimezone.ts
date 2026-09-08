import { useEffect, useState } from 'react'
import { getCurrentTenant } from '../api/tenant'

/**
 * The current tenant's IANA timezone (e.g. "America/Los_Angeles"), or null until it loads.
 * Backed by the cached GET /api/v1/tenants/me. For display hints only — best-effort, a failed
 * fetch just leaves it null.
 */
export function useTenantTimezone(): string | null {
  const [tz, setTz] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    getCurrentTenant()
      .then((t) => { if (!cancelled) setTz(t.timezone) })
      .catch(() => { /* hint is optional */ })
    return () => { cancelled = true }
  }, [])

  return tz
}
