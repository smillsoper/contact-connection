/**
 * Emoji this device's font can't draw (S183). Newer emoji (Unicode 13+ on Windows 10's Segoe UI Emoji — 🥲, 🫠, 🫨 …) render
 * as empty boxes.
 *  • supportedEmojiVersion — caps the emoji picker at the newest generation that draws, so it never offers a box.
 *  • canDrawEmoji / splitEmoji — for emoji typed or pasted elsewhere (messages, reactions): any that would be a box is shown
 *    as the default "emoji not available" icon instead (components/chat/EmojiText).
 *
 * How a box is recognised: an emoji no font has falls through every font to the same "missing glyph" as a code point that
 * doesn't exist at all, so each emoji is drawn off-screen and compared with that reference (or found to draw nothing).
 */

const PROBES: [version: string, emoji: string][] = [
  ['15.0', '🫨'], // shaking face
  ['14.0', '🫠'], // melting face
  ['13.0', '🥲'], // smiling face with tear
  ['12.0', '🥱'], // yawning face
  ['11.0', '🥰'], // smiling face with hearts
]

const SIZE = 40
const FONT = '28px "Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji","Segoe UI Symbol",sans-serif'
/** Unassigned in the emoji block — no font has it, so it draws as the missing-glyph box. */
const NO_SUCH_EMOJI = '\u{1FAFF}'

let ctx: CanvasRenderingContext2D | null | undefined
let missing: Uint8ClampedArray | null = null
const verdicts = new Map<string, boolean>()

function canvas(): CanvasRenderingContext2D | null {
  if (ctx !== undefined) return ctx
  try {
    const c = document.createElement('canvas')
    c.width = c.height = SIZE
    ctx = c.getContext('2d', { willReadFrequently: true })
    if (ctx) {
      ctx.textBaseline = 'top'
      ctx.font = FONT
      ctx.fillStyle = '#000'
      missing = draw(NO_SUCH_EMOJI)
    }
  } catch { ctx = null }
  return ctx ?? null
}

function draw(text: string): Uint8ClampedArray {
  ctx!.clearRect(0, 0, SIZE, SIZE)
  ctx!.fillText(text, 2, 2)
  return ctx!.getImageData(0, 0, SIZE, SIZE).data
}

function isBlankOrMissing(px: Uint8ClampedArray) {
  let ink = 0, diff = 0
  for (let i = 3; i < px.length; i += 4) {
    if (px[i] > 20) ink++
    if (missing && Math.abs(px[i] - missing[i]) > 40) diff++
  }
  return ink === 0 || (missing !== null && diff < 6)
}

/** Whether one emoji (a single grapheme) draws on this device. Unmeasurable → assume it does. */
export function canDrawEmoji(emoji: string): boolean {
  const known = verdicts.get(emoji)
  if (known !== undefined) return known
  const c = canvas()
  let ok = true
  if (c) {
    // Sequences (ZWJ families, skin tones) are checked part by part — any part that's a box spoils the whole emoji.
    const parts = emoji.split('‍').map((p) => p.replace(/[︎️]|[\u{1F3FB}-\u{1F3FF}]|[\u{E0020}-\u{E007F}]/gu, '')).filter(Boolean)
    ok = parts.every((p) => !isBlankOrMissing(draw(p)))
  }
  verdicts.set(emoji, ok)
  return ok
}

/** Emoji-looking graphemes only — text symbols such as © ™ ↔ (no emoji presentation) are left alone. */
const EMOJI_GRAPHEME = /\p{Emoji_Presentation}|️|‍|\p{Regional_Indicator}/u
// Intl.Segmenter is in every current browser; the project's TypeScript lib target predates it.
type GraphemeSegmenter = { segment: (text: string) => Iterable<{ segment: string }> }
const SegmenterCtor = (Intl as unknown as { Segmenter?: new (locale?: string, o?: { granularity: 'grapheme' }) => GraphemeSegmenter }).Segmenter
const segmenter: GraphemeSegmenter | null = SegmenterCtor ? new SegmenterCtor(undefined, { granularity: 'grapheme' }) : null

export type EmojiPiece = { text: string; missing: boolean }

/** The text in pieces, marking emoji this device can't draw. Text with none comes back as a single piece. */
export function splitEmoji(text: string): EmojiPiece[] {
  if (!segmenter || !text || !/\p{Extended_Pictographic}|\p{Regional_Indicator}/u.test(text)) return [{ text, missing: false }]
  const out: EmojiPiece[] = []
  let run = ''
  for (const { segment } of segmenter.segment(text)) {
    if (EMOJI_GRAPHEME.test(segment) && !canDrawEmoji(segment)) {
      if (run) { out.push({ text: run, missing: false }); run = '' }
      out.push({ text: segment, missing: true })
    } else run += segment
  }
  if (run) out.push({ text: run, missing: false })
  return out
}

/** "U+1FAE0" — how a missing emoji is named in its tooltip. */
export function codepoints(emoji: string) {
  return [...emoji].filter((c) => c !== '‍' && c !== '️')
    .map((c) => 'U+' + c.codePointAt(0)!.toString(16).toUpperCase()).join(' ')
}

let cachedVersion: string | null | undefined

/** The newest emoji version this device draws (e.g. "12.0"), or null when it can't be measured (no limit then). */
export function supportedEmojiVersion(): string | null {
  if (cachedVersion !== undefined) return cachedVersion
  if (!canvas()) return (cachedVersion = null)
  cachedVersion = PROBES.find(([, e]) => canDrawEmoji(e))?.[0] ?? '5.0'
  return cachedVersion
}
