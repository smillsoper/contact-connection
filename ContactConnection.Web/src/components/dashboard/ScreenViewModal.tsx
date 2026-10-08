import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type * as signalR from '@microsoft/signalr'
import { screenViewConnection, STROKE_MS, type ScreenStroke } from '../../lib/screenView'
import { loadIceServers, rtcConfig } from '../../utils/iceServers'
import { CloseIcon, DeleteIcon, EditIcon, PointerIcon, ScreenIcon } from '../icons/Icons'

/** Must match ScreenViewHub.DrawColors on the server. */
const COLORS = ['#ef4444', '#f59e0b', '#22c55e', '#3b82f6', '#a855f7', '#ffffff']

/**
 * Live screen view, the supervisor's side (S183): watch an agent's shared screen, and point at things on it — the agent
 * sees a marker at that spot. The agent always sees a "viewing your screen" banner while this is open.
 */
export default function ScreenViewModal({ agentId, agentName, onClose }: { agentId: string; agentName: string; onClose: () => void }) {
  const videoRef = useRef<HTMLVideoElement | null>(null)
  const connRef = useRef<signalR.HubConnection | null>(null)
  const sessionRef = useRef<string | null>(null)
  const [status, setStatus] = useState<'asking' | 'connecting' | 'live' | 'ended'>('asking')
  const [message, setMessage] = useState<string | null>(null)
  const [slow, setSlow] = useState(false)
  const [tool, setTool] = useState<'point' | 'draw' | 'none'>('point')
  const [color, setColor] = useState(COLORS[0])
  const [pings, setPings] = useState<{ id: number; x: number; y: number }[]>([])
  // Free-hand drawing (S183): my strokes, shown over the picture; the same strokes go to the agent in pieces as I draw.
  const [strokes, setStrokes] = useState<ScreenStroke[]>([])
  const [now, setNow] = useState(() => Date.now())
  const drawing = useRef<{ id: string; buffer: number[]; first: boolean; last: number } | null>(null)
  const [box, setBox] = useState<{ left: number; top: number; w: number; h: number } | null>(null)

  useEffect(() => {
    let pc: RTCPeerConnection | null = null
    const pendingIce: RTCIceCandidateInit[] = []
    let alive = true
    const conn = screenViewConnection()
    connRef.current = conn

    conn.on('screenViewOffer', async (sessionId: string, sdp: string) => {
      if (sessionId !== sessionRef.current) return
      setStatus('connecting')
      pc = new RTCPeerConnection(rtcConfig())
      pc.ontrack = (e) => {
        if (videoRef.current) videoRef.current.srcObject = e.streams[0] ?? new MediaStream([e.track])
      }
      pc.onicecandidate = (e) => {
        if (e.candidate) void conn.invoke('Ice', sessionId, JSON.stringify(e.candidate.toJSON())).catch(() => {})
      }
      pc.onconnectionstatechange = () => {
        if (pc?.connectionState === 'connected') setStatus('live')
        if (pc?.connectionState === 'failed') { setStatus('ended'); setMessage('The connection to the agent\'s screen failed.') }
      }
      await pc.setRemoteDescription({ type: 'offer', sdp })
      for (const ice of pendingIce.splice(0)) await pc.addIceCandidate(ice).catch(() => {})
      const answer = await pc.createAnswer()
      await pc.setLocalDescription(answer)
      await conn.invoke('ViewerAnswer', sessionId, answer.sdp)
    })
    conn.on('screenViewIce', async (sessionId: string, candidate: string) => {
      if (sessionId !== sessionRef.current) return
      const ice = JSON.parse(candidate) as RTCIceCandidateInit
      if (pc?.remoteDescription) await pc.addIceCandidate(ice).catch(() => {})
      else pendingIce.push(ice)
    })
    conn.on('screenViewEnded', (sessionId: string, reason: string) => {
      if (sessionId !== sessionRef.current) return
      setStatus('ended')
      setMessage(reason)
      pc?.close()
    })

    ;(async () => {
      try {
        await loadIceServers()
        await conn.start()
        if (!alive) return
        sessionRef.current = await conn.invoke<string>('StartView', agentId)
      } catch (e) {
        setStatus('ended')
        setMessage(e instanceof Error ? e.message.replace(/^.*HubException: /, '') : 'Could not start the view.')
      }
    })()
    const slowTimer = setTimeout(() => setSlow(true), 20000)

    return () => {
      alive = false
      clearTimeout(slowTimer)
      if (sessionRef.current) void conn.invoke('Stop', sessionRef.current).catch(() => {})
      pc?.close()
      void conn.stop()
    }
  }, [agentId])

  /** Where the picture actually sits inside the video box (it's letterboxed), relative to the box. */
  function contentBox() {
    const v = videoRef.current
    if (!v || !v.videoWidth) return null
    const r = v.getBoundingClientRect()
    const scale = Math.min(r.width / v.videoWidth, r.height / v.videoHeight)
    const w = v.videoWidth * scale, h = v.videoHeight * scale
    return { r, left: (r.width - w) / 2, top: (r.height - h) / 2, w, h }
  }

  /** A mouse position → a fraction of the agent's screen, or null off the picture. */
  function toScreen(e: React.PointerEvent | React.MouseEvent) {
    const b = contentBox()
    if (!b) return null
    const x = (e.clientX - b.r.left - b.left) / b.w, y = (e.clientY - b.r.top - b.top) / b.h
    return x < 0 || x > 1 || y < 0 || y > 1 ? null : { x, y, b }
  }

  // Keep the drawing layer over the picture as the window resizes / the stream starts.
  useEffect(() => {
    const v = videoRef.current
    if (!v) return
    const update = () => { const b = contentBox(); setBox(b ? { left: b.left, top: b.top, w: b.w, h: b.h } : null) }
    const ro = new ResizeObserver(update)
    ro.observe(v)
    v.addEventListener('loadedmetadata', update)
    v.addEventListener('resize', update)
    return () => { ro.disconnect(); v.removeEventListener('loadedmetadata', update); v.removeEventListener('resize', update) }
  }, [])

  // My strokes fade on the same clock as the agent's copy.
  useEffect(() => {
    if (strokes.length === 0) return
    const t = setInterval(() => {
      const at = Date.now()
      setNow(at)
      setStrokes((k) => (k.some((x) => at - x.updatedAt >= STROKE_MS && drawing.current?.id !== x.id)
        ? k.filter((x) => at - x.updatedAt < STROKE_MS || drawing.current?.id === x.id) : k))
    }, 200)
    return () => clearInterval(t)
  }, [strokes.length])

  function point(e: React.MouseEvent<HTMLDivElement>) {
    if (tool !== 'point' || status !== 'live') return
    const at = toScreen(e)
    if (!at) return
    const id = Date.now()
    setPings((p) => [...p, { id, x: e.clientX - at.b.r.left, y: e.clientY - at.b.r.top }])
    setTimeout(() => setPings((p) => p.filter((q) => q.id !== id)), 1500)
    if (sessionRef.current) void connRef.current?.invoke('Point', sessionRef.current, at.x, at.y).catch(() => {})
  }

  /** Send what's been drawn since the last send (pieces every ~60 ms, so the agent sees the line as it's made). */
  function flush() {
    const d = drawing.current
    if (!d || d.buffer.length === 0 || !sessionRef.current) return
    const pts = d.buffer.splice(0)
    void connRef.current?.invoke('Draw', sessionRef.current, d.id, color, pts, d.first).catch(() => {})
    d.first = false
    d.last = Date.now()
  }

  function penDown(e: React.PointerEvent<HTMLDivElement>) {
    if (tool !== 'draw' || status !== 'live') return
    const at = toScreen(e)
    if (!at) return
    e.currentTarget.setPointerCapture(e.pointerId)
    const id = Math.random().toString(36).slice(2, 12)
    drawing.current = { id, buffer: [at.x, at.y], first: true, last: 0 }
    setStrokes((k) => [...k, { id, color, points: [at.x, at.y], updatedAt: Date.now() }])
  }

  function penMove(e: React.PointerEvent<HTMLDivElement>) {
    const d = drawing.current
    if (!d) return
    const at = toScreen(e)
    if (!at) return
    d.buffer.push(at.x, at.y)
    setStrokes((k) => k.map((x) => (x.id === d.id ? { ...x, points: [...x.points, at.x, at.y], updatedAt: Date.now() } : x)))
    if (Date.now() - d.last > 60 || d.buffer.length >= 400) flush()
  }

  function penUp() {
    if (!drawing.current) return
    flush()
    const id = drawing.current.id
    drawing.current = null
    setStrokes((k) => k.map((x) => (x.id === id ? { ...x, updatedAt: Date.now() } : x)))
  }

  function clearDrawing() {
    setStrokes([])
    if (sessionRef.current) void connRef.current?.invoke('ClearDrawing', sessionRef.current).catch(() => {})
  }

  const statusText = status === 'asking'
    ? (slow ? `Still waiting for ${agentName} — their agent portal may be closed, or they haven't answered the request yet.` : `Asking ${agentName}'s agent portal…`)
    : status === 'connecting' ? 'Connecting…'
    : status === 'live' ? (tool === 'point' ? 'Live — click on their screen to point at it.' : tool === 'draw' ? 'Live — draw on their screen; it fades a few seconds after you lift.' : 'Live')
    : (message ?? 'The view has ended.')

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4">
      <div className="flex flex-col w-full max-w-6xl h-[90vh] rounded-xl border border-gray-700 bg-gray-950 shadow-2xl">
        <div className="flex items-center gap-3 px-4 py-2.5 border-b border-gray-800">
          <ScreenIcon size={16} className="text-violet-300" />
          <h2 className="text-white font-semibold">{agentName}'s screen</h2>
          <span className={`text-xs ${status === 'live' ? 'text-emerald-400' : status === 'ended' ? 'text-amber-300' : 'text-gray-400'}`}>{statusText}</span>
          <div className="ml-auto flex items-center gap-2">
            {status === 'live' && (
              <>
                <button onClick={() => setTool((t) => (t === 'point' ? 'none' : 'point'))}
                  className={`inline-flex items-center gap-1.5 rounded px-2.5 py-1 text-xs border ${tool === 'point' ? 'border-violet-500 bg-violet-600/30 text-white' : 'border-gray-700 text-gray-400 hover:text-gray-200'}`}
                  title="Click on their screen to show them where to look">
                  <PointerIcon size={13} />Point here
                </button>
                <button onClick={() => setTool((t) => (t === 'draw' ? 'none' : 'draw'))}
                  className={`inline-flex items-center gap-1.5 rounded px-2.5 py-1 text-xs border ${tool === 'draw' ? 'border-violet-500 bg-violet-600/30 text-white' : 'border-gray-700 text-gray-400 hover:text-gray-200'}`}
                  title="Draw on their screen — circle something, underline it, draw an arrow">
                  <EditIcon size={13} />Draw
                </button>
                {tool === 'draw' && (
                  <div className="flex items-center gap-1" role="radiogroup" aria-label="Pen colour">
                    {COLORS.map((c) => (
                      <button key={c} onClick={() => setColor(c)} role="radio" aria-checked={color === c} title={c}
                        className={`w-5 h-5 rounded-full border-2 ${color === c ? 'border-white scale-110' : 'border-gray-600'}`} style={{ background: c }} />
                    ))}
                  </div>
                )}
                <button onClick={clearDrawing} disabled={strokes.length === 0}
                  className="inline-flex items-center gap-1.5 rounded px-2.5 py-1 text-xs border border-gray-700 text-gray-400 hover:text-gray-200 disabled:opacity-40"
                  title="Remove the drawing from their screen now">
                  <DeleteIcon size={13} />Clear
                </button>
              </>
            )}
            <button onClick={onClose} className="text-gray-400 hover:text-white" title="Stop viewing"><CloseIcon size={18} /></button>
          </div>
        </div>
        <div className={`relative flex-1 min-h-0 bg-black touch-none ${tool !== 'none' && status === 'live' ? 'cursor-crosshair' : ''}`}
          onClick={point} onPointerDown={penDown} onPointerMove={penMove} onPointerUp={penUp} onPointerCancel={penUp}>
          <video ref={videoRef} autoPlay playsInline muted className="w-full h-full object-contain" />
          {box && strokes.length > 0 && (
            <svg className="absolute pointer-events-none" style={{ left: box.left, top: box.top, width: box.w, height: box.h }}
              viewBox="0 0 1 1" preserveAspectRatio="none">
              {strokes.map((k) => (
                <polyline key={k.id} fill="none" stroke={k.color} strokeWidth={3} vectorEffect="non-scaling-stroke"
                  strokeLinecap="round" strokeLinejoin="round"
                  points={k.points.reduce<string[]>((a, v, i) => (i % 2 ? (a[a.length - 1] += `,${v}`, a) : [...a, `${v}`]), []).join(' ')}
                  style={{ opacity: drawing.current?.id === k.id ? 1 : Math.max(0, Math.min(1, (STROKE_MS - (now - k.updatedAt)) / 700)) }} />
              ))}
            </svg>
          )}
          {pings.map((p) => (
            <span key={p.id} className="absolute pointer-events-none" style={{ left: p.x, top: p.y }}>
              <span className="absolute -left-5 -top-5 w-10 h-10 rounded-full border-4 border-violet-500 animate-ping" />
            </span>
          ))}
          {status !== 'live' && (
            <div className="absolute inset-0 flex items-center justify-center text-sm text-gray-400 px-8 text-center">{statusText}</div>
          )}
        </div>
        <p className="px-4 py-1.5 text-[11px] text-gray-500 border-t border-gray-800">
          {agentName} sees a banner while you're viewing. Each view is recorded in the audit log.
        </p>
      </div>
    </div>,
    document.body,
  )
}
