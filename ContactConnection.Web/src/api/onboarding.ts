export interface OnboardingFeatureFlags {
  telephony: boolean
  omsBuiltIn: boolean
  shopifyAdapter: boolean
  tenantChat: boolean
}

export interface OnboardingInfo {
  tenantId: string
  tenantName: string
  timezone: string
  email: string
  expiresAt: string
  featureFlags: OnboardingFeatureFlags
}

export interface CompleteOnboardingRequest {
  // Account
  firstName: string
  lastName: string
  password: string
  // Identity & Branding
  displayName?: string
  logoUrl?: string
  // Locale & Regional
  dateFormat: string
  timeFormat: string
  // Communication
  supportEmail?: string
  billingEmail?: string
  // Security
  sessionTimeoutMinutes: number
  mfaRequirement: string
  // Feature flags
  featureFlags?: OnboardingFeatureFlags
  // Additional admins
  additionalAdminEmails?: string[]
}

export interface OnboardingAgentResult {
  id: string
  firstName: string
  lastName: string
  email: string
  role: string
  tenantId: string
  tenantSubdomain: string
}

export interface OnboardingCompleteResult {
  token: string
  agent: OnboardingAgentResult
}

export async function getOnboardingInfo(token: string): Promise<OnboardingInfo> {
  const res = await fetch(`/api/v1/onboarding/${token}`)
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new Error(body?.error ?? 'This invitation link is invalid or has expired.')
  }
  return res.json()
}

export async function completeOnboarding(
  token: string,
  data: CompleteOnboardingRequest
): Promise<OnboardingCompleteResult> {
  const res = await fetch(`/api/v1/onboarding/${token}/complete`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new Error(body?.error ?? 'Failed to complete onboarding.')
  }
  return res.json()
}
