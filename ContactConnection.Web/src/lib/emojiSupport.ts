/**
 * Which emoji generation this device's font can draw (S183). Newer emoji (Unicode 14 / 15 — 🫠, 🫨 …) render as empty boxes
 * on fonts that predate them, e.g. Windows 10's Segoe UI Emoji. Each probe is drawn off-screen; a real emoji comes out in
 * colour, a missing one as a grey outline box. The emoji picker is capped at the newest generation that draws, so it never
 * offers an emoji the agent can't see.
 */

const PROBES: [version: string, emoji: string][] = [
  ['15.0', '🫨'], // shaking face
  ['14.0', '🫠'], // melting face
  ['13.0', '🥲'], // smiling face with tear
  ['12.0', '🥱'], // yawning face
  ['11.0', '🥰'], // smiling face with hearts
]

let cached: string | null | undefined

function drawsInColour(ctx: CanvasRenderingContext2D, emoji: string) {
  ctx.clearRect(0, 0, 32, 32)
  ctx.fillText(emoji, 0, 0)
  const { data } = ctx.getImageData(0, 0, 32, 32)
  for (let i = 0; i < data.length; i += 4) {
    if (data[i + 3] < 50) continue
    const r = data[i], g = data[i + 1], b = data[i + 2]
    if (Math.max(r, g, b) - Math.min(r, g, b) > 40) return true   // any saturated pixel — a colour emoji, not a box
  }
  return false
}

/** The newest emoji version this device draws (e.g. "12.0"), or null when it can't be measured (no limit then). */
export function supportedEmojiVersion(): string | null {
  if (cached !== undefined) return cached
  try {
    const canvas = document.createElement('canvas')
    canvas.width = canvas.height = 32
    const ctx = canvas.getContext('2d', { willReadFrequently: true })
    if (!ctx) return (cached = null)
    ctx.textBaseline = 'top'
    ctx.font = '24px "Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji","Segoe UI Symbol",sans-serif'
    ctx.fillStyle = '#000'
    cached = PROBES.find(([, e]) => drawsInColour(ctx, e))?.[0] ?? '5.0'
  } catch {
    cached = null
  }
  return cached
}
