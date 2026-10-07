// ContactConnection Agent — content script, on ContactConnection pages only (S183; see manifest "matches").
//
// On the agent portal: bridges window.postMessage ⇄ the extension (the page says hello / focus / capture on|off; the
// extension hands it the agent's click events).
// On ContactConnection pages: while the portal is recording a call, reports where the agent clicks (screen
// coordinates). Keystrokes are never captured — not on our pages, not anywhere.

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
        // The background decides from the tab's real address — only a ContactConnection portal is accepted.
        const r = await send({ cc: 'portal-hello' })
        if (!r?.ok) return
        isPortal = true
        window.postMessage({ source: FROM_EXT, type: 'ready', version: r.version ?? null }, window.location.origin)
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

  // ── Click capture ─────────────────────────────────────────────────────────
  const screenInfo = () => ({
    l: screen.availLeft ?? 0, t: screen.availTop ?? 0, w: screen.width, h: screen.height,
  })

  window.addEventListener('pointerdown', (e) => {
    if (!capturing || !e.isPrimary) return
    send({ cc: 'input', event: {
      kind: 'click', t: Date.now(), sx: e.screenX, sy: e.screenY, dpr: window.devicePixelRatio || 1, scr: screenInfo(),
    } })
  }, true)

})()
