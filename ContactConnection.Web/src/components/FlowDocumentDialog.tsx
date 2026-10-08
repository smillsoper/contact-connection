import { useState } from 'react'
import { createPortal } from 'react-dom'
import { api } from '../api/client'
import type { FlowSummary } from '../api/flows'
import { useAuthStore } from '../stores/authStore'
import { getSubdomainFromHostname } from '../utils/subdomain'
import { CloseIcon, FileIcon, MailIcon } from './icons/Icons'

/**
 * Flow document (S183): a client-ready PDF of a script or call flow — an automatically drawn chart plus every step's
 * wording — from the Flows list. Email it to yourself (to forward to the client) or download it.
 */
export default function FlowDocumentDialog({ flow, onClose }: { flow: FlowSummary; onClose: () => void }) {
  const published = flow.is_active && flow.published_version != null
  const [version, setVersion] = useState<'published' | 'draft'>(published ? 'published' : 'draft')
  const [busy, setBusy] = useState<'email' | 'download' | null>(null)
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null)

  async function email() {
    setBusy('email'); setMessage(null)
    try {
      const r = await api.post<{ sentTo: string }>(`/api/v1/flows/${flow.id}/document/email`, { version })
      setMessage({ ok: true, text: `Sent to ${r.sentTo}. It should arrive in a minute.` })
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Could not send it.' })
    } finally { setBusy(null) }
  }

  async function download() {
    setBusy('download'); setMessage(null)
    try {
      const { token, tenantSubdomain } = useAuthStore.getState()
      const sub = getSubdomainFromHostname() ?? tenantSubdomain
      const res = await fetch(`/api/v1/flows/${flow.id}/document?version=${version}`, {
        headers: { Authorization: `Bearer ${token ?? ''}`, ...(sub ? { 'X-Tenant-Subdomain': sub } : {}) },
      })
      if (!res.ok) {
        const body = await res.json().catch(() => ({} as { error?: string }))
        throw new Error(body.error ?? 'Could not build the document.')
      }
      const blob = await res.blob()
      const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(res.headers.get('content-disposition') ?? '')?.[1]
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = name ? decodeURIComponent(name) : `${flow.name}.pdf`
      a.click()
      setTimeout(() => URL.revokeObjectURL(url), 1000)
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : 'Could not build the document.' })
    } finally { setBusy(null) }
  }

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div className="w-full max-w-md rounded-xl border border-gray-700 bg-gray-900 shadow-2xl">
        <div className="flex items-center justify-between px-5 py-3 border-b border-gray-800">
          <h2 className="text-white font-semibold">Flow document</h2>
          <button onClick={onClose} className="text-gray-500 hover:text-white"><CloseIcon size={16} /></button>
        </div>
        <div className="p-5 space-y-4 text-sm">
          <p className="text-gray-300">
            A PDF of <b className="text-white">{flow.name}</b>: a chart of the whole {flow.flow_type === 'telephony' ? 'call flow' : 'script'} and
            every step's wording, numbered to match — ready to send to a client. Scripts it runs or hands off to are included as
            chapters. Web addresses, credentials and card details are never shown.
          </p>

          <div className="space-y-1.5">
            <span className="text-gray-400 text-xs uppercase tracking-wide">Version</span>
            <label className={`flex items-center gap-2 ${published ? 'text-gray-200' : 'text-gray-600'}`}>
              <input type="radio" disabled={!published} checked={version === 'published'} onChange={() => setVersion('published')} className="accent-indigo-500" />
              {published ? <>Published v{flow.published_version} <span className="text-gray-500 text-xs">— what agents and callers get</span></> : 'Published — not published yet'}
            </label>
            <label className="flex items-center gap-2 text-gray-200">
              <input type="radio" checked={version === 'draft'} onChange={() => setVersion('draft')} className="accent-indigo-500" />
              Draft v{flow.version}
              <span className="text-gray-500 text-xs">— {published && !flow.has_unpublished_changes ? 'same as published' : 'marked DRAFT on every page'}</span>
            </label>
          </div>

          {message && <p className={`text-xs ${message.ok ? 'text-emerald-400' : 'text-red-400'}`}>{message.text}</p>}

          <div className="flex items-center justify-end gap-2 pt-1">
            <button onClick={download} disabled={busy !== null}
              className="inline-flex items-center gap-1.5 border border-gray-700 hover:border-gray-500 text-gray-200 rounded-lg px-3 py-1.5 disabled:opacity-50">
              <FileIcon size={14} />{busy === 'download' ? 'Building…' : 'Download'}
            </button>
            <button onClick={email} disabled={busy !== null}
              className="inline-flex items-center gap-1.5 bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-3 py-1.5 disabled:opacity-50">
              <MailIcon size={14} />{busy === 'email' ? 'Sending…' : 'Email it to me'}
            </button>
          </div>
        </div>
      </div>
    </div>,
    document.body,
  )
}
