import { Fragment } from 'react'
import { codepoints, splitEmoji } from '../../lib/emojiSupport'
import { EMOJI_MISSING_SVG, EmojiMissingIcon } from '../icons/Icons'

/**
 * Text with any emoji this device can't draw shown as the default emoticon (S183) instead of an empty box. Hover names
 * the emoji. Everything else renders as plain text.
 */
export default function EmojiText({ text }: { text: string }) {
  const pieces = splitEmoji(text)
  if (pieces.length === 1 && !pieces[0].missing) return <>{text}</>
  return (
    <>
      {pieces.map((p, i) => p.missing
        ? <span key={i} className="emoji-missing" title={missingTitle(p.text)}><EmojiMissingIcon size={15} /></span>
        : <Fragment key={i}>{p.text}</Fragment>)}
    </>
  )
}

export function missingTitle(emoji: string) {
  return `An emoji this device can't display (${codepoints(emoji)})`
}

/** The same swap inside HTML already in the page (formatted messages): text nodes are split, missing emoji replaced. */
export function replaceMissingEmoji(root: HTMLElement) {
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
  const nodes: Text[] = []
  for (let n = walker.nextNode(); n; n = walker.nextNode()) nodes.push(n as Text)
  for (const node of nodes) {
    if (node.parentElement?.closest('.emoji-missing')) continue
    const pieces = splitEmoji(node.data)
    if (pieces.length === 1 && !pieces[0].missing) continue
    const frag = document.createDocumentFragment()
    for (const p of pieces) {
      if (!p.missing) { frag.appendChild(document.createTextNode(p.text)); continue }
      const span = document.createElement('span')
      span.className = 'emoji-missing'
      span.title = missingTitle(p.text)
      span.innerHTML = EMOJI_MISSING_SVG   // a constant of ours — no user text goes in here
      frag.appendChild(span)
    }
    node.replaceWith(frag)
  }
}
