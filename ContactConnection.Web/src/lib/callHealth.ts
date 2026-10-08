import { create } from 'zustand'
import { api } from '../api/client'
import { useAuthStore } from '../stores/authStore'
import { useCallStore } from '../stores/callStore'

/**
 * Connection health (S183). On a call, every 5 s, the softphone's WebRTC stats are read — audio lost in each direction,
 * jitter, round trip, whether audio goes through our TURN relay, and the microphone level — and sent to the server,
 * which scores the call (MOS → good / fair / poor, or "no mic audio") and shows supervisors live. Idle, it reports every
 * 30 s (registration, network) so the Agent List knows the portal is alive. The agent's softphone shows the same grade.
 */

export interface AgentHealth {
  onCall: boolean
  mos: number | null
  grade: 'good' | 'fair' | 'poor' | 'unknown' | 'idle'
  lossInPct: number | null
  lossOutPct: number | null
  jitterMs: number | null
  rttMs: number | null
  relay: boolean | null
  micLevel: number | null
  micSilent: boolean
  muted: boolean
  registered: boolean | null
  networkType: string | null
  downlinkMbps: number | null
  at: string
}

export const useCallHealthStore = create<{ health: AgentHealth | null }>(() => ({ health: null }))

/** The softphone tells us how to reach the live call's RTCPeerConnection (null when there's no call). */
let peerProvider: (() => RTCPeerConnection | null | undefined) | null = null
let registeredProvider: (() => boolean) | null = null
export function setCallPeerProvider(get: () => RTCPeerConnection | null | undefined) { peerProvider = get }
export function setRegisteredProvider(get: () => boolean) { registeredProvider = get }

const ON_CALL_MS = 5000
const IDLE_MS = 30000
const SILENT_LEVEL = 0.003     // below this the mic is effectively silent
const SILENT_AFTER_MS = 10000

let timer: ReturnType<typeof setTimeout> | null = null
let last: { at: number; received: number; lost: number } | null = null
let silentSince: number | null = null

function network() {
  const c = (navigator as Navigator & { connection?: { effectiveType?: string; downlink?: number } }).connection
  return { networkType: c?.effectiveType ?? null, downlinkMbps: c?.downlink ?? null }
}

async function sample() {
  const pc = peerProvider?.() ?? null
  const onCall = !!pc && pc.connectionState !== 'closed' && useCallStore.getState().callStatus === 'on-call'
  const muted = useCallStore.getState().isMuted
  const base = { onCall, muted, registered: registeredProvider?.() ?? null, ...network() }
  if (!onCall || !pc) {
    last = null
    silentSince = null
    return { ...base, lossInPct: null, lossOutPct: null, jitterMs: null, rttMs: null, relay: null, micLevel: null, micSilent: false }
  }

  const stats = await pc.getStats()
  let received = 0, lost = 0, jitter: number | null = null, rtt: number | null = null, lossOut: number | null = null
  let micLevel: number | null = null, relay: boolean | null = null, pairLocalId: string | null = null
  stats.forEach((r) => {
    const s = r as Record<string, unknown> & { type: string }
    if (s.type === 'inbound-rtp' && s.kind === 'audio') {
      received += Number(s.packetsReceived ?? 0); lost += Number(s.packetsLost ?? 0)
      if (typeof s.jitter === 'number') jitter = s.jitter * 1000
    }
    if (s.type === 'remote-inbound-rtp' && s.kind === 'audio') {
      if (typeof s.roundTripTime === 'number') rtt = s.roundTripTime * 1000
      if (typeof s.fractionLost === 'number') lossOut = s.fractionLost * 100
    }
    if (s.type === 'media-source' && s.kind === 'audio' && typeof s.audioLevel === 'number') micLevel = s.audioLevel
    if (s.type === 'candidate-pair' && (s.nominated || s.selected) && s.state === 'succeeded') {
      if (typeof s.currentRoundTripTime === 'number' && rtt === null) rtt = s.currentRoundTripTime * 1000
      pairLocalId = String(s.localCandidateId ?? '')
    }
  })
  if (pairLocalId) {
    const local = stats.get(pairLocalId) as { candidateType?: string } | undefined
    relay = local?.candidateType === 'relay'
  }
  // Loss over the last interval, not since the call began.
  const now = Date.now()
  let lossIn: number | null = null
  if (last) {
    const dr = received - last.received, dl = lost - last.lost
    lossIn = dr + dl > 0 ? Math.max(0, (dl / (dr + dl)) * 100) : 0
  }
  last = { at: now, received, lost }

  // Mic silent for 10 s while not muted — a muted headset, a dead mic, the wrong device.
  if (micLevel !== null && micLevel < SILENT_LEVEL && !muted) silentSince ??= now
  else silentSince = null
  const micSilent = silentSince !== null && now - silentSince >= SILENT_AFTER_MS

  return { ...base, lossInPct: lossIn, lossOutPct: lossOut, jitterMs: jitter, rttMs: rtt, relay, micLevel, micSilent }
}

async function tick() {
  timer = null
  if (!useAuthStore.getState().token) return
  let onCall = false
  try {
    const report = await sample()
    onCall = report.onCall
    const health = await api.post<AgentHealth>('/api/v1/agent-health', report)
    useCallHealthStore.setState({ health })
  } catch { /* health is best effort */ }
  timer = setTimeout(() => void tick(), onCall ? ON_CALL_MS : IDLE_MS)
}

/** Start reporting (the agent shell calls this once); a call starting kicks an immediate sample. */
let watchingCalls = false
export function startCallHealth() {
  if (timer) return
  timer = setTimeout(() => void tick(), 1000)
  if (watchingCalls) return
  watchingCalls = true
  useCallStore.subscribe((s, prev) => {
    if (s.callStatus !== prev.callStatus && (s.callStatus === 'on-call' || prev.callStatus === 'on-call')) {
      if (timer) clearTimeout(timer)
      last = null
      timer = setTimeout(() => void tick(), s.callStatus === 'on-call' ? 1500 : 500)
    }
  })
}

/** Sign-out / user switch: stop reporting for the previous user. */
let healthUser = useAuthStore.getState().agentId
useAuthStore.subscribe((s) => {
  if (s.agentId === healthUser) return
  healthUser = s.agentId
  if (timer) clearTimeout(timer)
  timer = null
  last = null
  silentSince = null
  useCallHealthStore.setState({ health: null })
})

/** One-line summary for tooltips. */
export function describeHealth(h: AgentHealth): string {
  if (!h.onCall) return `Not on a call${h.registered === false ? ' · softphone not registered' : ''}${h.networkType ? ` · network ${h.networkType}` : ''}`
  const parts = [
    h.mos != null ? `Call quality ${h.mos.toFixed(1)}/4.5` : null,
    h.micSilent ? 'No audio from the microphone' : h.muted ? 'Muted' : null,
    h.lossInPct != null ? `Lost (incoming) ${h.lossInPct.toFixed(1)}%` : null,
    h.lossOutPct != null ? `Lost (outgoing) ${h.lossOutPct.toFixed(1)}%` : null,
    h.jitterMs != null ? `Jitter ${Math.round(h.jitterMs)} ms` : null,
    h.rttMs != null ? `Round trip ${Math.round(h.rttMs)} ms` : null,
    h.relay != null ? (h.relay ? 'Through relay' : 'Direct') : null,
  ]
  return parts.filter(Boolean).join(' · ')
}
