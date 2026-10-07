import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { api } from '../api/client'
import { useAuthStore } from '../stores/authStore'
import { getSubdomainFromHostname } from '../utils/subdomain'
import CallDetailModal, { type CallDetailData } from './dashboard/CallDetailModal'
import { CloseIcon } from './icons/Icons'

// Caller history (S181): the caller's earlier calls (same caller number, same client), one click from the script — so
// "what did I order last time?" is answered without leaving the call.

interface PastCall {
  id: string; startedAt: string; campaign: string; agent: string; disposition: string | null; orderNumbers: string
  orderTotal: number | null; items: string; customerName: string; durationSeconds: number | null; matchedOn: string
}
interface History { number: string | null; total: number; items: PastCall[] }

const money = (n: number | null) => (n == null ? '' : `$${n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`)

function recording(callId: string) {
  const { token, tenantSubdomain } = useAuthStore.getState()
  const sub = getSubdomainFromHostname() ?? tenantSubdomain
  return fetch(`/api/v1/call-records/${callId}/recording`, {
    headers: { Authorization: `Bearer ${token ?? ''}`, ...(sub ? { 'X-Tenant-Subdomain': sub } : {}) },
  })
}

export default function CallerHistoryButton({ callRecordId }: { callRecordId: string | null }) {
  const [history, setHistory] = useState<History | null>(null)
  const [open, setOpen] = useState(false)
  const [detail, setDetail] = useState<{ detail: CallDetailData; canPlayRecording: boolean } | null>(null)
  const [detailId, setDetailId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = (id: string) => api.get<History>(`/api/v1/call-records/${id}/caller-history`)
    .then((h) => { setHistory(h); setError(null) })
    .catch((e) => setError(e instanceof Error ? e.message : 'Could not load the history'))

  useEffect(() => {
    setHistory(null); setOpen(false); setDetailId(null)
    if (callRecordId) load(callRecordId)
  }, [callRecordId])

  useEffect(() => {
    if (!detailId || !callRecordId) { setDetail(null); return }
    setDetail(null)
    api.get<{ detail: CallDetailData; canPlayRecording: boolean }>(`/api/v1/call-records/${callRecordId}/caller-history/${detailId}`)
      .then(setDetail).catch((e) => setError(e instanceof Error ? e.message : 'Could not load the call'))
  }, [detailId, callRecordId])

  if (!callRecordId || !history?.number) return null
  const count = history.total

  return (
    <>
      <button onClick={() => { setOpen(true); load(callRecordId) }}
        title={count ? `${count} earlier call${count === 1 ? '' : 's'} from this caller` : 'No earlier calls from this caller'}
        className={`ml-auto mb-1 shrink-0 flex items-center gap-1.5 text-xs px-2.5 py-1 rounded-md border transition-colors ${count
          ? 'border-sky-600 text-sky-200 bg-sky-500/10 hover:bg-sky-500/20' : 'border-gray-700 text-gray-500 hover:text-gray-300'}`}>
        <svg viewBox="0 0 24 24" className="w-3.5 h-3.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
          <path d="M3 12a9 9 0 1 0 3-6.7L3 8" /><path d="M3 3v5h5" /><path d="M12 7v5l3 2" />
        </svg>
        Caller history{count ? ` (${count})` : ''}
      </button>

      {open && createPortal(
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-3 sm:p-6" onClick={() => setOpen(false)}>
          <div className="bg-gray-900 border border-gray-800 rounded-xl shadow-xl w-full max-w-4xl max-h-[88vh] flex flex-col" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-start justify-between gap-4 px-5 pt-4 pb-3 border-b border-gray-800">
              <div>
                <h3 className="text-base font-semibold text-white">Caller history</h3>
                <p className="text-xs text-gray-400">
                  {count === 0 ? 'No earlier calls' : `${count} earlier call${count === 1 ? '' : 's'}`} from {history.number}
                  {count > history.items.length ? ` — showing the newest ${history.items.length}` : ''}
                </p>
              </div>
              <button className="text-gray-400 hover:text-white text-lg leading-none" onClick={() => setOpen(false)} title="Close"><CloseIcon size={18} /></button>
            </div>
            <div className="overflow-y-auto min-h-0 flex-1 p-3">
              {error && <p className="text-sm text-red-400 px-2">{error}</p>}
              {history.items.length === 0 && !error && (
                <p className="text-sm text-gray-500 text-center py-10">This is the first call we have from this number.</p>
              )}
              <div className="space-y-2">
                {history.items.map((c) => (
                  <button key={c.id} onClick={() => setDetailId(c.id)}
                    className="w-full text-left border border-gray-800 rounded-lg px-4 py-3 hover:border-sky-700 hover:bg-gray-800/40 transition-colors">
                    <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
                      <span className="text-sm text-white font-medium">{c.startedAt}</span>
                      <span className="text-sm text-gray-300">{c.campaign}</span>
                      {c.disposition && <span className="text-sm text-gray-200">{c.disposition}</span>}
                      {c.orderNumbers && <span className="text-xs font-mono text-gray-400">Order #{c.orderNumbers}</span>}
                      {c.orderTotal != null && <span className="ml-auto text-sm text-emerald-300">{money(c.orderTotal)}</span>}
                    </div>
                    {c.items && <div className="text-xs text-gray-300 mt-1">{c.items}</div>}
                    <div className="text-[11px] text-gray-500 mt-1">
                      {[c.customerName, c.agent && `with ${c.agent}`, c.matchedOn !== 'caller number' && `matched on the ${c.matchedOn}`].filter(Boolean).join(' · ')}
                    </div>
                  </button>
                ))}
              </div>
            </div>
          </div>
        </div>, document.body,
      )}

      {open && detailId && (
        <CallDetailModal data={detail?.detail ?? null} canPlayRecording={detail?.canPlayRecording ?? false}
          loadRecording={() => recording(detailId)} onClose={() => setDetailId(null)} />
      )}
    </>
  )
}
