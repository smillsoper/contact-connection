/**
 * Tenant web addresses (S184). Tenants live at {subdomain}.{platform domain}, or their own custom domain. The platform
 * domain is the one this page is served from (so a .cc preview links to .cc), or contactconnection.io on localhost.
 */

export const PLATFORM_DOMAINS = ['contactconnection.cc', 'contactconnection.io', 'cc.local']
export const DEFAULT_PLATFORM_DOMAIN = 'contactconnection.io'

/** The platform domain this page is on, or null on localhost / an IP. */
export function currentPlatformDomain(): string | null {
  const host = window.location.hostname
  return PLATFORM_DOMAINS.find((d) => host === d || host.endsWith(`.${d}`)) ?? null
}

/** The address a tenant's users go to, e.g. acme.contactconnection.io (or its custom domain). */
export function tenantAddress(subdomain: string, customDomain?: string | null): string {
  return customDomain || `${subdomain}.${currentPlatformDomain() ?? DEFAULT_PLATFORM_DOMAIN}`
}
