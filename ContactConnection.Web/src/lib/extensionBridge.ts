import { create } from 'zustand'

/**
 * Bridge to the ContactConnection Agent browser extension (S183, ContactConnection.Extension/). The page and the
 * extension's content script talk over window.postMessage:
 *   page → extension: hello (this is the portal) · focus (bring this tab forward) · capture on|off (report clicks)
 *   extension → page: loaded (just installed/updated — say hello) · ready (installed, version) · input (a click on a
 *   ContactConnection page; extensions ≥ 0.3.0 never send keys — 'key' stays in the type so older cues still render)
 * Without the extension everything here is a no-op and the portal works as before.
 */

const FROM_PAGE = 'cc-portal'
const FROM_EXT = 'cc-extension'

export interface ExtensionInputEvent {
  kind: 'click' | 'key'
  /** Agent machine's clock (Date.now()) when it happened. */
  t: number
  // click
  sx?: number
  sy?: number
  dpr?: number
  /** The screen the clicked page was on: availLeft / availTop / width / height in CSS pixels. */
  scr?: { l: number; t: number; w: number; h: number }
  // key
  label?: string
}

interface ExtensionState {
  installed: boolean
  version: string | null
}

export const useExtensionStore = create<ExtensionState>(() => ({ installed: false, version: null }))

const inputListeners = new Set<(e: ExtensionInputEvent) => void>()
let started = false

/** Announce the portal to the extension (safe to call repeatedly). The agent shell calls this once on mount. */
export function connectExtension() {
  if (!started) {
    started = true
    window.addEventListener('message', (e) => {
      if (e.source !== window || !e.data || e.data.source !== FROM_EXT) return
      if (e.data.type === 'ready') useExtensionStore.setState({ installed: true, version: e.data.version ?? null })
      // The extension was just installed or updated and loaded itself into this already-open page: introduce ourselves.
      else if (e.data.type === 'loaded') post({ type: 'hello' })
      else if (e.data.type === 'input' && e.data.event) inputListeners.forEach((fn) => fn(e.data.event as ExtensionInputEvent))
    })
  }
  // The content script loads at document_start, so it's listening by now; say hello again shortly in case it wasn't.
  post({ type: 'hello' })
  setTimeout(() => post({ type: 'hello' }), 1500)
}

function post(msg: Record<string, unknown>) {
  window.postMessage({ source: FROM_PAGE, ...msg }, window.location.origin)
}

/**
 * Work just landed in this portal (a call offer, auto-connect, script pop, a supervisor's call, a take over): bring the
 * tab and its window to the front. Only when the agent isn't already looking at it.
 */
export function requestPortalFocus(reason: string) {
  if (document.visibilityState === 'visible' && document.hasFocus()) return
  post({ type: 'focus', reason })
}

/** Ask ContactConnection pages to report the agent's clicks (only while a call is being screen recorded; never keystrokes). */
export function setInputCapture(on: boolean) {
  post({ type: 'capture', on })
}

export function onExtensionInput(fn: (e: ExtensionInputEvent) => void): () => void {
  inputListeners.add(fn)
  return () => { inputListeners.delete(fn) }
}
