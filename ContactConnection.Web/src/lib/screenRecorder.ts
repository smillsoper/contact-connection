import { create } from 'zustand'
import { api } from '../api/client'
import { onExtensionInput, setInputCapture, type ExtensionInputEvent } from './extensionBridge'

/**
 * Agent screen recording (S183). The agent shares their screen once per shift (one browser prompt); every call on a
 * campaign with screen recording switched on then records its own segment from that live share — no prompt per call.
 *
 * Timing: the server merges the video with the call audio on the server's clock. Before each segment the portal samples
 * GET /screen-recordings/time a few times and keeps the fastest round trip: offset = server − (local send+receive)/2,
 * accurate to ±rtt/2. The segment's start (MediaRecorder "start", local clock) + that offset is its start on the server.
 *
 * Clicks come from the browser extension (ContactConnection pages only; never keystrokes) and are stored as cue points on the
 * segment's own timeline; the server draws them onto the merged video.
 */

export type ShareStatus = 'none' | 'requesting' | 'sharing' | 'ended'

interface ScreenShareState {
  status: ShareStatus
  /** The share isn't the screen this portal window is on — click positions can't be placed. */
  otherScreen: boolean
  error: string | null
  /** Campaigns that record screens (null until loaded). */
  campaignIds: string[] | null
  /** The call being recorded right now. */
  recordingCallId: string | null
  lastProblem: string | null
}

export const useScreenShareStore = create<ScreenShareState>(() => ({
  status: 'none', otherScreen: false, error: null, campaignIds: null, recordingCallId: null, lastProblem: null,
}))

let stream: MediaStream | null = null
/** The portal window's screen when sharing began — the frame of reference for click positions. */
let shareScreen: { l: number; t: number; w: number; h: number; dpr: number } | null = null

export async function loadRecordingCampaigns() {
  try {
    const r = await api.get<{ campaignIds: string[] }>('/api/v1/screen-recordings/campaigns')
    useScreenShareStore.setState({ campaignIds: r.campaignIds })
  } catch { useScreenShareStore.setState({ campaignIds: [] }) }
}

/** Must run from a click (browsers only show the share picker on a user gesture). */
export async function startScreenShare() {
  if (stream) return
  useScreenShareStore.setState({ status: 'requesting', error: null })
  try {
    const s = await navigator.mediaDevices.getDisplayMedia({
      video: { displaySurface: 'monitor', frameRate: { ideal: 10, max: 15 } },
      audio: false,
      // Chrome hints: offer whole screens first, keep this tab out of the list.
      ...({ monitorTypeSurfaces: 'include', selfBrowserSurface: 'exclude', surfaceSwitching: 'exclude' } as object),
    } as DisplayMediaStreamOptions)
    const track = s.getVideoTracks()[0]
    const settings = track.getSettings() as MediaTrackSettings & { displaySurface?: string }
    if (settings.displaySurface && settings.displaySurface !== 'monitor') {
      s.getTracks().forEach((t) => t.stop())
      useScreenShareStore.setState({ status: 'none', error: 'Share your entire screen — not a window or a tab.' })
      return
    }
    stream = s
    const dpr = window.devicePixelRatio || 1
    // availLeft / availTop are Chromium extensions to Screen — the same values the extension reports per click.
    const scr = screen as Screen & { availLeft?: number; availTop?: number }
    shareScreen = { l: scr.availLeft ?? 0, t: scr.availTop ?? 0, w: screen.width, h: screen.height, dpr }
    // Which monitor was picked isn't exposed to the page. Compare shapes, never sizes: page zoom changes the pixel ratio
    // and Chrome may scale the capture down, so a size check flagged the right screen as wrong (S183). Same aspect ratio
    // = taken to be the screen this window is on.
    const sameScreen = !!settings.width && !!settings.height
      && Math.abs(settings.width / settings.height - screen.width / screen.height) < 0.02
    track.addEventListener('ended', () => {
      // "Stop sharing" in the browser bar, or the screen went away.
      stream = null
      void stopSegment('share ended')
      useScreenShareStore.setState({ status: 'ended' })
    })
    useScreenShareStore.setState({ status: 'sharing', otherScreen: !sameScreen })
  } catch (e) {
    const denied = e instanceof DOMException && e.name === 'NotAllowedError'
    useScreenShareStore.setState({ status: 'none', error: denied ? 'Screen sharing was cancelled.' : (e instanceof Error ? e.message : 'Screen sharing failed.') })
  }
}

