import { useEffect, useState } from 'react'
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
  /** What the platform has enabled for the account (S182). */
  features?: { cardDataExports?: boolean }
}

/** The account's platform-enabled features; null while loading. */
export function useTenantFeatures(): CurrentTenant['features'] | null {
  const [features, setFeatures] = useState<CurrentTenant['features'] | null>(null)
  useEffect(() => { getCurrentTenant().then((t) => setFeatures(t.features ?? {})).catch(() => setFeatures({})) }, [])
  return features
}

/** Shown where a platform-enabled feature is switched off for the account. */
export const CARD_EXPORTS_OFF_NOTE = 'Card-data exports are not enabled for your account. Contact ContactConnection support with your use case to enable them.'

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
