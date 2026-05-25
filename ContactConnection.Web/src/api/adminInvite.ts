export interface TenantAdminInviteInfo {
  email: string
  role: string
  tenantId: string
  tenantName: string
  tenantDisplayName: string | null
  tenantLogoUrl: string | null
  mfaRequirement: string
  expiresAt: string
}

export interface AcceptTenantAdminInviteRequest {
  firstName: string
  lastName: string
  password: string
}

export interface AcceptTenantAdminInviteResult {
  token: string
  agent: {
    id: string
    firstName: string
    lastName: string
    email: string
    role: string
    tenantId: string
    tenantSubdomain: string
  }
}

export async function getTenantAdminInviteInfo(token: string): Promise<TenantAdminInviteInfo> {
  const res = await fetch(`/api/v1/admin-invite/${token}`)
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new Error(body?.error ?? 'This invitation link is invalid or has expired.')
  }
  return res.json()
}

export async function acceptTenantAdminInvite(
  token: string,
  data: AcceptTenantAdminInviteRequest,
): Promise<AcceptTenantAdminInviteResult> {
  const res = await fetch(`/api/v1/admin-invite/${token}/accept`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new Error(body?.error ?? 'Failed to accept invitation.')
  }
  return res.json()
}