/** The live shared screen (live screen view, S183) — null when the agent isn't sharing. */
export function getShareStream(): MediaStream | null {
  return stream && stream.getVideoTracks().some((t) => t.readyState === 'live') ? stream : null
}

/** The screen the share is of, for placing a supervisor's pointer: availLeft/Top/width/height in CSS pixels. */
export function getShareScreen() {
  return shareScreen
}

export function stopScreenShare() {
  stream?.getTracks().forEach((t) => t.stop())
  stream = null
  void stopSegment('share stopped')
  useScreenShareStore.setState({ status: 'none' })
}

// ── Clock ────────────────────────────────────────────────────────────────────

async function syncClock(samples = 5): Promise<{ offsetMs: number; rttMs: number } | null> {
  let best: { offsetMs: number; rttMs: number } | null = null
  for (let i = 0; i < samples; i++) {
    try {
      const t0 = Date.now()
      const r = await api.get<{ serverTime: string }>('/api/v1/screen-recordings/time')
      const t1 = Date.now()
      const rtt = t1 - t0
      const offset = Math.round(new Date(r.serverTime).getTime() - (t0 + t1) / 2)
      if (!best || rtt < best.rttMs) best = { offsetMs: offset, rttMs: rtt }
    } catch { /* keep the samples we have */ }
  }
  return best
}

// ── One call's segment ───────────────────────────────────────────────────────

interface Segment {
  callRecordId: string
  recorder: MediaRecorder
  startedAt: number | null      // local clock, MediaRecorder "start"
  id: Promise<string | null>     // server id once the start request returns
  nextIndex: number
  uploads: Promise<unknown>      // chain — chunks go up in order
  cues: { atMs: number; kind: string; detail: string }[]
  flushTimer: ReturnType<typeof setInterval> | null
  unsubscribeInput: () => void
  width: number
  height: number
  stopped: Promise<void>
}

let current: Segment | null = null

function mimeType() {
  for (const m of ['video/webm;codecs=vp9', 'video/webm;codecs=vp8', 'video/webm'])
    if (MediaRecorder.isTypeSupported(m)) return m
  return ''
}

function report(problem: string) {
  console.warn('[screen-recording]', problem)
  useScreenShareStore.setState({ lastProblem: problem })
}

/** Starts recording this call's segment from the live share. No-op without a share or when already recording it. */
export function startSegment(callRecordId: string) {
  if (!stream || current?.callRecordId === callRecordId) return
  if (current) void stopSegment('next call')

  const settings = stream.getVideoTracks()[0]?.getSettings() ?? {}
  const type = mimeType()
  const recorder = new MediaRecorder(stream, { ...(type ? { mimeType: type } : {}), videoBitsPerSecond: 600_000 })
  let resolveStopped!: () => void
  const seg: Segment = {
    callRecordId, recorder, startedAt: null, id: Promise.resolve(null), nextIndex: 0, uploads: Promise.resolve(), cues: [],
    flushTimer: null, unsubscribeInput: () => {}, width: settings.width ?? 0, height: settings.height ?? 0,
    stopped: new Promise<void>((r) => { resolveStopped = r }),
  }
  current = seg
  useScreenShareStore.setState({ recordingCallId: callRecordId, lastProblem: null })

  recorder.onstart = () => {
    seg.startedAt = Date.now()
    seg.id = (async () => {
      const clock = await syncClock()
      try {
        const r = await api.post<{ id: string }>('/api/v1/screen-recordings', {
          callRecordId, startedAtClient: new Date(seg.startedAt!).toISOString(),
          container: 'webm', codec: (type.split('codecs=')[1] ?? ''),
          clockOffsetMs: clock?.offsetMs ?? null, clockRttMs: clock?.rttMs ?? null,
          videoWidth: seg.width || null, videoHeight: seg.height || null,
        })
        return r.id
      } catch (e) {
        report(`Screen recording didn't start: ${e instanceof Error ? e.message : e}`)
        recorder.state !== 'inactive' && recorder.stop()
        return null
      }
    })()
    seg.unsubscribeInput = onExtensionInput((ev) => addInputCue(seg, ev))
    setInputCapture(true)
    seg.flushTimer = setInterval(() => void flushCues(seg), 3000)
  }

  recorder.ondataavailable = (e) => {
    if (!e.data || e.data.size === 0) return
    const index = seg.nextIndex++
    seg.uploads = seg.uploads.then(async () => {
      const id = await seg.id
      if (!id) return
      for (let attempt = 0; attempt < 3; attempt++) {
        try { await api.putBinary(`/api/v1/screen-recordings/${id}/chunks/${index}`, e.data); return }
        catch (err) { if (attempt === 2) report(`A screen-recording chunk failed to upload: ${err instanceof Error ? err.message : err}`) }
        await new Promise((r) => setTimeout(r, 1000 * (attempt + 1)))
      }
    })
  }

  recorder.onstop = () => {
    void (async () => {
      setInputCapture(false)
      seg.unsubscribeInput()
      if (seg.flushTimer) clearInterval(seg.flushTimer)
      await seg.uploads
      const id = await seg.id
      if (id) {
        await flushCues(seg)
        try {
          await api.post(`/api/v1/screen-recordings/${id}/complete`, { durationMs: seg.startedAt ? Date.now() - seg.startedAt : 0 })
        } catch (e) { report(`Screen recording didn't finish uploading: ${e instanceof Error ? e.message : e}`) }
      }
      if (current === seg) { current = null; useScreenShareStore.setState({ recordingCallId: null }) }
      resolveStopped()
    })()
  }

  recorder.start(4000)   // a chunk every 4 s — a dropped connection loses little
}

