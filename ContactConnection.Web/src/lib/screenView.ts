import * as signalR from '@microsoft/signalr'
import { create } from 'zustand'
import { useAuthStore } from '../stores/authStore'
import { getSubdomainFromHostname } from '../utils/subdomain'
import { rtcConfig, loadIceServers } from '../utils/iceServers'
import { getShareStream, useScreenShareStore } from './screenRecorder'

/**
 * Live screen view (S183), the agent's side. A supervisor asks to watch (hub /hubs/screen-view); if this portal is
 * already sharing the screen (the once-per-shift share), it answers straight away, otherwise the agent is asked to
 * share. The video goes browser to browser over WebRTC (TURN relay when needed); the hub carries only the set-up and
 * the supervisor's pointer clicks. The agent always sees who's watching.
 */

export interface ScreenViewRequest {
  sessionId: string
  viewerName: string
  status: 'asking' | 'connecting' | 'live'
}

export interface ScreenPoint { x: number; y: number; viewerName: string; at: number }

interface ScreenViewState {
  views: ScreenViewRequest[]
  point: ScreenPoint | null
}

export const useScreenViewStore = create<ScreenViewState>(() => ({ views: [], point: null }))

/** A hub connection to /hubs/screen-view with the signed-in user's token. */
export function screenViewConnection() {
  const { token, tenantSubdomain } = useAuthStore.getState()
  return new signalR.HubConnectionBuilder()
    .withUrl(`/hubs/screen-view?access_token=${token ?? ''}`, {
      headers: { 'X-Tenant-Subdomain': getSubdomainFromHostname() ?? tenantSubdomain ?? '' },
    })
    .withAutomaticReconnect()
    .build()
}

let connection: signalR.HubConnection | null = null
const peers = new Map<string, { pc: RTCPeerConnection; pendingIce: RTCIceCandidateInit[] }>()

function setView(sessionId: string, patch: Partial<ScreenViewRequest> | null, viewerName = '') {
  useScreenViewStore.setState((s) => {
    if (patch === null) return { views: s.views.filter((v) => v.sessionId !== sessionId) }
    const existing = s.views.find((v) => v.sessionId === sessionId)
    return existing
      ? { views: s.views.map((v) => (v.sessionId === sessionId ? { ...v, ...patch } : v)) }
      : { views: [...s.views, { sessionId, viewerName, status: 'asking', ...patch }] }
  })
}

function closePeer(sessionId: string) {
  peers.get(sessionId)?.pc.close()
  peers.delete(sessionId)
}

/** Connect once (the agent shell calls this on mount). */
export function startScreenViewAgent() {
  if (connection) return
  const c = screenViewConnection()
  connection = c
  void loadIceServers()

  c.on('screenViewRequested', (sessionId: string, viewerName: string) => {
    setView(sessionId, {}, viewerName)
    if (getShareStream()) void acceptScreenView(sessionId)
  })
  c.on('screenViewAnswer', async (sessionId: string, sdp: string) => {
    const peer = peers.get(sessionId)
    if (!peer) return
    await peer.pc.setRemoteDescription({ type: 'answer', sdp })
    for (const ice of peer.pendingIce.splice(0)) await peer.pc.addIceCandidate(ice).catch(() => {})
  })
  c.on('screenViewIce', async (sessionId: string, candidate: string) => {
    const peer = peers.get(sessionId)
    if (!peer) return
    const ice = JSON.parse(candidate) as RTCIceCandidateInit
    if (peer.pc.remoteDescription) await peer.pc.addIceCandidate(ice).catch(() => {})
    else peer.pendingIce.push(ice)
  })
  c.on('screenViewPoint', (sessionId: string, x: number, y: number, viewerName: string) => {
    if (!peers.has(sessionId)) return
    useScreenViewStore.setState({ point: { x, y, viewerName, at: Date.now() } })
  })
  c.on('screenViewEnded', (sessionId: string) => {
    closePeer(sessionId)
    setView(sessionId, null)
  })
  void c.start().catch(() => { /* no live view this session; everything else works */ })

  // The agent stopped sharing (browser bar, or the screen went away): every view ends.
  useScreenShareStore.subscribe((s, prev) => {
    if (prev.status === 'sharing' && s.status !== 'sharing')
      for (const id of [...peers.keys()]) {
        closePeer(id)
        setView(id, null)
        void connection?.invoke('Stop', id).catch(() => {})
      }
  })
}

/** Send this screen to the supervisor (the screen must already be shared). */
export async function acceptScreenView(sessionId: string) {
  const stream = getShareStream()
  if (!connection || !stream) return
  if (peers.has(sessionId)) return
  setView(sessionId, { status: 'connecting' })
  const pc = new RTCPeerConnection(rtcConfig())
  peers.set(sessionId, { pc, pendingIce: [] })
  for (const track of stream.getVideoTracks()) pc.addTrack(track, stream)
  pc.onicecandidate = (e) => {
    if (e.candidate) void connection?.invoke('Ice', sessionId, JSON.stringify(e.candidate.toJSON())).catch(() => {})
  }
  pc.onconnectionstatechange = () => {
    if (pc.connectionState === 'connected') setView(sessionId, { status: 'live' })
    if (pc.connectionState === 'failed') {
      closePeer(sessionId)
      setView(sessionId, null)
      void connection?.invoke('Stop', sessionId).catch(() => {})
    }
  }
  const offer = await pc.createOffer()
  await pc.setLocalDescription(offer)
  try {
    await connection.invoke('AgentOffer', sessionId, offer.sdp)
  } catch {
    closePeer(sessionId)
    setView(sessionId, null)
  }
}

export async function declineScreenView(sessionId: string, reason = "The agent didn't share their screen") {
  setView(sessionId, null)
  await connection?.invoke('Decline', sessionId, reason).catch(() => {})
}

/**
 * Where a supervisor's point (fractions of the shared screen) lands in this window, in CSS pixels — or null when it's
 * outside this window. The share is of the screen this window is on; the window's position on it comes from
 * screenX / screenY and the browser's own toolbars (outer minus inner size).
 */
export function pointInWindow(x: number, y: number, screen0: { l: number; t: number; w: number; h: number } | null) {
  if (!screen0) return null
  const sx = screen0.l + x * screen0.w
  const sy = screen0.t + y * screen0.h
  const side = Math.max(0, (window.outerWidth - window.innerWidth) / 2)
  const top = Math.max(0, window.outerHeight - window.innerHeight - side)
  const cx = sx - window.screenX - side
  const cy = sy - window.screenY - top
  const inside = cx >= 0 && cy >= 0 && cx <= window.innerWidth && cy <= window.innerHeight
  return { x: cx, y: cy, inside }
}
