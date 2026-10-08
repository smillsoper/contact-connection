import type { MouseEvent } from 'react'

/**
 * Links in rich content (scripts, help desk topics, chat) always open in a new window: following one in place would
 * navigate the portal away and drop the agent's call. Attach to the content's container as onClick and onAuxClick.
 */
export function openLinkInNewWindow(e: MouseEvent) {
  const a = (e.target as HTMLElement).closest('a[href]')
  if (!a) return
  e.preventDefault()
  const href = a.getAttribute('href') ?? ''
  if (/^(https?:|mailto:)/i.test(href)) window.open(href, '_blank', 'noopener,noreferrer')
}
