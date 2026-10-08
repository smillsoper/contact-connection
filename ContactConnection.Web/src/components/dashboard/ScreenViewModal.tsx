import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type * as signalR from '@microsoft/signalr'
import { screenViewConnection } from '../../lib/screenView'
import { loadIceServers, rtcConfig } from '../../utils/iceServers'
import { CloseIcon, PointerIcon, ScreenIcon } from '../icons/Icons'

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
  const [pointing, setPointing] = useState(true)
  const [pings, setPings] = useState<{ id: number; x: number; y: number }[]>([])

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

  /** Click on the picture → a fraction of the agent's screen (the video is letterboxed inside its box). */
  function point(e: React.MouseEvent<HTMLDivElement>) {
    const v = videoRef.current
    if (!pointing || status !== 'live' || !v || !v.videoWidth) return
    const r = v.getBoundingClientRect()
    const scale = Math.min(r.width / v.videoWidth, r.height / v.videoHeight)
    const w = v.videoWidth * scale, h = v.videoHeight * scale
    const left = r.left + (r.width - w) / 2, top = r.top + (r.height - h) / 2
    const x = (e.clientX - left) / w, y = (e.clientY - top) / h
    if (x < 0 || x > 1 || y < 0 || y > 1) return
    const id = Date.now()
    setPings((p) => [...p, { id, x: e.clientX - r.left, y: e.clientY - r.top }])
    setTimeout(() => setPings((p) => p.filter((q) => q.id !== id)), 1500)
    if (sessionRef.current) void connRef.current?.invoke('Point', sessionRef.current, x, y).catch(() => {})
  }

  const statusText = status === 'asking'
    ? (slow ? `Still waiting for ${agentName} — their agent portal may be closed, or they haven't answered the request yet.` : `Asking ${agentName}'s agent portal…`)
    : status === 'connecting' ? 'Connecting…'
    : status === 'live' ? (pointing ? 'Live — click anywhere on their screen to point at it.' : 'Live')
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
              <button onClick={() => setPointing((p) => !p)}
                className={`inline-flex items-center gap-1.5 rounded px-2.5 py-1 text-xs border ${pointing ? 'border-violet-500 bg-violet-600/30 text-white' : 'border-gray-700 text-gray-400 hover:text-gray-200'}`}
                title="Click on their screen to show them where to look">
                <PointerIcon size={13} />Point here
              </button>
            )}
            <button onClick={onClose} className="text-gray-400 hover:text-white" title="Stop viewing"><CloseIcon size={18} /></button>
          </div>
        </div>
        <div className={`relative flex-1 min-h-0 bg-black ${pointing && status === 'live' ? 'cursor-crosshair' : ''}`} onClick={point}>
          <video ref={videoRef} autoPlay playsInline muted className="w-full h-full object-contain" />
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
