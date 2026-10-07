// ContactConnection Agent — background service worker (S183).
//
// Two jobs:
//   1. Portal focus — web pages can't raise another tab; when the agent portal says "a call is here" (incoming call,
//      auto-connect, script pop, supervisor call, take over), bring that tab and its window to the front.
//   2. Input relay — while the portal is screen recording, every page's content script reports the agent's clicks and
//      keys; forward them to the portal tab, which places them on the recording's timeline.
//
// MV3 workers sleep between events, so state lives in chrome.storage.session (survives the worker, not the browser).

const STATE_KEY = 'ccState'   // { portalTabIds: number[], capturing: boolean }

async function getState() {
  const s = (await chrome.storage.session.get(STATE_KEY))[STATE_KEY]
  return s ?? { portalTabIds: [], capturing: false }
}
async function setState(patch) {
  const next = { ...(await getState()), ...patch }
  await chrome.storage.session.set({ [STATE_KEY]: next })
  return next
}

async function focusTab(tab) {
  if (!tab?.id) return
  await chrome.tabs.update(tab.id, { active: true })
  const win = await chrome.windows.get(tab.windowId)
  // Windows may refuse to steal focus from another app; drawAttention flashes the taskbar button instead.
  await chrome.windows.update(tab.windowId, {
    focused: true,
    drawAttention: true,
    ...(win.state === 'minimized' ? { state: 'normal' } : {}),
  })
}

/** Tell every frame of every tab whether to report input (only while the portal is recording). */
async function broadcastCapture(capturing) {
  const tabs = await chrome.tabs.query({})
  for (const t of tabs) {
    if (t.id) chrome.tabs.sendMessage(t.id, { cc: 'capture', on: capturing }).catch(() => {})
  }
}

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!msg || typeof msg.cc !== 'string') return
  ;(async () => {
    const state = await getState()
    switch (msg.cc) {
      case 'portal-hello': {
        // The page announcing itself is the agent portal — remember its tab (top frame only).
        if (sender.tab?.id && sender.frameId === 0 && !state.portalTabIds.includes(sender.tab.id))
          await setState({ portalTabIds: [...state.portalTabIds, sender.tab.id] })
        sendResponse({ ok: true, version: chrome.runtime.getManifest().version, capturing: state.capturing })
        return
      }
      case 'focus':
        await focusTab(sender.tab)
        sendResponse({ ok: true })
        return
      case 'capture': {
        const next = await setState({ capturing: !!msg.on })
        await broadcastCapture(next.capturing)
        sendResponse({ ok: true })
        return
      }
      case 'capture-state':
        sendResponse({ capturing: state.capturing })
        return
      case 'input': {
        if (!state.capturing) return
        for (const id of state.portalTabIds)
          chrome.tabs.sendMessage(id, { cc: 'input', event: msg.event }, { frameId: 0 }).catch(() => {})
        return
      }
    }
  })()
  return true   // async sendResponse
})

chrome.tabs.onRemoved.addListener(async (tabId) => {
  const state = await getState()
  if (!state.portalTabIds.includes(tabId)) return
  const portalTabIds = state.portalTabIds.filter((id) => id !== tabId)
  // The last portal closed — nothing is recording any more.
  const next = await setState({ portalTabIds, capturing: portalTabIds.length > 0 && state.capturing })
  if (!next.capturing) await broadcastCapture(false)
})