/** Stops the current segment (call ended, share stopped). Resolves once it's uploaded. */
export async function stopSegment(_why: string) {
  const seg = current
  if (!seg) return
  if (seg.recorder.state !== 'inactive') seg.recorder.stop()
  await seg.stopped
}

function addInputCue(seg: Segment, ev: ExtensionInputEvent) {
  if (seg.startedAt == null) return
  const atMs = ev.t - seg.startedAt
  if (atMs < 0) return
  if (ev.kind === 'key' && ev.label) {
    seg.cues.push({ atMs, kind: 'key', detail: ev.label.slice(0, 40) })
  } else if (ev.kind === 'click' && shareScreen && ev.scr && ev.sx != null && ev.sy != null && seg.width && seg.height) {
    // Only clicks on the shared screen (the one the portal is on) can be placed.
    if (useScreenShareStore.getState().otherScreen || !sameMonitor(ev.scr, ev.dpr ?? 1, shareScreen)) return
    // Where across / down the screen, in that page's own units — page zoom and capture scaling cancel out — then
    // into the video's pixels.
    const s = ev.scr
    const fx = (ev.sx - s.l) / s.w
    const fy = (ev.sy - s.t) / s.h
    if (fx < 0 || fy < 0 || fx > 1 || fy > 1) return
    seg.cues.push({ atMs, kind: 'click', detail: `${Math.round(fx * seg.width)},${Math.round(fy * seg.height)}` })
  }
}

/** Same physical monitor? Pages at different zoom report screen geometry in different units, so compare either as
 *  reported or scaled to device pixels. */
function sameMonitor(s: { l: number; t: number; w: number; h: number }, dpr: number,
  ref: { l: number; t: number; w: number; h: number; dpr: number }) {
  const near = (a: number, b: number) => Math.abs(a - b) <= 2
  if (near(s.l, ref.l) && near(s.t, ref.t) && near(s.w, ref.w) && near(s.h, ref.h)) return true
  return near(s.l * dpr, ref.l * ref.dpr) && near(s.t * dpr, ref.t * ref.dpr)
    && near(s.w * dpr, ref.w * ref.dpr) && near(s.h * dpr, ref.h * ref.dpr)
}

async function flushCues(seg: Segment) {
  if (seg.cues.length === 0) return
  const id = await seg.id
  if (!id) return
  const points = seg.cues.splice(0, seg.cues.length)
  try { await api.post(`/api/v1/screen-recordings/${id}/cuepoints`, { points }) }
  catch { seg.cues.unshift(...points) }   // try again on the next flush
}

/** Whether a call on this campaign should be recorded. */
export function campaignRecordsScreen(campaignId: string | null) {
  const ids = useScreenShareStore.getState().campaignIds
  return !!campaignId && !!ids && ids.includes(campaignId)
}
