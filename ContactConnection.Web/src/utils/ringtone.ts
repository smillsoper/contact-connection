// Softphone ringtone (S169). Rings while a call is waiting on the agent — a queue / ring-all offer, a
// direct incoming call, or a supervisor's call — so an agent who isn't looking at the screen still knows.
// Tones are synthesized with Web Audio (no audio files). Settings are remembered per browser.

import { getOutputDeviceId } from './audioDevices'

export type RingtoneId = 'classic' | 'double' | 'chime' | 'soft' | 'digital' | 'off'

export const RINGTONES: { id: RingtoneId; label: string }[] = [
  { id: 'classic', label: 'Classic phone' },
  { id: 'double', label: 'Double ring' },
  { id: 'chime', label: 'Chime' },
  { id: 'soft', label: 'Soft pulse' },
  { id: 'digital', label: 'Digital beeps' },
  { id: 'off', label: 'Off (silent)' },
]

const TONE_KEY = 'cc.ring.tone'
const VOLUME_KEY = 'cc.ring.volume'
const OUTPUT_KEY = 'cc.ring.output'

function read(key: string): string | null {
  try { return localStorage.getItem(key) } catch { return null }
}
function write(key: string, value: string) {
  try { localStorage.setItem(key, value) } catch { /* storage unavailable — lasts until reload */ }
}

export const getRingtone = (): RingtoneId => (RINGTONES.some((r) => r.id === read(TONE_KEY)) ? read(TONE_KEY) as RingtoneId : 'classic')
export const setRingtone = (id: RingtoneId) => write(TONE_KEY, id)
export const getRingVolume = () => { const v = Number(read(VOLUME_KEY)); return read(VOLUME_KEY) !== null && v >= 0 && v <= 1 ? v : 0.6 }
export const setRingVolume = (v: number) => write(VOLUME_KEY, String(Math.min(1, Math.max(0, v))))
/** '' = ring on the same device as call audio. */
export const getRingOutput = () => read(OUTPUT_KEY) ?? ''
export const setRingOutput = (id: string) => write(OUTPUT_KEY, id)

// ── Shared audio context ─────────────────────────────────────────────────────
// Browsers only let a page make sound after the user has interacted with it. The context is created
// (or resumed) on the first click/keypress anywhere, so a later incoming call can ring.
let ctx: AudioContext | null = null
function context(): AudioContext {
  if (!ctx) ctx = new AudioContext()
  if (ctx.state === 'suspended') ctx.resume().catch(() => {})
  return ctx
}
if (typeof window !== 'undefined') {
  const unlock = () => { context(); window.removeEventListener('pointerdown', unlock); window.removeEventListener('keydown', unlock) }
  window.addEventListener('pointerdown', unlock)
  window.addEventListener('keydown', unlock)
}
/** False until the page has been clicked once after loading — rings can't play before that. */
export const canRing = () => ctx?.state === 'running'

// ── Patterns ─────────────────────────────────────────────────────────────────
// Each pattern schedules one cycle starting at `t` into `out` and returns the cycle length (s).
type Pattern = (c: AudioContext, out: AudioNode, t: number) => number

function tone(c: AudioContext, out: AudioNode, freqs: number[], start: number, dur: number, type: OscillatorType = 'sine', peak = 1) {
  const g = c.createGain()
  g.gain.setValueAtTime(0, start)
  g.gain.linearRampToValueAtTime(peak / freqs.length, start + 0.02)
  g.gain.setValueAtTime(peak / freqs.length, start + dur - 0.03)
  g.gain.linearRampToValueAtTime(0, start + dur)
  g.connect(out)
  for (const f of freqs) {
    const o = c.createOscillator()
    o.type = type
    o.frequency.value = f
    o.connect(g)
    o.start(start)
    o.stop(start + dur)
  }
}

const PATTERNS: Record<Exclude<RingtoneId, 'off'>, Pattern> = {
  // North American ring: 440 + 480 Hz, 2 s on, 4 s off.
  classic: (c, out, t) => { tone(c, out, [440, 480], t, 2); return 6 },
  // UK-style double ring: 400 + 450 Hz, 0.4 on / 0.2 off / 0.4 on / 2 off.
  double: (c, out, t) => { tone(c, out, [400, 450], t, 0.4); tone(c, out, [400, 450], t + 0.6, 0.4); return 3 },
  chime: (c, out, t) => {
    ;[659, 784, 988].forEach((f, i) => tone(c, out, [f], t + i * 0.22, 0.5, 'triangle'))
    return 3
  },
  soft: (c, out, t) => {
    const g = c.createGain()
    g.gain.setValueAtTime(0, t)
    g.gain.linearRampToValueAtTime(0.8, t + 0.6)
    g.gain.linearRampToValueAtTime(0, t + 1.4)
    g.connect(out)
    const o = c.createOscillator()
    o.frequency.value = 523
    o.connect(g)
    o.start(t)
    o.stop(t + 1.5)
    return 2.5
  },
  digital: (c, out, t) => { [0, 0.18, 0.36].forEach((d) => tone(c, out, [1000], t + d, 0.1, 'square', 0.5)); return 2 },
}

// ── Player ───────────────────────────────────────────────────────────────────
interface Ringing { stop: () => void }

/** Starts ringing with the saved settings (or the given overrides). `cycles` limits a preview. */
export function startRinging(opts: { tone?: RingtoneId; volume?: number; outputId?: string; cycles?: number } = {}): Ringing {
  const toneId = opts.tone ?? getRingtone()
  if (toneId === 'off') return { stop: () => {} }

  const c = context()
  const master = c.createGain()
  master.gain.value = opts.volume ?? getRingVolume()
  const dest = c.createMediaStreamDestination()
  master.connect(dest)

  // Play through an <audio> element so the ring can go to a chosen device (setSinkId).
  const el = new Audio()
  el.srcObject = dest.stream
  const deviceId = opts.outputId ?? (getRingOutput() || getOutputDeviceId())
  ;(deviceId && 'setSinkId' in el ? el.setSinkId(deviceId).catch(() => {}) : Promise.resolve())
    .then(() => el.play().catch(() => {}))

  const pattern = PATTERNS[toneId]
  let next = c.currentTime + 0.05
  let played = 0
  let stopped = false
  // Schedule a cycle ahead of time; check twice a second whether the next one is due.
  const schedule = () => {
    while (!stopped && next < c.currentTime + 1 && (opts.cycles === undefined || played < opts.cycles)) {
      next += pattern(c, master, next)
      played++
    }
    if (opts.cycles !== undefined && played >= opts.cycles && c.currentTime > next) stop()
  }
  schedule()
  const timer = window.setInterval(schedule, 500)

  function stop() {
    if (stopped) return
    stopped = true
    window.clearInterval(timer)
    master.gain.cancelScheduledValues(c.currentTime)
    master.gain.setValueAtTime(0, c.currentTime)
    setTimeout(() => { master.disconnect(); el.pause(); el.srcObject = null }, 100)
  }
  return { stop }
}
