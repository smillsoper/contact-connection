import { useEffect, useRef, useState } from 'react'
import JsSIP from 'jssip'
import { useSipStore, type SipRegistrationStatus } from '../stores/sipStore'
import { useCallStore } from '../stores/callStore'
import { useAuthStore } from '../stores/authStore'
import { api } from '../api/client'
import { getClientTransferNumbers, type ClientTransferNumber } from '../api/telephony'
import { flowsApi } from '../api/flows'
import { useFlowSessionsStore } from '../stores/flowSessionsStore'
import { useAgentStateStore } from '../stores/agentStateStore'

// Local dev: connect directly to FreeSWITCH (no cert required, no tunnel overhead).
// External: VITE_SIP_WS_URL must be set to the production WSS endpoint (e.g. the
// Cloudflare tunnel hostname that routes to FreeSWITCH port 7080).
function getSipWsUrl(): string {
  const { hostname } = window.location
  if (hostname === 'localhost' || hostname === '127.0.0.1') return 'ws://localhost:7080'
  if (import.meta.env.VITE_SIP_WS_URL) return import.meta.env.VITE_SIP_WS_URL as string
  const scheme = window.location.protocol === 'https:' ? 'wss' : 'ws'
  return `${scheme}://${hostname}/sip-ws`
}
const SIP_WS_URL = getSipWsUrl()

const REG_COLOR: Record<SipRegistrationStatus, string> = {
  idle:        'bg-gray-500',
  registering: 'bg-yellow-500 animate-pulse',
  registered:  'bg-green-500',
  failed:      'bg-red-500',
}

const REG_LABEL: Record<SipRegistrationStatus, string> = {
  idle:        'Not registered',
  registering: 'Registering…',
  registered:  'Registered',
  failed:      'Registration failed',
}

function formatElapsed(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = seconds % 60
  return `${m}:${s.toString().padStart(2, '0')}`
}

// ── Icons (inline so no icon library needed) ──────────────────────────────

const PhoneIcon = ({ className }: { className?: string }) => (
  <svg className={className} fill="currentColor" viewBox="0 0 24 24">
    <path d="M6.62 10.79a15.05 15.05 0 006.59 6.59l2.2-2.2a1 1 0 011.01-.24 11.48 11.48 0 003.6.57 1 1 0 011 1V21a1 1 0 01-1 1A17 17 0 013 5a1 1 0 011-1h3.5a1 1 0 011 1 11.48 11.48 0 00.57 3.6 1 1 0 01-.25 1.01l-2.2 2.18z"/>
  </svg>
)

const MicOffIcon = ({ className }: { className?: string }) => (
  <svg className={className} fill="currentColor" viewBox="0 0 24 24">
    <path d="M19 11h-1.7c0 .74-.16 1.43-.43 2.05l1.23 1.23c.56-.98.9-2.09.9-3.28zm-4.02.17c0-.06.02-.11.02-.17V5c0-1.66-1.34-3-3-3S9 3.34 9 5v.18l5.98 5.99zM4.27 3L3 4.27l6.01 6.01V11c0 1.66 1.33 3 2.99 3 .22 0 .44-.03.65-.08l1.66 1.66c-.71.33-1.5.52-2.31.52-2.76 0-5.3-2.1-5.3-5.1H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c.91-.13 1.77-.45 2.54-.9L19.73 21 21 19.73 4.27 3z"/>
  </svg>
)

const MicIcon = ({ className }: { className?: string }) => (
  <svg className={className} fill="currentColor" viewBox="0 0 24 24">
    <path d="M12 14c1.66 0 2.99-1.34 2.99-3L15 5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5.3-3c0 3-2.54 5.1-5.3 5.1S6.7 14 6.7 11H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c3.28-.48 6-3.3 6-6.72h-1.7z"/>
  </svg>
)

const PauseIcon = ({ className }: { className?: string }) => (
  <svg className={className} fill="currentColor" viewBox="0 0 24 24">
    <path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/>
  </svg>
)

const PlayIcon = ({ className }: { className?: string }) => (
  <svg className={className} fill="currentColor" viewBox="0 0 24 24">
    <path d="M8 5v14l11-7z"/>
  </svg>
)

// ─────────────────────────────────────────────────────────────────────────────

