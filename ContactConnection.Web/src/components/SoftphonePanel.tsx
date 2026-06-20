import { useEffect, useRef, useState } from 'react'
import JsSIP from 'jssip'
import { useSipStore, type SipRegistrationStatus } from '../stores/sipStore'
import { useCallStore } from '../stores/callStore'
import { useAuthStore } from '../stores/authStore'
import { api } from '../api/client'

const SIP_WS_URL = import.meta.env.VITE_SIP_WS_URL as string ?? 'ws://localhost:7080'

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

export default function SoftphonePanel() {
  const { sipExtension, sipPassword, registrationStatus, setRegistrationStatus } = useSipStore()
  const tenantSubdomain = useAuthStore((s) => s.tenantSubdomain)
  const {
    callStatus, callerNumber, callerName, isMuted, callStartedAt,
    setRinging, setOnCall, setMuted, setCallRecordId, reset,
  } = useCallStore()

  const uaRef          = useRef<InstanceType<typeof JsSIP.UA> | null>(null)
  const sessionRef     = useRef<any>(null)
  const remoteAudioRef = useRef<HTMLAudioElement>(null)

  const [elapsed, setElapsed] = useState(0)

  // Call timer — ticks every second while on a call
  useEffect(() => {
    if (callStatus !== 'on-call' || !callStartedAt) { setElapsed(0); return }
    const id = setInterval(() => setElapsed(Math.floor((Date.now() - callStartedAt) / 1000)), 1000)
    return () => clearInterval(id)
  }, [callStatus, callStartedAt])

  // JsSIP UA — register and listen for incoming calls
  useEffect(() => {
    if (!sipExtension || !sipPassword || !tenantSubdomain) return

    const socket = new JsSIP.WebSocketInterface(SIP_WS_URL)
    const ua = new JsSIP.UA({
      sockets:          [socket],
      uri:              `sip:${sipExtension}@${tenantSubdomain}`,
      password:         sipPassword,
      register:         true,
      register_expires: 300,
    })

    ua.on('registered',         () => setRegistrationStatus('registered'))
    ua.on('unregistered',       () => setRegistrationStatus('idle'))
    ua.on('registrationFailed', () => setRegistrationStatus('failed'))

    ua.on('newRTCSession', ({ session }: { session: any }) => {
      if (session.direction !== 'incoming') return

      const num  = session.remote_identity?.uri?.user ?? 'Unknown'
      const name = session.remote_identity?.display_name ?? ''
      setRinging(num, name)
      sessionRef.current = session

      // Wire remote audio before answer so the track listener is ready
      session.on('peerconnection', ({ peerconnection }: { peerconnection: RTCPeerConnection }) => {
        peerconnection.addEventListener('track', (e: RTCTrackEvent) => {
          if (remoteAudioRef.current && e.streams[0]) {
            remoteAudioRef.current.srcObject = e.streams[0]
          }
        })
      })

      session.on('accepted', () => setOnCall())
      session.on('ended',    () => { sessionRef.current = null; reset() })
      session.on('failed',   () => { sessionRef.current = null; reset() })
    })

    setRegistrationStatus('registering')
    ua.start()
    uaRef.current = ua

    return () => { ua.stop(); uaRef.current = null }
  }, [sipExtension, sipPassword, tenantSubdomain]) // eslint-disable-line react-hooks/exhaustive-deps

  const handleAnswer = async () => {
    const session = sessionRef.current
    if (!session) return

    // Create call record so the center panel can screen-pop
    try {
      const rec = await api.post<{ id: string }>('/api/v1/call-records/inbound', {
        callerNumber,
        callerName,
        channelUuid: null,
      })
      setCallRecordId(rec.id)
    } catch { /* don't block the answer if record creation fails */ }

    session.answer({ mediaConstraints: { audio: true, video: false } })
  }

  const handleReject = () => {
    sessionRef.current?.terminate()
    sessionRef.current = null
    reset()
  }

  const handleHangUp = () => {
    sessionRef.current?.terminate()
    sessionRef.current = null
    reset()
  }

  const handleMute = () => {
    const session = sessionRef.current
    if (!session) return
    if (isMuted) {
      session.unmute({ audio: true })
      setMuted(false)
    } else {
      session.mute({ audio: true })
      setMuted(true)
    }
  }

  return (
    <div className="flex flex-col h-full p-3 gap-3">
      {/* Hidden audio element for remote stream */}
      <audio ref={remoteAudioRef} autoPlay playsInline className="hidden" />

      {/* Header */}
      <div className="flex items-center gap-2 pt-1">
        <svg className="w-4 h-4 text-gray-400 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
            d="M2.25 6.75c0 8.284 6.716 15 15 15h2.25a2.25 2.25 0 002.25-2.25v-1.372c0-.516-.351-.966-.852-1.091l-4.423-1.106c-.44-.11-.902.055-1.173.417l-.97 1.293c-.282.376-.769.542-1.21.38a12.035 12.035 0 01-7.143-7.143c-.162-.441.004-.928.38-1.21l1.293-.97c.363-.271.527-.734.417-1.173L6.963 3.102a1.125 1.125 0 00-1.091-.852H4.5A2.25 2.25 0 002.25 4.5v2.25z" />
        </svg>
        <span className="text-xs font-medium text-gray-300 uppercase tracking-wide">Softphone</span>
      </div>

      {/* Registration status (shown when idle) */}
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

      {/* ── RINGING ── */}
      {callStatus === 'ringing' && (
        <div className="flex flex-col gap-3 flex-1">
          {/* Pulsing ring indicator */}
          <div className="flex justify-center pt-2">
            <div className="relative flex items-center justify-center">
              <span className="absolute w-12 h-12 rounded-full bg-green-500 opacity-30 animate-ping" />
              <span className="relative w-10 h-10 rounded-full bg-green-600 flex items-center justify-center">
                <svg className="w-5 h-5 text-white" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M6.62 10.79a15.05 15.05 0 006.59 6.59l2.2-2.2a1 1 0 011.01-.24 11.48 11.48 0 003.6.57 1 1 0 011 1V21a1 1 0 01-1 1A17 17 0 013 5a1 1 0 011-1h3.5a1 1 0 011 1 11.48 11.48 0 00.57 3.6 1 1 0 01-.25 1.01l-2.2 2.18z"/>
                </svg>
              </span>
            </div>
          </div>

          <div className="text-center">
            <p className="text-xs text-gray-500 mb-1">Incoming call</p>
            <p className="text-white font-semibold text-sm truncate">{callerNumber ?? 'Unknown'}</p>
            {callerName && <p className="text-gray-400 text-xs truncate">{callerName}</p>}
          </div>

          {/* Answer / Reject */}
          <div className="flex gap-3 justify-center mt-1">
            <button
              onClick={handleReject}
              className="w-12 h-12 rounded-full bg-red-600 hover:bg-red-500 flex items-center justify-center transition-colors"
              title="Reject"
            >
              <svg className="w-5 h-5 text-white rotate-135" fill="currentColor" viewBox="0 0 24 24">
                <path d="M6.62 10.79a15.05 15.05 0 006.59 6.59l2.2-2.2a1 1 0 011.01-.24 11.48 11.48 0 003.6.57 1 1 0 011 1V21a1 1 0 01-1 1A17 17 0 013 5a1 1 0 011-1h3.5a1 1 0 011 1 11.48 11.48 0 00.57 3.6 1 1 0 01-.25 1.01l-2.2 2.18z"/>
              </svg>
            </button>
            <button
              onClick={handleAnswer}
              className="w-12 h-12 rounded-full bg-green-600 hover:bg-green-500 flex items-center justify-center transition-colors"
              title="Answer"
            >
              <svg className="w-5 h-5 text-white" fill="currentColor" viewBox="0 0 24 24">
                <path d="M6.62 10.79a15.05 15.05 0 006.59 6.59l2.2-2.2a1 1 0 011.01-.24 11.48 11.48 0 003.6.57 1 1 0 011 1V21a1 1 0 01-1 1A17 17 0 013 5a1 1 0 011-1h3.5a1 1 0 011 1 11.48 11.48 0 00.57 3.6 1 1 0 01-.25 1.01l-2.2 2.18z"/>
              </svg>
            </button>
          </div>
        </div>
      )}

      {/* ── ON CALL ── */}
      {callStatus === 'on-call' && (
        <div className="flex flex-col gap-3 flex-1">
          <div className="bg-green-950 border border-green-800 rounded-lg px-3 py-2 text-center">
            <p className="text-green-400 text-xs font-medium mb-1">On call</p>
            <p className="text-white font-mono text-xl tracking-widest">{formatElapsed(elapsed)}</p>
          </div>

          <div className="text-center">
            <p className="text-white text-sm font-semibold truncate">{callerNumber ?? 'Unknown'}</p>
            {callerName && <p className="text-gray-400 text-xs truncate">{callerName}</p>}
          </div>

          {/* Mute + Hang Up */}
          <div className="flex gap-3 justify-center mt-1">
            <button
              onClick={handleMute}
              className={`w-12 h-12 rounded-full flex items-center justify-center transition-colors ${
                isMuted
                  ? 'bg-yellow-600 hover:bg-yellow-500'
                  : 'bg-gray-700 hover:bg-gray-600'
              }`}
              title={isMuted ? 'Unmute' : 'Mute'}
            >
              {isMuted ? (
                <svg className="w-5 h-5 text-white" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M19 11h-1.7c0 .74-.16 1.43-.43 2.05l1.23 1.23c.56-.98.9-2.09.9-3.28zm-4.02.17c0-.06.02-.11.02-.17V5c0-1.66-1.34-3-3-3S9 3.34 9 5v.18l5.98 5.99zM4.27 3L3 4.27l6.01 6.01V11c0 1.66 1.33 3 2.99 3 .22 0 .44-.03.65-.08l1.66 1.66c-.71.33-1.5.52-2.31.52-2.76 0-5.3-2.1-5.3-5.1H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c.91-.13 1.77-.45 2.54-.9L19.73 21 21 19.73 4.27 3z"/>
                </svg>
              ) : (
                <svg className="w-5 h-5 text-white" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M12 14c1.66 0 2.99-1.34 2.99-3L15 5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5.3-3c0 3-2.54 5.1-5.3 5.1S6.7 14 6.7 11H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c3.28-.48 6-3.3 6-6.72h-1.7z"/>
                </svg>
              )}
            </button>

            <button
              onClick={handleHangUp}
              className="w-12 h-12 rounded-full bg-red-600 hover:bg-red-500 flex items-center justify-center transition-colors"
              title="Hang up"
            >
              <svg className="w-5 h-5 text-white rotate-135" fill="currentColor" viewBox="0 0 24 24">
                <path d="M6.62 10.79a15.05 15.05 0 006.59 6.59l2.2-2.2a1 1 0 011.01-.24 11.48 11.48 0 003.6.57 1 1 0 011 1V21a1 1 0 01-1 1A17 17 0 013 5a1 1 0 011-1h3.5a1 1 0 011 1 11.48 11.48 0 00.57 3.6 1 1 0 01-.25 1.01l-2.2 2.18z"/>
              </svg>
            </button>
          </div>
        </div>
      )}

      {/* Idle / waiting */}
      {callStatus === 'idle' && sipExtension && registrationStatus === 'registered' && (
        <div className="flex-1 flex items-center justify-center">
          <p className="text-gray-700 text-xs text-center">Waiting for calls…</p>
        </div>
      )}

      {/* No creds */}
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
