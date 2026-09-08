import { api } from './client'

export interface CurrentTenant {
  id: string
  name: string
  displayName?: string | null
  logoUrl?: string | null
  subdomain: string
  /** IANA name, e.g. "America/Los_Angeles". */
  timezone: string
  onboardingComplete: boolean
}

// GET /api/v1/tenants/me is stable for the life of a tab (name / timezone don't change
// mid-session), so cache the in-flight promise and reuse it. A failed fetch clears the cache
// so a later caller can retry.
let cached: Promise<CurrentTenant> | null = null

export function getCurrentTenant(): Promise<CurrentTenant> {
  cached ??= api.get<CurrentTenant>('/api/v1/tenants/me').catch((err) => {
    cached = null
    throw err
  })
  return cached
}
