const PLATFORM_DOMAINS = ['contactconnection.cc', 'cc.local']

/**
 * Returns the tenant subdomain for the current page.
 *
 * Resolution order:
 *  1. ?subdomain= query param  — dev override (localhost is not a secure context
 *     so custom *.cc.local hostnames crash MSAL; use localhost:5173/login?subdomain=tms instead)
 *  2. Hostname subdomain       — production (tms.contactconnection.cc → "tms")
 */
export function getSubdomainFromHostname(): string | null {
  // Dev override via query param
  const params = new URLSearchParams(window.location.search)
  const qp = params.get('subdomain')
  if (qp) return qp

  const hostname = window.location.hostname
  if (hostname === 'localhost' || /^\d+\.\d+\.\d+\.\d+$/.test(hostname)) return null

  for (const domain of PLATFORM_DOMAINS) {
    if (hostname.endsWith(`.${domain}`)) {
      const sub = hostname.slice(0, hostname.length - domain.length - 1)
      if (sub && !sub.includes('.')) return sub
    }
  }

  return null
}
