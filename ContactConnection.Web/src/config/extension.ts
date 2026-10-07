/**
 * ContactConnection Agent browser extension (S183) — where to get it.
 *
 * Fill these in once the store listings are approved (the store assigns the extension ID). Until then the install
 * buttons explain that the extension is awaiting approval and that an administrator can install it.
 */
export const EXTENSION = {
  /** e.g. https://chromewebstore.google.com/detail/contactconnection-agent/<id> */
  chromeStoreUrl: '',
  /** e.g. https://microsoftedge.microsoft.com/addons/detail/<id> */
  edgeStoreUrl: '',
  /** The store-assigned ID — what admins use to force-install it for everyone (Google Admin / Intune / Group Policy). */
  extensionId: '',
  /** Oldest version the portal is happy with; older installs are asked to update. */
  minimumVersion: '0.3.0',   // 0.3.0: clicks on our pages only, no keystrokes
}

export const isEdge = () => typeof navigator !== 'undefined' && /Edg\//.test(navigator.userAgent)

/** This browser's store page, or null while the listing is pending. */
export function storeUrl(): string | null {
  const url = isEdge() ? EXTENSION.edgeStoreUrl || EXTENSION.chromeStoreUrl : EXTENSION.chromeStoreUrl
  return url || null
}

/** "0.2.0" < "0.10.1" etc. */
export function versionAtLeast(version: string | null, minimum: string) {
  if (!version) return false
  const a = version.split('.').map(Number), b = minimum.split('.').map(Number)
  for (let i = 0; i < Math.max(a.length, b.length); i++) {
    const d = (a[i] ?? 0) - (b[i] ?? 0)
    if (d !== 0) return d > 0
  }
  return true
}
