import { useCallback, useEffect, useRef, useState } from 'react'
import {
  applySpeaker, canChooseSpeaker, getInputDeviceId, getOutputDeviceId, micConstraints,
  setInputDeviceId, setOutputDeviceId,
} from '../utils/audioDevices'
import {
  RINGTONES, getRingOutput, getRingVolume, getRingtone, setRingOutput, setRingVolume, setRingtone, startRinging,
  type RingtoneId,
} from '../utils/ringtone'

// Softphone audio settings (S169): pick the headset microphone and speaker, see the mic level move,
// and play a test sound — so an agent can sort out their own headset without a walkthrough.

export default function AudioSettingsPanel() {
  const [inputs, setInputs] = useState<MediaDeviceInfo[]>([])
  const [outputs, setOutputs] = useState<MediaDeviceInfo[]>([])
  const [inputId, setInputId] = useState(getInputDeviceId())
  const [outputId, setOutputId] = useState(getOutputDeviceId())
  const [needsPermission, setNeedsPermission] = useState(false)
  const [level, setLevel] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [ringtone, setRingtoneState] = useState<RingtoneId>(getRingtone())
  const [ringVolume, setRingVolumeState] = useState(getRingVolume())
  const [ringOutput, setRingOutputState] = useState(getRingOutput())
  const previewRef = useRef<{ stop: () => void } | null>(null)
  useEffect(() => () => previewRef.current?.stop(), [])

  function previewRing(tone = ringtone, volume = ringVolume, output = ringOutput) {
    previewRef.current?.stop()
    previewRef.current = startRinging({ tone, volume, outputId: output || outputId, cycles: 1 })
  }

  const refresh = useCallback(async () => {
    try {
      const devices = await navigator.mediaDevices.enumerateDevices()
      const ins = devices.filter((d) => d.kind === 'audioinput')
      setInputs(ins)
      setOutputs(devices.filter((d) => d.kind === 'audiooutput'))
      // Device names are hidden until the site has microphone permission.
      setNeedsPermission(ins.length > 0 && ins.every((d) => !d.label))
    } catch {
      setError('This browser can’t list audio devices.')
    }
  }, [])

  useEffect(() => {
    refresh()
    navigator.mediaDevices?.addEventListener('devicechange', refresh)
    return () => navigator.mediaDevices?.removeEventListener('devicechange', refresh)
  }, [refresh])

  // Live mic level for the chosen microphone, only while this panel is open.
  const rafRef = useRef<number>(0)
  useEffect(() => {
    if (needsPermission) return
    let stream: MediaStream | null = null
    let ctx: AudioContext | null = null
    let cancelled = false
    navigator.mediaDevices.getUserMedia(micConstraints()).then((s) => {
      if (cancelled) { s.getTracks().forEach((t) => t.stop()); return }
      stream = s
      ctx = new AudioContext()
      const analyser = ctx.createAnalyser()
      analyser.fftSize = 512
      ctx.createMediaStreamSource(s).connect(analyser)
      const data = new Uint8Array(analyser.fftSize)
      const tick = () => {
        analyser.getByteTimeDomainData(data)
        let sum = 0
        for (const v of data) { const x = (v - 128) / 128; sum += x * x }
        setLevel(Math.min(1, Math.sqrt(sum / data.length) * 4))
        rafRef.current = requestAnimationFrame(tick)
      }
      tick()
    }).catch(() => setError('Couldn’t open the microphone — check the browser’s microphone permission.'))
    return () => {
      cancelled = true
      cancelAnimationFrame(rafRef.current)
      stream?.getTracks().forEach((t) => t.stop())
      ctx?.close().catch(() => {})
      setLevel(0)
    }
  }, [inputId, needsPermission])

  async function allowMic() {
    setError(null)
    try {
      const s = await navigator.mediaDevices.getUserMedia({ audio: true })
      s.getTracks().forEach((t) => t.stop())
      await refresh()
    } catch {
      setError('Microphone access was blocked. Allow it from the lock icon in the address bar, then try again.')
    }
  }

  async function playTestSound() {
    setError(null)
    try {
      const ctx = new AudioContext()
      const dest = ctx.createMediaStreamDestination()
      const gain = ctx.createGain()
      gain.gain.value = 0.2
      gain.connect(dest)
      // Two short tones, like a phone chime.
      ;[[660, 0], [880, 0.35]].forEach(([freq, at]) => {
        const osc = ctx.createOscillator()
        osc.frequency.value = freq
        osc.connect(gain)
        osc.start(ctx.currentTime + at)
        osc.stop(ctx.currentTime + at + 0.3)
      })
      const el = new Audio()
      el.srcObject = dest.stream
      await applySpeaker(el, outputId)
      await el.play()
      setTimeout(() => { el.pause(); ctx.close().catch(() => {}) }, 1000)
    } catch {
      setError('Couldn’t play the test sound on that speaker.')
    }
  }

  const selectCls = 'w-full bg-gray-800 text-white text-xs rounded px-2 py-1.5 focus:outline-none focus:ring-1 focus:ring-blue-500'

  return (
    <div className="bg-gray-800/60 border border-gray-700 rounded-lg p-2.5 flex flex-col gap-2 text-xs">
      <p className="text-gray-300 font-medium">Audio settings</p>

      {needsPermission ? (
        <button onClick={allowMic} className="bg-blue-600 hover:bg-blue-500 text-white rounded px-2 py-1.5">
          Allow microphone to choose devices
        </button>
      ) : (
        <>
          <label className="flex flex-col gap-1">
            <span className="text-gray-400">Microphone</span>
            <select value={inputId} className={selectCls}
              onChange={(e) => { setInputId(e.target.value); setInputDeviceId(e.target.value) }}>
              <option value="">System default</option>
              {inputs.filter((d) => d.deviceId && d.deviceId !== 'default').map((d) => (
                <option key={d.deviceId} value={d.deviceId}>{d.label || 'Microphone'}</option>
              ))}
            </select>
          </label>
          <div className="h-1.5 bg-gray-900 rounded overflow-hidden" title="Speak — the bar should move">
            <div className="h-full bg-emerald-500 transition-[width] duration-75" style={{ width: `${Math.round(level * 100)}%` }} />
          </div>
          <p className="text-[10px] text-gray-500 -mt-1">Speak — the green bar should move.</p>

          {canChooseSpeaker() ? (
            <label className="flex flex-col gap-1">
              <span className="text-gray-400">Speaker / headset</span>
              <select value={outputId} className={selectCls}
                onChange={(e) => { setOutputId(e.target.value); setOutputDeviceId(e.target.value) }}>
                <option value="">System default</option>
                {outputs.filter((d) => d.deviceId && d.deviceId !== 'default').map((d) => (
                  <option key={d.deviceId} value={d.deviceId}>{d.label || 'Speaker'}</option>
                ))}
              </select>
            </label>
          ) : (
            <p className="text-[10px] text-gray-500">This browser plays calls on the system’s default speaker.</p>
          )}
          <button onClick={playTestSound} className="border border-gray-600 text-gray-200 hover:bg-gray-700 rounded px-2 py-1">
            Play test sound
          </button>
        </>
      )}

      <div className="border-t border-gray-700 pt-2 flex flex-col gap-2">
        <p className="text-gray-300 font-medium">Ringtone</p>
        <label className="flex flex-col gap-1">
          <span className="text-gray-400">Sound</span>
          <select value={ringtone} className={selectCls}
            onChange={(e) => { const t = e.target.value as RingtoneId; setRingtoneState(t); setRingtone(t); previewRing(t) }}>
            {RINGTONES.map((r) => <option key={r.id} value={r.id}>{r.label}</option>)}
          </select>
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-gray-400">Volume</span>
          <input type="range" min={0} max={1} step={0.05} value={ringVolume} disabled={ringtone === 'off'}
            onChange={(e) => { const v = Number(e.target.value); setRingVolumeState(v); setRingVolume(v) }}
            onPointerUp={() => previewRing()} className="accent-blue-500" />
        </label>
        {canChooseSpeaker() && (
          <label className="flex flex-col gap-1">
            <span className="text-gray-400">Ring on</span>
            <select value={ringOutput} className={selectCls} disabled={ringtone === 'off'}
              onChange={(e) => { setRingOutputState(e.target.value); setRingOutput(e.target.value); previewRing(ringtone, ringVolume, e.target.value) }}>
              <option value="">Same as call audio</option>
              {outputs.filter((d) => d.deviceId && d.deviceId !== 'default').map((d) => (
                <option key={d.deviceId} value={d.deviceId}>{d.label || 'Speaker'}</option>
              ))}
            </select>
            <span className="text-[10px] text-gray-500">Pick your PC speakers to hear calls ring with the headset off.</span>
          </label>
        )}
        <button onClick={() => previewRing()} disabled={ringtone === 'off'}
          className="border border-gray-600 text-gray-200 hover:bg-gray-700 disabled:opacity-40 rounded px-2 py-1">
          Preview ringtone
        </button>
      </div>
      {error && <p className="text-[10px] text-red-400">{error}</p>}
    </div>
  )
}
