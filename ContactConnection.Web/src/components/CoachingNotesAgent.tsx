import { useCallback, useEffect, useRef, useState } from 'react'
import { coachingApi, type CoachingNote } from '../api/coaching'
import { CoachIcon } from './icons/Icons'

/**
 * Coaching notes pinned in the agent's portal (S183): what a supervisor typed while the agent works the call. Each
 * stays until the agent presses "Got it"; the supervisor sees when it was seen and acknowledged. Reloads on the
 * server's push (FlowPanel re-dispatches it as a window event) and survives a page refresh.
 */
export default function CoachingNotesAgent() {
  const [notes, setNotes] = useState<CoachingNote[]>([])
  const known = useRef<Set<string>>(new Set())
  const loaded = useRef(false)

  const load = useCallback(() => {
    coachingApi.mine().then((list) => {
      const fresh = list.some((n) => !known.current.has(n.id))
      list.forEach((n) => known.current.add(n.id))
      setNotes(list)
      // Chime for notes that arrive while the portal is open — not for ones already waiting at page load.
      if (fresh && loaded.current) chime()
      loaded.current = true
    }).catch(() => {})
  }, [])

  useEffect(() => {
    load()
    const on = () => load()
    window.addEventListener('cc:coaching-changed', on)
    return () => window.removeEventListener('cc:coaching-changed', on)
  }, [load])

  // "Seen" = shown while the agent can actually see this page.
  useEffect(() => {
    const mark = () => {
      if (document.visibilityState !== 'visible') return
      for (const n of notes) if (n.status === 'sent') void coachingApi.seen(n.id).catch(() => {})
    }
    mark()
    document.addEventListener('visibilitychange', mark)
    return () => document.removeEventListener('visibilitychange', mark)
  }, [notes])

  async function gotIt(id: string) {
    setNotes((list) => list.filter((n) => n.id !== id))
    await coachingApi.acknowledge(id).catch(() => load())
  }

  if (notes.length === 0) return null
  return (
    <div className="flex flex-col gap-px bg-amber-950/40 border-b border-amber-800/60">
      {notes.map((n) => (
        <div key={n.id} className="flex items-start gap-3 px-4 py-2 text-sm">
          <CoachIcon size={16} className="text-amber-300 mt-0.5" />
          <div className="flex-1 min-w-0">
            <p className="text-[11px] text-amber-300/80">
              Coaching from <b className="text-amber-200">{n.fromName}</b> · {new Date(n.createdAt).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}
            </p>
            <p className="text-amber-50 whitespace-pre-wrap break-words">{n.text}</p>
            {/* Right under the message (William, S184) — at the far right edge of a wide screen it went unnoticed. */}
            <button onClick={() => void gotIt(n.id)}
              className="mt-1.5 bg-amber-500 hover:bg-amber-400 text-gray-950 rounded px-3 py-1 text-xs font-semibold">Got it</button>
          </div>
        </div>
      ))}
    </div>
  )
}

/** A soft two-note chime so a new note isn't missed mid-call. */
function chime() {
  try {
    const ctx = new AudioContext()
    const now = ctx.currentTime
    for (const [i, f] of [660, 880].entries()) {
      const o = ctx.createOscillator(), g = ctx.createGain()
      o.frequency.value = f
      g.gain.setValueAtTime(0.0001, now + i * 0.14)
      g.gain.exponentialRampToValueAtTime(0.08, now + i * 0.14 + 0.02)
      g.gain.exponentialRampToValueAtTime(0.0001, now + i * 0.14 + 0.25)
      o.connect(g).connect(ctx.destination)
      o.start(now + i * 0.14)
      o.stop(now + i * 0.14 + 0.3)
    }
    setTimeout(() => void ctx.close(), 800)
  } catch { /* no audio — the note is still pinned */ }
}