export default function SoftphonePanel() {
  const { sipExtension, sipPassword, registrationStatus, setRegistrationStatus } = useSipStore()
  const tenantSubdomain = useAuthStore((s) => s.tenantSubdomain)

  // ── Agent state ───────────────────────────────────────────────────────────
  type AgentStateCode = 'unavailable' | 'available' | 'unavailable_break' | 'unavailable_lunch' | 'on_call' | string
  type CustomCode = { id: string; name: string }

  const { agentStateCode: agentState, agentStateExpiresAt, setAgentStateCode } = useAgentStateStore()
  const [stateOpen, setStateOpen]   = useState(false)
  const [customCodes, setCustomCodes] = useState<CustomCode[]>([])
  const [acwCountdown, setAcwCountdown] = useState<number | null>(null)

  useEffect(() => {
    api.get<CustomCode[]>('/api/v1/unavailable-codes').then(setCustomCodes).catch(() => {})
  }, [])

  // ACW countdown timer — ticks every second while in ACW with an expiry
  useEffect(() => {
    if (agentState !== 'acw' || !agentStateExpiresAt) { setAcwCountdown(null); return }
    const update = () =>
      setAcwCountdown(Math.max(0, Math.ceil((agentStateExpiresAt.getTime() - Date.now()) / 1000)))
    update()
    const id = setInterval(update, 1000)
    return () => clearInterval(id)
  }, [agentState, agentStateExpiresAt])

  async function applyState(code: AgentStateCode, customCodeId?: string, customLabel?: string) {
    try {
      await api.put('/api/v1/agent-state', { code, customCodeId: customCodeId ?? null, customLabel: customLabel ?? null })
      setAgentStateCode(code)
    } catch {}
    setStateOpen(false)
  }
  const {
    callStatus, callerNumber, destinationNumber, isMuted, isOnHold,
    callStartedAt, campaignId, callRecordId,
    transferState, transferTarget, transferTargetLabel,
    setRinging, setDialing, setOnCall, setMuted, setOnHold,
    setCallRecordId, setCampaignId,
    setTransferDialing, setTransferConnected, setTransferConference, resetTransfer, reset,
  } = useCallStore()
  const addFlowSession = useFlowSessionsStore((s) => s.addSession)

  // Primary call session
  const sessionRef          = useRef<any>(null)
  // Consultation leg (warm transfer target)
  const transferSessionRef  = useRef<any>(null)
  // Flags the next newRTCSession as a consultation leg, not a new primary call
  const nextIsTransferRef   = useRef(false)
  // Audio: separate elements so primary and consultation audio never collide
  const remoteAudioRef      = useRef<HTMLAudioElement>(null)
  const consultAudioRef     = useRef<HTMLAudioElement>(null)
  // WebAudio context used for 3-way conference mixing
  const audioCtxRef         = useRef<AudioContext | null>(null)
  // Original mic send-track on the primary session; saved so we can restore it on end-conference
  const origMicTrackRef     = useRef<MediaStreamTrack | null>(null)

  const uaRef = useRef<InstanceType<typeof JsSIP.UA> | null>(null)
  // Set true when agent clicks Pick Up so the next incoming SIP INVITE is auto-answered.
  const autoAnswerBridgeRef = useRef(false)

  const [elapsed, setElapsed]                       = useState(0)
  const [dialInput, setDialInput]                   = useState('')
  const [transferNumbers, setTransferNumbers]       = useState<ClientTransferNumber[]>([])
  const [showTransfers, setShowTransfers]           = useState(false)
  const [pickingUp, setPickingUp]                   = useState(false)
  const [pickUpError, setPickUpError]               = useState<string | null>(null)

  // ── Timer ─────────────────────────────────────────────────────────────────
  useEffect(() => {
    if (callStatus !== 'on-call' || !callStartedAt) { setElapsed(0); return }
    const id = setInterval(() => setElapsed(Math.floor((Date.now() - callStartedAt) / 1000)), 1000)
    return () => clearInterval(id)
  }, [callStatus, callStartedAt])

  // Reset pick-up state when a new queued call arrives
  useEffect(() => {
    if (callStatus === 'queued') { setPickingUp(false); setPickUpError(null) }
  }, [callRecordId])

  // RingStrategy.AutoAnswerBestAgent — the server picked this agent with no click, so the ref
  // that normally gets set inside handlePickUp (below) must be armed here instead, the moment
  // the receiveAutoConnecting push arrives — ahead of the whisper/bridge INVITE that follows it.
  useEffect(() => {
    if (callStatus === 'auto-connecting') autoAnswerBridgeRef.current = true
  }, [callStatus, callRecordId])

  // ── Transfer numbers for this campaign ───────────────────────────────────
  useEffect(() => {
    if (!campaignId) { setTransferNumbers([]); return }
    getClientTransferNumbers(campaignId).then(setTransferNumbers).catch(() => {})
  }, [campaignId])

  // ── JsSIP UA ─────────────────────────────────────────────────────────────
  useEffect(() => {
    if (!sipExtension || !sipPassword || !tenantSubdomain) return

    const socket = new JsSIP.WebSocketInterface(SIP_WS_URL)
    // Cloudflare terminates TLS before FreeSWITCH, so FreeSWITCH receives a
    // plain WS connection on port 7080 regardless of the wss:// URL the browser
    // uses. Override via_transport so Via: SIP/2.0/WS matches the ws-binding
    // transport FreeSWITCH validates against.
    if (SIP_WS_URL.startsWith('wss://')) {
      Object.defineProperty(socket, 'via_transport', { get: () => 'WS', configurable: true })
    }
    const ua = new JsSIP.UA({
      sockets:          [socket],
      uri:              `sip:${sipExtension}@${tenantSubdomain}`,
      password:         sipPassword,
      register:         true,
      register_expires: 300,
      session_timers:   false,
    })

    // Track first registration so re-REGISTERs (every 300s) don't reset state.
    let firstRegistration = true
    ua.on('registered', () => {
      setRegistrationStatus('registered')
      if (firstRegistration) {
        firstRegistration = false
        setAgentStateCode('unavailable')
        api.put('/api/v1/agent-state', { code: 'unavailable', customCodeId: null, customLabel: null }).catch(() => {})
      }
    })
    ua.on('unregistered',       () => setRegistrationStatus('idle'))
    ua.on('registrationFailed', () => setRegistrationStatus('failed'))

    ua.on('newRTCSession', ({ session }: { session: any }) => {
      // Decide whether this is a consultation leg or a new primary call
      const isTransfer = nextIsTransferRef.current
      nextIsTransferRef.current = false

      // Route remote audio to the correct element
      const audioRef = isTransfer ? consultAudioRef : remoteAudioRef
      const wireAudio = (pc: RTCPeerConnection) => {
        pc.addEventListener('track', (e: RTCTrackEvent) => {
          if (audioRef.current && e.streams[0]) {
            audioRef.current.srcObject = e.streams[0]
            audioRef.current.play().catch(() => {})
          }
        })
      }
      session.on('peerconnection', ({ peerconnection }: { peerconnection: RTCPeerConnection }) => wireAudio(peerconnection))
      if ((session as any).connection) wireAudio((session as any).connection)

      if (isTransfer) {
        // ── Consultation leg ──────────────────────────────────────────────
        transferSessionRef.current = session

        session.on('accepted', async () => {
          setTransferConnected()
          try {
            await api.post('/api/v1/call-records/outbound', {
              dialedNumber: session.remote_identity?.uri?.user ?? null,
            })
          } catch {}
        })

        const cleanupConsult = async () => {
          transferSessionRef.current = null
          if (consultAudioRef.current) consultAudioRef.current.srcObject = null
          stopConferenceAudio()
          await restorePrimaryMic()
          resetTransfer()
          // Unhold primary automatically when consultation ends/fails (no-op if in conference)
          const primary = sessionRef.current
          if (primary) {
            try { primary.unhold() } catch {}
            setOnHold(false)
          }
        }

        session.on('ended',  cleanupConsult)
        session.on('failed', cleanupConsult)

      } else {
        // ── Primary leg ───────────────────────────────────────────────────
        sessionRef.current = session

        if (session.direction === 'incoming') {
          const num  = session.remote_identity?.uri?.user ?? 'Unknown'
          const name = session.remote_identity?.display_name ?? ''

          session.on('accepted', () => { setOnCall() })
          session.on('ended',    () => { sessionRef.current = null; reset() })
          session.on('failed',   () => { sessionRef.current = null; reset() })

          if (autoAnswerBridgeRef.current) {
            // This INVITE was triggered by our own Pick Up bridge request — answer immediately.
            autoAnswerBridgeRef.current = false
            try {
              session.answer({ mediaConstraints: { audio: true, video: false }, pcConfig: { iceServers: [] } })
            } catch {
              // ignore
            }
          } else {
            setRinging(num, name)
          }
        } else {
          // Outbound direct dial (idle → dialing)
          session.on('accepted', async () => {
            setOnCall()
            try {
              const rec = await api.post<{ id: string }>('/api/v1/call-records/outbound', {
                dialedNumber: session.remote_identity?.uri?.user ?? null,
              })
              setCallRecordId(rec.id)
            } catch {}
          })
          session.on('ended',  () => { sessionRef.current = null; reset() })
          session.on('failed', () => { sessionRef.current = null; reset() })
        }
      }
    })

    setRegistrationStatus('registering')
    ua.start()
    uaRef.current = ua

    return () => { ua.stop(); uaRef.current = null }
  }, [sipExtension, sipPassword, tenantSubdomain]) // eslint-disable-line react-hooks/exhaustive-deps

  // ── Conference audio helpers ──────────────────────────────────────────────

  const stopConferenceAudio = () => {
    audioCtxRef.current?.close()
    audioCtxRef.current = null
  }

  const restorePrimaryMic = async () => {
    const track = origMicTrackRef.current
    if (!track) return
    origMicTrackRef.current = null
    const sender = (sessionRef.current?.connection as RTCPeerConnection | undefined)
      ?.getSenders().find((s) => s.track?.kind === 'audio')
    if (sender) {
      try { await sender.replaceTrack(track) } catch {}
    }
  }

  // ── Call actions ──────────────────────────────────────────────────────────

  const handleAnswer = async () => {
    const session = sessionRef.current
    if (!session) return
    // Skip creating a new CallRecord when the DID-routing screen pop already created one
    if (!callRecordId) {
      try {
        const calledNumber = session.local_identity?.uri?.user ?? null
        const rec = await api.post<{ id: string; campaignId?: string }>('/api/v1/call-records/inbound', {
          callerNumber, callerName: null, channelUuid: null, calledNumber,
        })
        setCallRecordId(rec.id)
        if (rec.campaignId) setCampaignId(rec.campaignId)
      } catch {}
    }
    session.answer({ mediaConstraints: { audio: true, video: false }, pcConfig: { iceServers: [] } })
  }

  // Pick up a queued (screen-popped) call: bridge the parked FreeSWITCH channel to this agent's SIP extension.
  // FreeSWITCH then sends a SIP INVITE → JsSIP fires newRTCSession (incoming) → softphone auto-answers.
  // If the telephony flow has a tf_script_pop node, the API returns the initial CRM flow node state.
  const handlePickUp = async () => {
    if (!callRecordId) {
      setPickUpError('No call record ID — cannot bridge.')
      return
    }
    setPickingUp(true)
    setPickUpError(null)
    autoAnswerBridgeRef.current = true
    try {
      await api.post<void>('/api/v1/telephony/answer-queued-call', { callRecordId })
      setPickingUp(false)
      // Script pop (if any) arrives via SignalR ReceiveScriptPop after CHANNEL_BRIDGE → agent_answer
    } catch (e) {
      autoAnswerBridgeRef.current = false
      setPickingUp(false)
      setPickUpError(e instanceof Error ? e.message : 'Bridge request failed.')
    }
  }

  const handleReject = () => {
    sessionRef.current?.terminate()
    sessionRef.current = null
    reset()
  }

  const handleHangUp = () => {
    stopConferenceAudio()
    origMicTrackRef.current = null
    if (transferSessionRef.current) {
      try { transferSessionRef.current.terminate() } catch {}
      transferSessionRef.current = null
      if (consultAudioRef.current) consultAudioRef.current.srcObject = null
    }

    const primary = sessionRef.current
    try { primary?.terminate() } catch {}

    sessionRef.current = null
    reset()
  }

  const handleMute = () => {
    const session = sessionRef.current
    if (!session) return
    if (isMuted) { session.unmute({ audio: true }); setMuted(false) }
    else         { session.mute({ audio: true });   setMuted(true)  }
  }

  const handleHoldToggle = () => {
    const session = sessionRef.current
    if (!session) return
    if (isOnHold) {
      try { session.unhold() } catch {}
      setOnHold(false)
    } else {
      try { session.hold() } catch {}
      setOnHold(true)
    }
  }

  // Dial a transfer number: hold primary → call consultation leg
  const handleInitiateTransfer = (t: ClientTransferNumber) => {
    const ua      = uaRef.current
    const primary = sessionRef.current
    if (!ua || !tenantSubdomain || !primary) return

    // Hold the primary call so the inbound caller hears hold music
    try { primary.hold() } catch {}
    setOnHold(true)

    // Route the next newRTCSession to transferSessionRef
    nextIsTransferRef.current = true
    setTransferDialing(t.number, t.label)
    setShowTransfers(false)

    try {
      ua.call(`sip:${t.number}@${tenantSubdomain}`, {
        mediaConstraints: { audio: true, video: false },
        pcConfig: { iceServers: [] },
      })
    } catch {
      // Dial failed immediately — undo hold and reset
      nextIsTransferRef.current = false
      resetTransfer()
      try { primary.unhold() } catch {}
      setOnHold(false)
    }

    // Auto-open the CRM script tab if this transfer number has one configured
    if (t.campaignFlowId) {
      const label = t.campaignName ? `${t.campaignName} Script` : 'Transfer Script'
      flowsApi.startSession({ flowId: t.campaignFlowId, callRecordId: callRecordId ?? undefined })
        .then((node) => addFlowSession({ id: node.sessionId, label, sessionId: node.sessionId, initialNode: node }))
        .catch(console.error)
    }
  }

  // Complete warm transfer / leave conference: attended REFER → FreeSWITCH bridges caller + target
  const handleCompleteTransfer = () => {
    const primary = sessionRef.current
    const consult = transferSessionRef.current
    if (!primary || !consult) return

    stopConferenceAudio()
    origMicTrackRef.current = null

    try {
      // Attended SIP REFER with Replaces — bridges original caller with the consultation party
      primary.refer(consult)
    } catch {
      // Fallback: blind transfer to the number
      try { primary.refer(`sip:${transferTarget}@${tenantSubdomain}`) } catch {}
    }

    try { consult.terminate() } catch {}
    transferSessionRef.current = null
    if (consultAudioRef.current) consultAudioRef.current.srcObject = null
    sessionRef.current = null
    reset()
  }

  // Cancel transfer / end conference: hang up consultation, restore primary to active
  const handleCancelTransfer = async () => {
    const primary = sessionRef.current
    const consult = transferSessionRef.current

    stopConferenceAudio()
    await restorePrimaryMic()

    if (consult) {
      try { consult.terminate() } catch {}
      transferSessionRef.current = null
      if (consultAudioRef.current) consultAudioRef.current.srcObject = null
    }

    // Only unhold if the primary was on hold (not when coming from conference state)
    if (isOnHold && primary) {
      try { primary.unhold() } catch {}
      setOnHold(false)
    }

    resetTransfer()
    setShowTransfers(true)
  }

  // Enter 3-way conference: unhold primary and set up WebAudio mixing so all three parties hear each other
  const handleConference = async () => {
    const primary = sessionRef.current
    const consult = transferSessionRef.current
    if (!primary || !consult) return

    // Unhold primary — inbound caller rejoins the audio
    try { primary.unhold() } catch {}
    setTransferConference()

    try {
      const ctx = new AudioContext()
      audioCtxRef.current = ctx

      const callerStream = remoteAudioRef.current?.srcObject as MediaStream | null
      const consultStream = consultAudioRef.current?.srcObject as MediaStream | null
      if (!callerStream || !consultStream) return

      // Remote audio sources
      const callerSrc  = ctx.createMediaStreamSource(callerStream)
      const consultSrc = ctx.createMediaStreamSource(consultStream)

      // Agent's mic — captured fresh so we have a clean MediaStreamTrack to mix
      const micStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false })
      const micSrc    = ctx.createMediaStreamSource(micStream)

      // What the inbound caller hears: agent mic + transfer-target voice
      const toCallerDest  = ctx.createMediaStreamDestination()
      micSrc.connect(toCallerDest)
      consultSrc.connect(toCallerDest)

      // What the transfer target hears: agent mic + caller voice
      const toConsultDest = ctx.createMediaStreamDestination()
      micSrc.connect(toConsultDest)
      callerSrc.connect(toConsultDest)

      // Save the original mic track so we can restore it if the agent ends conference early
      const primaryConn = primary.connection as RTCPeerConnection | undefined
      const primarySender = primaryConn?.getSenders().find((s) => s.track?.kind === 'audio')
      if (primarySender?.track) origMicTrackRef.current = primarySender.track

      // Replace send tracks so the mixing takes effect
      if (primarySender)
        await primarySender.replaceTrack(toCallerDest.stream.getAudioTracks()[0])

      const consultConn   = consult.connection as RTCPeerConnection | undefined
      const consultSender = consultConn?.getSenders().find((s) => s.track?.kind === 'audio')
      if (consultSender)
        await consultSender.replaceTrack(toConsultDest.stream.getAudioTracks()[0])

    } catch {
      // Mixing failed — still in conference state but audio is relay-mode (agent hears both, both hear agent)
    }
  }

  // Manual outbound dial from idle state
  const handleDial = () => {
    const ua = uaRef.current
    const number = dialInput.trim()
    if (!ua || !tenantSubdomain || !number) return
    setDialing(number)
    setDialInput('')
    try {
      ua.call(`sip:${number}@${tenantSubdomain}`, {
        mediaConstraints: { audio: true, video: false },
        pcConfig: { iceServers: [] },
      })
    } catch { reset() }
  }

  const handleDialKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') handleDial()
  }

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col h-full p-3 gap-3">
      {/* Audio: two separate elements so primary and consultation never share a stream */}
      <audio ref={remoteAudioRef} autoPlay playsInline className="hidden" />
      <audio ref={consultAudioRef} autoPlay playsInline className="hidden" />

      {/* Header */}
      <div className="flex items-center gap-2 pt-1">
        <svg className="w-4 h-4 text-gray-400 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
            d="M2.25 6.75c0 8.284 6.716 15 15 15h2.25a2.25 2.25 0 002.25-2.25v-1.372c0-.516-.351-.966-.852-1.091l-4.423-1.106c-.44-.11-.902.055-1.173.417l-.97 1.293c-.282.376-.769.542-1.21.38a12.035 12.035 0 01-7.143-7.143c-.162-.441.004-.928.38-1.21l1.293-.97c.363-.271.527-.734.417-1.173L6.963 3.102a1.125 1.125 0 00-1.091-.852H4.5A2.25 2.25 0 002.25 4.5v2.25z" />
        </svg>
        <span className="text-xs font-medium text-gray-300 uppercase tracking-wide">Softphone</span>
      </div>

      {/* Registration status */}
      {callStatus === 'idle' && (
        <div className="flex items-center gap-2 px-1">
          <span className={`w-2 h-2 rounded-full shrink-0 ${REG_COLOR[registrationStatus]}`} />
          <span className="text-xs text-gray-400">{REG_LABEL[registrationStatus]}</span>
        </div>
      )}

      {/* Extension badge */}
      {sipExtension && callStatus === 'idle' && (
        <div className="bg-gray-800 rounded-lg px-3 py-2 text-center">
          <p className="text-gray-500 text-xs mb-1">Extension</p>
          <p className="text-white font-mono text-lg tracking-widest">{sipExtension}</p>
        </div>
      )}

      {/* Agent state dropdown */}
      {registrationStatus === 'registered' && callStatus === 'idle' && (() => {
        const STATE_META: Record<string, { label: string; dot: string; text: string }> = {
          unavailable:       { label: 'Unavailable',        dot: 'bg-gray-500',   text: 'text-gray-400' },
          available:         { label: 'Available',          dot: 'bg-green-500',  text: 'text-green-400' },
          unavailable_break: { label: 'Unavailable - Break', dot: 'bg-amber-700', text: 'text-amber-600' },
          unavailable_lunch: { label: 'Unavailable - Lunch', dot: 'bg-amber-700', text: 'text-amber-600' },
          on_call:           { label: 'On Call',             dot: 'bg-blue-500',  text: 'text-blue-400'  },
          acw:               { label: 'After Call Work',     dot: 'bg-purple-500', text: 'text-purple-400' },
        }
        const current = STATE_META[agentState] ?? { label: customCodes.find(c => c.id === agentState)?.name ?? 'Unavailable', dot: 'bg-orange-500', text: 'text-orange-400' }
        const currentLabel = agentState === 'acw' && acwCountdown !== null
          ? `${current.label} (${acwCountdown}s)`
          : current.label

        return (
          <div className="relative">
            <button
              onClick={() => setStateOpen((v) => !v)}
              className="w-full flex items-center gap-2 bg-gray-800 hover:bg-gray-700 rounded-lg px-3 py-2 transition-colors"
            >
              <span className={`w-2 h-2 rounded-full shrink-0 ${current.dot}`} />
              <span className={`text-xs font-medium flex-1 text-left ${current.text}`}>{currentLabel}</span>
              <svg className={`w-3 h-3 text-gray-500 transition-transform ${stateOpen ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
              </svg>
            </button>

            {stateOpen && (
              <div className="absolute left-0 right-0 top-full mt-1 bg-gray-800 border border-gray-700 rounded-lg overflow-hidden z-20 shadow-xl">
                {/* Built-in selectable states */}
                {[
                  { code: 'available',         label: 'Available',          dot: 'bg-green-500',  text: 'text-green-400' },
                  { code: 'unavailable_break', label: 'Unavailable - Break', dot: 'bg-amber-700', text: 'text-amber-600' },
                  { code: 'unavailable_lunch', label: 'Unavailable - Lunch', dot: 'bg-amber-700', text: 'text-amber-600' },
                ].map((opt) => (
                  <button
                    key={opt.code}
                    onClick={() => applyState(opt.code)}
                    className={`w-full flex items-center gap-2 px-3 py-2 text-xs hover:bg-gray-700 transition-colors text-left ${agentState === opt.code ? 'bg-gray-700/60' : ''}`}
                  >
                    <span className={`w-2 h-2 rounded-full shrink-0 ${opt.dot}`} />
                    <span className={opt.text}>{opt.label}</span>
                  </button>
                ))}

                {/* Custom codes for this role */}
                {customCodes.length > 0 && (
                  <>
                    <div className="border-t border-gray-700 mx-2 my-0.5" />
                    {customCodes.map((c) => (
                      <button
                        key={c.id}
                        onClick={() => applyState(c.id, c.id, c.name)}
                        className={`w-full flex items-center gap-2 px-3 py-2 text-xs hover:bg-gray-700 transition-colors text-left ${agentState === c.id ? 'bg-gray-700/60' : ''}`}
                      >
                        <span className="w-2 h-2 rounded-full shrink-0 bg-orange-500" />
                        <span className="text-orange-400">{c.name}</span>
                      </button>
                    ))}
                  </>
                )}
              </div>
            )}
          </div>
        )
      })()}

      {/* ── QUEUED (screen pop — call parked on FreeSWITCH, waiting for agent to pick up) ── */}
      {callStatus === 'queued' && (
        <div className="flex flex-col gap-3 flex-1">
          <div className="flex justify-center pt-2">
            <div className="relative flex items-center justify-center">
              <span className="absolute w-12 h-12 rounded-full bg-amber-500 opacity-30 animate-ping" />
              <span className="relative w-10 h-10 rounded-full bg-amber-600 flex items-center justify-center">
                <PhoneIcon className="w-5 h-5 text-white" />
              </span>
            </div>
          </div>
          <div className="text-center">
            <p className="text-xs text-amber-400 font-medium mb-1">Inbound queue call</p>
            <p className="text-white font-semibold text-sm truncate">{callerNumber ?? 'Unknown'}</p>
            {destinationNumber && <p className="text-gray-400 text-xs font-mono truncate">→ {destinationNumber}</p>}
          </div>
          <div className="flex gap-3 justify-center mt-1">
            <button
              onClick={reset}
              className="w-12 h-12 rounded-full bg-gray-700 hover:bg-gray-600 flex items-center justify-center transition-colors"
              title="Dismiss"
            >
              <PhoneIcon className="w-5 h-5 text-white rotate-135" />
            </button>
            <button
              onClick={handlePickUp}
              disabled={pickingUp || registrationStatus !== 'registered'}
              className="w-12 h-12 rounded-full bg-green-600 hover:bg-green-500 disabled:opacity-60 flex items-center justify-center transition-colors"
              title={registrationStatus !== 'registered' ? 'Softphone not registered — waiting…' : 'Pick up'}
            >
              {pickingUp
                ? <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
                : registrationStatus === 'registering'
                  ? <span className="w-3 h-3 border-2 border-white border-t-transparent rounded-full animate-spin" />
                  : <PhoneIcon className="w-5 h-5 text-white" />}
            </button>
          </div>
          {registrationStatus !== 'registered' && !pickingUp && (
            <p className="text-center text-xs text-yellow-500">Softphone {registrationStatus === 'registering' ? 'registering…' : 'not registered'}</p>
          )}
          {pickUpError && <p className="text-center text-xs text-red-400">{pickUpError}</p>}
        </div>
      )}

      {/* ── AUTO-CONNECTING (RingStrategy.AutoAnswerBestAgent — server picked this agent, no
           click needed; the whisper/bridge INVITE auto-answers itself moments after this
           renders, per the useEffect above that arms autoAnswerBridgeRef) ── */}
      {callStatus === 'auto-connecting' && (
        <div className="flex flex-col gap-3 flex-1">
          <div className="flex justify-center pt-2">
            <div className="relative flex items-center justify-center">
              <span className="absolute w-12 h-12 rounded-full bg-indigo-500 opacity-30 animate-ping" />
              <span className="relative w-10 h-10 rounded-full bg-indigo-600 flex items-center justify-center">
                <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
              </span>
            </div>
          </div>
          <div className="text-center">
            <p className="text-xs text-indigo-400 font-medium mb-1">Connecting you…</p>
            <p className="text-white font-semibold text-sm truncate">{callerNumber ?? 'Unknown'}</p>
            {destinationNumber && <p className="text-gray-400 text-xs font-mono truncate">→ {destinationNumber}</p>}
          </div>
        </div>
      )}

      {/* ── RINGING ── */}
      {callStatus === 'ringing' && (
        <div className="flex flex-col gap-3 flex-1">
          <div className="flex justify-center pt-2">
            <div className="relative flex items-center justify-center">
              <span className="absolute w-12 h-12 rounded-full bg-green-500 opacity-30 animate-ping" />
              <span className="relative w-10 h-10 rounded-full bg-green-600 flex items-center justify-center">
                <PhoneIcon className="w-5 h-5 text-white" />
              </span>
            </div>
          </div>
          <div className="text-center">
            <p className="text-xs text-gray-500 mb-1">Incoming call</p>
            <p className="text-white font-semibold text-sm truncate">{callerNumber ?? 'Unknown'}</p>
            {destinationNumber && <p className="text-gray-400 text-xs font-mono truncate">→ {destinationNumber}</p>}
          </div>
          <div className="flex gap-3 justify-center mt-1">
            <button
              onClick={handleReject}
              className="w-12 h-12 rounded-full bg-red-600 hover:bg-red-500 flex items-center justify-center transition-colors"
              title="Reject"
            >
              <PhoneIcon className="w-5 h-5 text-white rotate-135" />
            </button>
            <button
              onClick={handleAnswer}
              className="w-12 h-12 rounded-full bg-green-600 hover:bg-green-500 flex items-center justify-center transition-colors"
              title="Answer"
            >
              <PhoneIcon className="w-5 h-5 text-white" />
            </button>
          </div>
        </div>
      )}

      {/* ── DIALING (idle outbound) ── */}
      {callStatus === 'dialing' && (
        <div className="flex flex-col gap-3 flex-1">
          <div className="flex justify-center pt-2">
            <div className="relative flex items-center justify-center">
              <span className="absolute w-12 h-12 rounded-full bg-blue-500 opacity-30 animate-ping" />
              <span className="relative w-10 h-10 rounded-full bg-blue-600 flex items-center justify-center">
                <PhoneIcon className="w-5 h-5 text-white" />
              </span>
            </div>
          </div>
          <div className="text-center">
            <p className="text-xs text-gray-500 mb-1">Calling…</p>
            <p className="text-white font-semibold text-sm truncate">{callerNumber ?? 'Unknown'}</p>
          </div>
          <div className="flex justify-center mt-1">
            <button
              onClick={handleHangUp}
              className="w-12 h-12 rounded-full bg-red-600 hover:bg-red-500 flex items-center justify-center transition-colors"
              title="Cancel"
            >
              <PhoneIcon className="w-5 h-5 text-white rotate-135" />
            </button>
          </div>
        </div>
      )}

      {/* ── ON CALL ── */}
      {callStatus === 'on-call' && (
        <div className="flex flex-col gap-3 flex-1 min-h-0">

          {/* Primary call status card */}
          <div className={`border rounded-lg px-3 py-2 text-center transition-colors ${
            isOnHold ? 'bg-amber-950 border-amber-700' : 'bg-green-950 border-green-800'
          }`}>
            <p className={`text-xs font-medium mb-1 ${isOnHold ? 'text-amber-400' : 'text-green-400'}`}>
              {isOnHold ? 'On hold' : 'On call'}
            </p>
            <p className="text-white font-mono text-xl tracking-widest">{formatElapsed(elapsed)}</p>
          </div>

          {/* Caller identity */}
          <div className="text-center">
            <p className="text-white text-sm font-semibold truncate">{callerNumber ?? 'Unknown'}</p>
            {destinationNumber && <p className="text-gray-400 text-xs font-mono truncate">→ {destinationNumber}</p>}
          </div>

          {/* ── No transfer in progress: normal controls + transfer list ── */}
          {transferState === 'idle' && (
            <>
              <div className="flex gap-2 justify-center">
                {/* Mute */}
                <button
                  onClick={handleMute}
                  className={`w-11 h-11 rounded-full flex items-center justify-center transition-colors ${
                    isMuted ? 'bg-yellow-600 hover:bg-yellow-500' : 'bg-gray-700 hover:bg-gray-600'
                  }`}
                  title={isMuted ? 'Unmute' : 'Mute'}
                >
                  {isMuted ? <MicOffIcon className="w-5 h-5 text-white" /> : <MicIcon className="w-5 h-5 text-white" />}
                </button>

                {/* Hold / Resume */}
                <button
                  onClick={handleHoldToggle}
                  className={`w-11 h-11 rounded-full flex items-center justify-center transition-colors ${
                    isOnHold ? 'bg-amber-600 hover:bg-amber-500' : 'bg-gray-700 hover:bg-gray-600'
                  }`}
                  title={isOnHold ? 'Resume' : 'Hold'}
                >
                  {isOnHold ? <PlayIcon className="w-5 h-5 text-white" /> : <PauseIcon className="w-5 h-5 text-white" />}
                </button>

                {/* Hang up */}
                <button
                  onClick={handleHangUp}
                  className="w-11 h-11 rounded-full bg-red-600 hover:bg-red-500 flex items-center justify-center transition-colors"
                  title="Hang up"
                >
                  <PhoneIcon className="w-5 h-5 text-white rotate-135" />
                </button>
              </div>

              {/* Transfer numbers */}
              {transferNumbers.length > 0 && (
                <div className="border border-gray-700 rounded-lg overflow-hidden">
                  <button
                    onClick={() => setShowTransfers(v => !v)}
                    className="w-full flex items-center justify-between px-3 py-2 bg-gray-800 text-left"
                  >
                    <span className="text-xs font-medium text-gray-300">Transfer Numbers</span>
                    <svg
                      className={`w-3 h-3 text-gray-500 transition-transform ${showTransfers ? 'rotate-180' : ''}`}
                      fill="none" stroke="currentColor" viewBox="0 0 24 24"
                    >
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                    </svg>
                  </button>
                  {showTransfers && (
                    <div className="divide-y divide-gray-700/50 overflow-y-auto max-h-48">
                      {transferNumbers.map(t => (
                        <div key={t.id} className="flex items-center gap-2 px-3 py-2 bg-gray-900">
                          <div className="flex-1 min-w-0">
                            <p className="text-xs font-medium text-gray-200 truncate">{t.label}</p>
                            <p className="text-xs text-gray-500 font-mono">{t.number}</p>
                          </div>
                          <button
                            onClick={() => handleInitiateTransfer(t)}
                            className="shrink-0 px-2 py-1 rounded bg-indigo-700 hover:bg-indigo-600 text-xs text-white transition-colors"
                            title={`Warm transfer to ${t.number}`}
                          >
                            Dial
                          </button>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}
            </>
          )}

          {/* ── Transfer: consultation leg dialing ── */}
          {transferState === 'dialing' && (
            <div className="flex flex-col gap-2">
              <div className="bg-blue-950 border border-blue-800 rounded-lg px-3 py-2">
                <p className="text-blue-400 text-xs font-medium mb-1">Consulting…</p>
                <p className="text-white text-sm font-semibold truncate">{transferTargetLabel ?? transferTarget}</p>
                <p className="text-gray-500 text-xs font-mono">{transferTarget}</p>
              </div>

              <div className="bg-gray-800/60 rounded-lg px-3 py-1.5 text-center">
                <p className="text-amber-400 text-xs">Inbound caller on hold</p>
              </div>

              <button
                onClick={handleCancelTransfer}
                className="w-full py-2 rounded-lg bg-gray-700 hover:bg-gray-600 text-xs text-gray-200 font-medium transition-colors"
              >
                Cancel Transfer
              </button>
            </div>
          )}

          {/* ── Transfer: consultation connected — ready to bridge or conference ── */}
          {transferState === 'connected' && (
            <div className="flex flex-col gap-2">
              <div className="bg-indigo-950 border border-indigo-700 rounded-lg px-3 py-2">
                <p className="text-indigo-400 text-xs font-medium mb-1">Connected</p>
                <p className="text-white text-sm font-semibold truncate">{transferTargetLabel ?? transferTarget}</p>
                <p className="text-gray-500 text-xs font-mono">{transferTarget}</p>
              </div>

              <div className="bg-gray-800/60 rounded-lg px-3 py-1.5 text-center">
                <p className="text-amber-400 text-xs">Inbound caller on hold</p>
              </div>

              {/* Primary action row */}
              <div className="flex gap-2">
                <button
                  onClick={handleCompleteTransfer}
                  className="flex-1 py-2 rounded-lg bg-green-700 hover:bg-green-600 text-xs text-white font-semibold transition-colors"
                  title="Bridge caller and transfer target; drop agent"
                >
                  Transfer
                </button>
                <button
                  onClick={handleConference}
                  className="flex-1 py-2 rounded-lg bg-purple-700 hover:bg-purple-600 text-xs text-white font-semibold transition-colors"
                  title="Join all three parties in a conference call"
                >
                  Conference
                </button>
              </div>

              <button
                onClick={handleCancelTransfer}
                className="w-full py-1.5 rounded-lg bg-gray-700 hover:bg-gray-600 text-xs text-gray-400 hover:text-gray-200 transition-colors"
              >
                Cancel
              </button>
            </div>
          )}

          {/* ── In conference: all 3 parties bridged ── */}
          {transferState === 'conference' && (
            <div className="flex flex-col gap-2">
              <div className="bg-purple-950 border border-purple-700 rounded-lg px-3 py-2">
                <p className="text-purple-400 text-xs font-medium mb-2">In Conference</p>
                <div className="space-y-1.5">
                  <div className="flex items-center gap-2">
                    <span className="w-1.5 h-1.5 rounded-full bg-green-400 shrink-0 animate-pulse" />
                    <p className="text-xs text-gray-200 truncate">{callerNumber ?? 'Inbound caller'}</p>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="w-1.5 h-1.5 rounded-full bg-indigo-400 shrink-0 animate-pulse" />
                    <p className="text-xs text-gray-200 truncate">{transferTargetLabel ?? transferTarget}</p>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="w-1.5 h-1.5 rounded-full bg-purple-400 shrink-0 animate-pulse" />
                    <p className="text-xs text-gray-400">You (agent)</p>
                  </div>
                </div>
              </div>

              <div className="flex gap-2">
                <button
                  onClick={handleCompleteTransfer}
                  className="flex-1 py-2 rounded-lg bg-green-700 hover:bg-green-600 text-xs text-white font-semibold transition-colors"
                  title="Bridge caller and transfer target; agent leaves the call"
                >
                  Transfer & Leave
                </button>
                <button
                  onClick={handleCancelTransfer}
                  className="flex-1 py-2 rounded-lg bg-gray-700 hover:bg-gray-600 text-xs text-gray-200 transition-colors"
                  title="Hang up the transfer target; return to 1-on-1 with caller"
                >
                  End Conference
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* ── IDLE: dialpad ── */}
      {callStatus === 'idle' && sipExtension && registrationStatus === 'registered' && (
        <div className="flex flex-col gap-2">
          <div className="flex gap-1">
            <input
              type="tel"
              value={dialInput}
              onChange={(e) => setDialInput(e.target.value)}
              onKeyDown={handleDialKeyDown}
              placeholder="Number to dial"
              className="flex-1 min-w-0 bg-gray-800 text-white text-sm rounded-lg px-3 py-2 placeholder-gray-600 focus:outline-none focus:ring-1 focus:ring-blue-500"
            />
            <button
              onClick={handleDial}
              disabled={!dialInput.trim()}
              className="w-10 h-10 rounded-lg bg-blue-600 hover:bg-blue-500 disabled:bg-gray-700 flex items-center justify-center transition-colors shrink-0"
              title="Call"
            >
              <PhoneIcon className="w-4 h-4 text-white" />
            </button>
          </div>
          <p className="text-gray-700 text-xs text-center pt-1">Waiting for calls…</p>
        </div>
      )}

      {/* No SIP credentials */}
      {!sipExtension && (
        <div className="flex-1 flex items-center justify-center">
          <p className="text-gray-600 text-xs text-center">Log in to activate softphone</p>
        </div>
      )}

      {/* Registration failed */}
      {callStatus === 'idle' && registrationStatus === 'failed' && (
        <div className="bg-red-950 border border-red-800 rounded-lg px-3 py-2">
          <p className="text-red-400 text-xs">Could not reach FreeSWITCH.</p>
          <p className="text-red-600 text-xs mt-1">Check that the softphone server is running.</p>
        </div>
      )}
    </div>
  )
}
