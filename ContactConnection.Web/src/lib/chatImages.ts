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

/** Upload a file as an attachment (any type but programs / scripts, up to 25 MB). */
export async function uploadChatAttachment(file: File): Promise<{ id: string; name: string; size: number; contentType: string }> {
  const res = await fetch(`/api/v1/chat/files?kind=file&name=${encodeURIComponent(file.name)}`, {
    method: 'POST', body: file, headers: { ...headers(), 'Content-Type': file.type || 'application/octet-stream' },
  })
  if (!res.ok) {
    let msg = `Upload failed (${res.status}).`
    try { msg = (await res.json()).error ?? msg } catch { /* keep the status */ }
    throw new Error(msg)
  }
  const r = await res.json() as { id: string; name: string; sizeBytes: number; contentType: string }
  return { id: r.id, name: r.name, size: r.sizeBytes, contentType: r.contentType }
}

/** Download an attachment with the viewer's sign-in and save it under its name. */
export async function downloadChatFile(id: string, name: string) {
  const res = await fetch(`/api/v1/chat/files/${id}`, { headers: headers() })
  if (!res.ok) throw new Error(res.status === 404 ? 'That file is no longer available.' : `Download failed (${res.status}).`)
  const url = URL.createObjectURL(await res.blob())
  const a = document.createElement('a')
  a.href = url; a.download = name
  document.body.appendChild(a); a.click(); a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

export function formatBytes(n: number) {
  return n < 1024 ? `${n} B` : n < 1024 * 1024 ? `${(n / 1024).toFixed(0)} KB` : `${(n / 1024 / 1024).toFixed(1)} MB`
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
