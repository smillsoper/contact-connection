import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { acceptScreenView, declineScreenView, pointInWindow, startScreenViewAgent, STROKE_MS, useScreenViewStore, type ScreenStroke } from '../lib/screenView'
import { getShareScreen, startScreenShare, useScreenShareStore } from '../lib/screenRecorder'
import { PointerIcon, ScreenIcon } from './icons/Icons'

/**
 * Live screen view, the agent's side (S183): the "X is viewing your screen" banner (always shown while anyone watches),
 * the request to share when the agent isn't sharing yet, and the supervisor's pointer.
 */
export default function ScreenViewAgent() {
  const views = useScreenViewStore((s) => s.views)
  const point = useScreenViewStore((s) => s.point)
  const shareStatus = useScreenShareStore((s) => s.status)
  const [busy, setBusy] = useState<string | null>(null)

  useEffect(() => { startScreenViewAgent() }, [])

  const watching = views.filter((v) => v.status !== 'asking')
  const asking = views.filter((v) => v.status === 'asking')

  async function share(sessionId: string) {
    setBusy(sessionId)
    try {
      await startScreenShare()
      if (useScreenShareStore.getState().status === 'sharing') await acceptScreenView(sessionId)
      else await declineScreenView(sessionId, useScreenShareStore.getState().error ?? "The agent didn't share their screen")
    } finally { setBusy(null) }
  }

  return (
    <>
      {watching.length > 0 && (
        <div className="flex items-center gap-2 px-4 py-1.5 bg-violet-700 text-white text-xs font-medium">
          <ScreenIcon size={14} />
          {watching.map((v) => v.viewerName).join(', ')} {watching.length > 1 ? 'are' : 'is'} viewing your screen
          {watching.some((v) => v.status === 'connecting') && <span className="text-violet-200 font-normal">(connecting…)</span>}
        </div>
      )}

      {asking.map((v) => (
        <div key={v.sessionId} className="flex flex-wrap items-center gap-3 px-4 py-2 bg-violet-950 border-b border-violet-800 text-sm text-violet-100">
          <ScreenIcon size={15} />
          <span><b>{v.viewerName}</b> would like to see your screen{shareStatus === 'sharing' ? '.' : ' — share your entire screen to let them.'}</span>
          <button disabled={busy === v.sessionId} onClick={() => void share(v.sessionId)}
            className="bg-violet-600 hover:bg-violet-500 text-white rounded px-3 py-1 text-xs font-medium disabled:opacity-50">
            {busy === v.sessionId ? 'Choose your screen…' : 'Share my screen'}
          </button>
          <button onClick={() => void declineScreenView(v.sessionId, 'The agent chose not to share their screen')}
            className="text-violet-300 hover:text-white text-xs">Not now</button>
        </div>
      ))}

      {point && <PointMarker key={point.at} x={point.x} y={point.y} viewerName={point.viewerName} />}
      <Drawing />
    </>
  )
}

/** The supervisor's free-hand drawing over this window; each stroke fades out a few seconds after the pen lifts. */
function Drawing() {
  const strokes = useScreenViewStore((s) => s.strokes)
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (strokes.length === 0) return
    const t = setInterval(() => {
      const at = Date.now()
      setNow(at)
      const live = useScreenViewStore.getState().strokes.filter((k) => at - k.updatedAt < STROKE_MS)
      if (live.length !== useScreenViewStore.getState().strokes.length) useScreenViewStore.setState({ strokes: live })
    }, 200)
    return () => clearInterval(t)
  }, [strokes.length])
  if (strokes.length === 0) return null
  const screen0 = getShareScreen()
  const drawn = strokes.map((k) => ({ k, pts: toWindow(k, screen0) })).filter((d) => d.pts.length > 0)
  const anyInside = drawn.some((d) => d.pts.some((p) => p.inside))
  return createPortal(
    <>
      <svg className="fixed inset-0 z-[100] pointer-events-none" width="100%" height="100%">
        {drawn.map(({ k, pts }) => (
          <polyline key={k.id} points={pts.map((p) => `${p.x},${p.y}`).join(' ')} fill="none" stroke={k.color}
            strokeWidth={4} strokeLinecap="round" strokeLinejoin="round"
            style={{ opacity: Math.max(0, Math.min(1, (STROKE_MS - (now - k.updatedAt)) / 700)), filter: 'drop-shadow(0 0 2px rgba(0,0,0,.6))' }} />
        ))}
      </svg>
      {!anyInside && (
        <div className="fixed bottom-6 left-1/2 -translate-x-1/2 z-[100] flex items-center gap-2 rounded-lg bg-violet-700 text-white text-sm px-4 py-2 shadow-xl">
          <PointerIcon size={15} />Your supervisor is drawing on another part of your screen
        </div>
      )}
    </>,
    document.body,
  )
}

function toWindow(k: ScreenStroke, screen0: ReturnType<typeof getShareScreen>) {
  const out: { x: number; y: number; inside: boolean }[] = []
  for (let i = 0; i + 1 < k.points.length; i += 2) {
    const p = pointInWindow(k.points[i], k.points[i + 1], screen0)
    if (p) out.push(p)
  }
  return out
}

/** A pulsing marker where the supervisor pointed (4 s) — or a note when it's outside this window. */
function PointMarker({ x, y, viewerName }: { x: number; y: number; viewerName: string }) {
  const [visible, setVisible] = useState(true)
  useEffect(() => {
    const t = setTimeout(() => setVisible(false), 4500)
    return () => clearTimeout(t)
  }, [])
  if (!visible) return null
  const at = pointInWindow(x, y, getShareScreen())
  if (!at || !at.inside)
    return createPortal(
      <div className="fixed bottom-6 left-1/2 -translate-x-1/2 z-[100] flex items-center gap-2 rounded-lg bg-violet-700 text-white text-sm px-4 py-2 shadow-xl">
        <PointerIcon size={15} />{viewerName} pointed at something outside this window
      </div>,
      document.body,
    )
  return createPortal(
    <div className="fixed z-[100] pointer-events-none" style={{ left: at.x, top: at.y }}>
      <span className="absolute -left-6 -top-6 w-12 h-12 rounded-full border-4 border-violet-500 animate-ping" />
      <span className="absolute -left-3 -top-3 w-6 h-6 rounded-full bg-violet-500/60 border-2 border-white shadow-lg" />
      <span className="absolute left-5 -top-2 whitespace-nowrap rounded bg-violet-700 text-white text-[11px] px-1.5 py-0.5 shadow">{viewerName}</span>
    </div>,
    document.body,
  )
}
