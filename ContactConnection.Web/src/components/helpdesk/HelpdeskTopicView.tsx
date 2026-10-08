import { useEffect, useMemo, useRef, useState } from 'react'
import DOMPurify from 'dompurify'
import { downloadHelpdeskFile, loadHelpdeskImage } from '../../lib/helpdeskFiles'
import { formatBytes } from '../../lib/chatImages'
import { FileIcon } from '../icons/Icons'

/** Help desk shapes (S184), as the API returns them. */
export interface HelpdeskFile { id: string; name: string; contentType: string; sizeBytes: number; isImage: boolean }
export interface HelpdeskTopic {
  id: string; title: string; html: string; sortOrder: number; updatedAt: string; updatedByName: string; attachments: HelpdeskFile[]
}
export interface Helpdesk {
  id: string; name: string; description: string | null; campaignIds: string[]; isActive: boolean; updatedAt: string; topics: HelpdeskTopic[]
}

const ALLOWED_TAGS = ['p', 'br', 'strong', 'b', 'em', 'i', 'u', 's', 'span', 'mark', 'ul', 'ol', 'li', 'a', 'img', 'code', 'pre', 'blockquote',
  'h2', 'h3', 'h4', 'hr']
const ALLOWED_ATTR = ['style', 'href', 'data-hd-file', 'data-color']

/** A topic's content (sanitized again here), its links opening in a new window, images loaded with the viewer's sign-in. */
export function HelpdeskBody({ html }: { html: string }) {
  const ref = useRef<HTMLDivElement>(null)
  const [zoom, setZoom] = useState<string | null>(null)
  const clean = useMemo(() => DOMPurify.sanitize(html, { ALLOWED_TAGS, ALLOWED_ATTR, ALLOW_DATA_ATTR: false }), [html])
  useEffect(() => {
    const el = ref.current
    if (!el) return
    el.querySelectorAll('a[href]').forEach((a) => { a.setAttribute('target', '_blank'); a.setAttribute('rel', 'noreferrer noopener') })
    el.querySelectorAll<HTMLImageElement>('img[data-hd-file]').forEach((img) => {
      img.alt = 'Image'
      loadHelpdeskImage(img.getAttribute('data-hd-file')!).then((url) => {
        img.src = url
        img.style.cursor = 'zoom-in'
        img.onclick = () => setZoom(url)
      }).catch(() => { img.alt = 'Image unavailable' })
    })
  }, [clean])
  return (
    <>
      <div ref={ref} className="helpdesk-rich text-sm text-gray-200" dangerouslySetInnerHTML={{ __html: clean }} />
      {zoom && (
        <div className="fixed inset-0 z-[70] bg-black/80 flex items-center justify-center p-6 cursor-zoom-out" onClick={() => setZoom(null)}>
          <img src={zoom} alt="" className="max-w-full max-h-full rounded shadow-2xl" />
        </div>
      )}
    </>
  )
}

export function HelpdeskAttachments({ files }: { files: HelpdeskFile[] }) {
  const [error, setError] = useState<string | null>(null)
  if (files.length === 0) return null
  return (
    <div className="mt-4 border-t border-gray-800 pt-3">
      <div className="text-[11px] uppercase tracking-wide text-gray-500 mb-2">Attached files</div>
      <div className="flex flex-wrap gap-2">
        {files.map((f) => (
          <button key={f.id} onClick={() => downloadHelpdeskFile(f.id, f.name).catch((e: Error) => setError(e.message))}
            title="Download"
            className="flex items-center gap-2 text-xs bg-gray-800 hover:bg-gray-700 border border-gray-700 rounded-md px-2.5 py-1.5 text-gray-200 max-w-[260px]">
            <FileIcon size={14} className="text-amber-300" /><span className="truncate">{f.name}</span>
            <span className="text-gray-500 shrink-0">{formatBytes(f.sizeBytes)}</span>
          </button>
        ))}
      </div>
      {error && <p className="text-xs text-red-400 mt-1">{error}</p>}
    </div>
  )
}
