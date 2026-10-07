import { useAuthStore } from '../stores/authStore'
import { getSubdomainFromHostname } from '../utils/subdomain'

/**
 * Chat images (S183) are fetched with the viewer's own sign-in and shown from blob URLs — no token ever goes into an
 * image URL, and a message's HTML never points anywhere outside the platform.
 */

const cache = new Map<string, Promise<string>>()

function headers(): Record<string, string> {
  const { token, tenantSubdomain } = useAuthStore.getState()
  const h: Record<string, string> = {}
  if (token) h['Authorization'] = `Bearer ${token}`
  const sub = getSubdomainFromHostname() ?? tenantSubdomain
  if (sub) h['X-Tenant-Subdomain'] = sub
  return h
}

export function loadChatImage(id: string): Promise<string> {
  let p = cache.get(id)
  if (!p) {
    p = fetch(`/api/v1/chat/files/${id}`, { headers: headers() })
      .then((r) => (r.ok ? r.blob() : Promise.reject(new Error(String(r.status)))))
      .then((b) => URL.createObjectURL(b))
    p.catch(() => cache.delete(id))
    cache.set(id, p)
  }
  return p
}

/** Upload a pasted / chosen image; the local copy is reused for display straight away. */
export async function uploadChatImage(file: Blob): Promise<{ id: string; url: string }> {
  const res = await fetch('/api/v1/chat/files', {
    method: 'POST', body: file, headers: { ...headers(), 'Content-Type': file.type || 'application/octet-stream' },
  })
  if (!res.ok) {
    let msg = `Upload failed (${res.status}).`
    try { msg = (await res.json()).error ?? msg } catch { /* keep the status */ }
    throw new Error(msg)
  }
  const { id } = await res.json() as { id: string }
  const url = URL.createObjectURL(file)
  cache.set(id, Promise.resolve(url))
  return { id, url }
}
