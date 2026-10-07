import { useCallback, useEffect, useRef, useState } from 'react'
import { dedicationsApi, type MyDedication, type MyQueueRow } from '../api/dedications'
import { ChevronDownIcon, ChevronUpIcon, LockIcon, PhoneIcon } from './icons/Icons'

/**
 * Personal queue (S183), pinned to the bottom of the softphone: callers waiting right now on the campaigns this agent
 * takes calls for (assignments, groups, and any dedication), oldest first, with live wait timers. Refetches on the
 * server's queue-changed / dedication-changed pushes (FlowPanel's hub connection re-dispatches them as window events)
 * and at the moment a scheduled dedication switches on or off.
 */

function wait(since: string | null, now: number) {
  if (!since) return '—'
  const s = Math.max(0, Math.floor((now - new Date(since).getTime()) / 1000))
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}` : `${m}:${String(sec).padStart(2, '0')}`
}

export default function MyQueuePanel() {
  const [rows, setRows] = useState<MyQueueRow[] | null>(null)
  const [dedication, setDedication] = useState<MyDedication | null>(null)
  const [open, setOpen] = useState(true)
  const [now, setNow] = useState(() => Date.now())
  const debounce = useRef<ReturnType<typeof setTimeout> | null>(null)

  const loadQueue = useCallback(() => {
    if (debounce.current) clearTimeout(debounce.current)
    debounce.current = setTimeout(() => { dedicationsApi.myQueue().then(setRows).catch(() => {}) }, 150)
  }, [])
  const loadDedication = useCallback(() => { dedicationsApi.mine().then(setDedication).catch(() => {}) }, [])

  useEffect(() => {
    loadQueue(); loadDedication()
    const onQueue = () => loadQueue()
    const onDedication = () => { loadDedication(); loadQueue() }
    window.addEventListener('cc:my-queue-changed', onQueue)
    window.addEventListener('cc:dedication-changed', onDedication)
    return () => {
      window.removeEventListener('cc:my-queue-changed', onQueue)
      window.removeEventListener('cc:dedication-changed', onDedication)
      if (debounce.current) clearTimeout(debounce.current)
    }
  }, [loadQueue, loadDedication])

  // A scheduled dedication switches on/off with no push — refresh right then.
  useEffect(() => {
    if (!dedication?.nextChangeAt) return
    const ms = new Date(dedication.nextChangeAt).getTime() - Date.now() + 1000
    if (ms > 2 ** 31 - 1) return
    const t = setTimeout(() => { loadDedication(); loadQueue() }, Math.max(1000, ms))
    return () => clearTimeout(t)
  }, [dedication?.nextChangeAt, loadDedication, loadQueue])

  useEffect(() => {
    if (!rows?.length) return
    const id = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(id)
  }, [rows?.length])

  const count = rows?.length ?? 0
  const oldest = rows?.[0]?.queuedSince ?? null

  return (
    <div className="border-t border-gray-800 bg-gray-950/80">
      {dedication?.active && (
        <div className="px-3 py-2 border-b border-gray-800 bg-indigo-950/40" title={dedication.summary}>
          <p className="flex items-center gap-1.5 text-[11px] font-medium text-indigo-200">
            <LockIcon size={11} />Dedicated to {dedication.campaigns.join(', ')}
          </p>
          <p className="text-[10px] text-indigo-300/70 truncate">{dedication.summary}</p>
        </div>
      )}
      <button onClick={() => setOpen((o) => !o)}
        className="w-full flex items-center gap-2 px-3 py-2 text-left hover:bg-gray-900">
        <span className="text-[11px] font-semibold uppercase tracking-wide text-gray-400 flex-1">My queue</span>
        {count > 0 && <span className="text-[10px] text-amber-300 font-mono">{wait(oldest, now)}</span>}
        <span className={`text-[10px] font-semibold rounded-full px-1.5 min-w-[1.25rem] text-center ${count > 0 ? 'bg-amber-500 text-gray-950' : 'bg-gray-800 text-gray-500'}`}>{count}</span>
        {open ? <ChevronDownIcon size={12} className="text-gray-500" /> : <ChevronUpIcon size={12} className="text-gray-500" />}
      </button>
      {open && (
        <div className="max-h-48 overflow-y-auto pb-1">
          {rows === null ? null : count === 0 ? (
            <p className="px-3 pb-2 text-[11px] text-gray-600">No one waiting on your campaigns.</p>
          ) : rows.map((r) => (
            <div key={r.callRecordId} className="flex items-center gap-2 px-3 py-1 text-xs">
              {r.isCallback && <span title="Waiting for a callback — keeps their place in line"><PhoneIcon size={11} className="text-sky-400" /></span>}
              <span className="text-gray-300 truncate flex-1" title={r.campaign}>{r.campaign || 'Unknown campaign'}</span>
              <span className="text-gray-400 font-mono tabular-nums">{wait(r.queuedSince, now)}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
