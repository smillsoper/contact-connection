import { useAuthStore } from '../stores/authStore'
import { getSubdomainFromHostname } from '../utils/subdomain'

/**
 * Help desk files (S184), the chat-images pattern: fetched with the viewer's own sign-in and shown from blob URLs, so no
 * token goes into a URL and a topic's HTML never points outside the platform.
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

export interface HelpdeskFileInfo { id: string; name: string; contentType: string; sizeBytes: number; isImage: boolean }

export function loadHelpdeskImage(id: string): Promise<string> {
  let p = cache.get(id)
  if (!p) {
    p = fetch(`/api/v1/helpdesks/files/${id}`, { headers: headers() })
      .then((r) => (r.ok ? r.blob() : Promise.reject(new Error(String(r.status)))))
      .then((b) => URL.createObjectURL(b))
    p.catch(() => cache.delete(id))
    cache.set(id, p)
  }
  return p
}

async function upload(helpdeskId: string, file: Blob, query: string): Promise<HelpdeskFileInfo> {
  const res = await fetch(`/api/v1/admin/helpdesks/${helpdeskId}/files${query}`, {
    method: 'POST', body: file, headers: { ...headers(), 'Content-Type': file.type || 'application/octet-stream' },
  })
  if (!res.ok) {
    let msg = `Upload failed (${res.status}).`
    try { msg = (await res.json()).error ?? msg } catch { /* keep the status */ }
    throw new Error(msg)
  }
  return res.json() as Promise<HelpdeskFileInfo>
}

/** An embedded image (PNG, JPEG, GIF, WebP up to 5 MB); the local copy is reused for display straight away. */
export async function uploadHelpdeskImage(helpdeskId: string, file: Blob): Promise<{ id: string; url: string }> {
  const r = await upload(helpdeskId, file, '')
  const url = URL.createObjectURL(file)
  cache.set(r.id, Promise.resolve(url))
  return { id: r.id, url }
}

/** An attachment (any type but programs / scripts, up to 25 MB). */
export const uploadHelpdeskAttachment = (helpdeskId: string, file: File) =>
  upload(helpdeskId, file, `?kind=file&name=${encodeURIComponent(file.name)}`)

/** Download an attachment with the viewer's sign-in and save it under its name. */
export async function downloadHelpdeskFile(id: string, name: string) {
  const res = await fetch(`/api/v1/helpdesks/files/${id}`, { headers: headers() })
  if (!res.ok) throw new Error(res.status === 404 ? 'That file is no longer available.' : `Download failed (${res.status}).`)
  const url = URL.createObjectURL(await res.blob())
  const a = document.createElement('a')
  a.href = url; a.download = name
  document.body.appendChild(a); a.click(); a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}
