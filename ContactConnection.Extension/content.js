// ContactConnection Agent — content script, in every frame of every page (S183).
//
// On the agent portal: bridges window.postMessage ⇄ the extension (the page says hello / focus / capture on|off; the
// extension hands it the agent's input events).
// On every page: while the portal is recording, reports clicks (with screen coordinates) and key labels. Typed
// characters in password and card fields are reported as "•", never their value.

(() => {
  if (window.__ccAgentContent) return
  window.__ccAgentContent = true

  const FROM_PAGE = 'cc-portal'
  const FROM_EXT  = 'cc-extension'
  const isTop = window === window.top
  let capturing = false
  let isPortal = false

  // After the extension is reloaded or updated, an old page's chrome.runtime is gone — swallow that, never break the page.
  const send = (msg) => {
    try { return chrome.runtime.sendMessage(msg).catch(() => undefined) } catch { return Promise.resolve(undefined) }
  }

  // ── Portal bridge (top frame only) ────────────────────────────────────────
  if (isTop) {
    window.addEventListener('message', async (e) => {
      if (e.source !== window || !e.data || e.data.source !== FROM_PAGE) return
      const { type } = e.data
      if (type === 'hello') {
        isPortal = true
        const r = await send({ cc: 'portal-hello' })
        window.postMessage({ source: FROM_EXT, type: 'ready', version: r?.version ?? null }, window.location.origin)
      } else if (type === 'focus') {
        await send({ cc: 'focus', reason: e.data.reason ?? null })
      } else if (type === 'capture') {
        await send({ cc: 'capture', on: !!e.data.on })
      }
    })
  }

  chrome.runtime.onMessage.addListener((msg) => {
    if (msg?.cc === 'capture') capturing = !!msg.on
    else if (msg?.cc === 'input' && isTop && isPortal)
      window.postMessage({ source: FROM_EXT, type: 'input', event: msg.event }, window.location.origin)
  })

  // A page opened mid-recording asks whether to report.
  send({ cc: 'capture-state' }).then((r) => { capturing = !!r?.capturing }).catch(() => {})

  // ── Input capture ─────────────────────────────────────────────────────────
  const screenInfo = () => ({
    l: screen.availLeft ?? 0, t: screen.availTop ?? 0, w: screen.width, h: screen.height,
  })

  window.addEventListener('pointerdown', (e) => {
    if (!capturing || !e.isPrimary) return
    send({ cc: 'input', event: {
      kind: 'click', t: Date.now(), sx: e.screenX, sy: e.screenY, dpr: window.devicePixelRatio || 1, scr: screenInfo(),
    } })
  }, true)

  const NAMED = {
    Enter: 'Enter', Tab: 'Tab', Backspace: 'Bksp', Delete: 'Del', Escape: 'Esc', ' ': 'Space',
    ArrowLeft: '←', ArrowRight: '→', ArrowUp: '↑', ArrowDown: '↓', Home: 'Home', End: 'End', PageUp: 'PgUp', PageDown: 'PgDn',
  }
  const MODIFIER_ONLY = new Set(['Shift', 'Control', 'Alt', 'Meta', 'CapsLock', 'AltGraph', 'Fn', 'OS'])

  function isSecret(el) {
    if (!el || el.tagName !== 'INPUT') return false
    const type = (el.getAttribute('type') || '').toLowerCase()
    const ac = (el.getAttribute('autocomplete') || '').toLowerCase()
    return type === 'password' || ac.startsWith('cc-') || ac === 'current-password' || ac === 'new-password'
  }

  window.addEventListener('keydown', (e) => {
    if (!capturing || e.repeat || MODIFIER_ONLY.has(e.key)) return
    const mods = [e.ctrlKey && 'Ctrl', e.altKey && 'Alt', e.metaKey && 'Win'].filter(Boolean)
    let label
    if (e.key.length === 1 && mods.length === 0) label = isSecret(e.target) ? '•' : e.key
    else label = [...mods, NAMED[e.key] ?? (e.key.length === 1 ? e.key.toUpperCase() : e.key)].join('+')
    if (label === ' ') label = 'Space'
    send({ cc: 'input', event: { kind: 'key', t: Date.now(), label } })
  }, true)
})()
