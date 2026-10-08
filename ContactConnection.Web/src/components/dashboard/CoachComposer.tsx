import { useCallback, useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { coachingApi, COACHING_MAX, COACHING_STATUS_LABEL, type CoachingNote } from '../../api/coaching'
import { CloseIcon, CoachIcon } from '../icons/Icons'

/**
 * Send an agent a coaching note (S183) and watch it land: Sent → Seen → Got it, live. Used in the Agent List's coach
 * window and, compact, under the live screen view.
 */
export function CoachComposer({ agentId, agentName, compact = false }: { agentId: string; agentName: string; compact?: boolean }) {
  const [text, setText] = useState('')
  const [notes, setNotes] = useState<CoachingNote[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => { coachingApi.forAgent(agentId).then(setNotes).catch(() => {}) }, [agentId])
  useEffect(() => {
    load()
    const on = (e: Event) => { if ((e as CustomEvent<string>).detail?.toLowerCase() === agentId.toLowerCase()) load() }
    window.addEventListener('cc:coaching-status', on)
    return () => window.removeEventListener('cc:coaching-status', on)
  }, [agentId, load])

  async function send() {
    const t = text.trim()
    if (!t || busy) return
    setBusy(true); setError(null)
    try {
      const n = await coachingApi.send(agentId, t)
      setNotes((list) => [n, ...list.filter((x) => x.id !== n.id)])
      setText('')
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not send it.') }
    finally { setBusy(false) }
  }

  async function retract(id: string) {
    try { const n = await coachingApi.retract(id); setNotes((list) => list.map((x) => (x.id === id ? n : x))) } catch { load() }
  }

  const statusClass = (s: CoachingNote['status']) =>
    s === 'acknowledged' ? 'text-emerald-400' : s === 'seen' ? 'text-sky-300' : s === 'retracted' ? 'text-gray-500 line-through' : 'text-amber-300'

  const input = (
    <div className="flex items-end gap-2">
      <textarea value={text} onChange={(e) => setText(e.target.value.slice(0, COACHING_MAX))} rows={compact ? 1 : 3}
        onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void send() } }}
        placeholder={`Note to ${agentName} — e.g. "Offer the 3-month package"`}
        className="flex-1 resize-none bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-amber-500" />
      <button onClick={() => void send()} disabled={!text.trim() || busy}
        className="bg-amber-500 hover:bg-amber-400 text-gray-950 font-semibold rounded-lg px-3 py-1.5 text-sm disabled:opacity-40">
        {busy ? 'Sending…' : 'Send'}
      </button>
    </div>
  )

  if (compact) {
    const last = notes[0]
    return (
      <div className="space-y-1">
        {input}
        <p className="text-[11px] text-gray-500">
          {error ? <span className="text-red-400">{error}</span>
            : last ? <>Last note: “{last.text.length > 60 ? last.text.slice(0, 60) + '…' : last.text}” · <span className={statusClass(last.status)}>{COACHING_STATUS_LABEL[last.status]}</span></>
            : `Pinned in ${agentName}'s portal until they press Got it. Enter sends.`}
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-3">
      {input}
      {error && <p className="text-xs text-red-400">{error}</p>}
      <p className="text-[11px] text-gray-500">It's pinned in {agentName}'s portal (with a soft chime) until they press <b>Got it</b>. Enter sends, Shift+Enter for a new line.</p>
      {notes.length > 0 && (
        <ul className="divide-y divide-gray-800 rounded-lg border border-gray-800">
          {notes.map((n) => (
            <li key={n.id} className="px-3 py-2 text-sm">
              <div className="flex items-start gap-2">
                <p className={`flex-1 whitespace-pre-wrap break-words ${n.status === 'retracted' ? 'text-gray-500 line-through' : 'text-gray-200'}`}>{n.text}</p>
                {n.status !== 'acknowledged' && n.status !== 'retracted' && (
                  <button onClick={() => void retract(n.id)} className="text-[11px] text-gray-500 hover:text-red-300">Take back</button>
                )}
              </div>
              <p className="text-[11px] text-gray-500">
                {new Date(n.createdAt).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })} · {n.fromName} ·{' '}
                <span className={statusClass(n.status)}>{COACHING_STATUS_LABEL[n.status]}</span>
                {n.callRecordId && <> · <a href={`/admin/calls/${n.callRecordId}`} target="_blank" rel="noreferrer" className="text-indigo-400 hover:text-indigo-300">on a call</a></>}
              </p>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/** The Agent List's coach window. */
export default function CoachModal({ agentId, agentName, onClose }: { agentId: string; agentName: string; onClose: () => void }) {
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div className="w-full max-w-lg max-h-[85vh] overflow-y-auto rounded-xl border border-gray-700 bg-gray-900 shadow-2xl">
        <div className="flex items-center gap-2 px-5 py-3 border-b border-gray-800">
          <CoachIcon size={16} className="text-amber-300" />
          <h2 className="text-white font-semibold flex-1">Coach {agentName}</h2>
          <button onClick={onClose} className="text-gray-500 hover:text-white"><CloseIcon size={16} /></button>
        </div>
        <div className="p-5"><CoachComposer agentId={agentId} agentName={agentName} /></div>
      </div>
    </div>,
    document.body,
  )
}
