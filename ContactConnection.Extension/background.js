// ContactConnection Agent — background service worker (S183).
//
// Two jobs:
//   1. Portal focus — web pages can't raise another tab; when the agent portal says "a call is here" (incoming call,
//      auto-connect, script pop, supervisor call, take over), bring that tab and its window to the front.
//   2. Click relay — while the portal is screen recording, ContactConnection pages report where the agent clicks (never
//      keystrokes); forward them to the portal tab, which places them on the recording's timeline.
//
// Only the real agent portal may act as one: a tab is accepted as a portal — and may ask for focus or switch input
// capture on — only when its address (sender.url, set by the browser, not by the page) is a ContactConnection site.
// (Content scripts only run on ContactConnection pages anyway; this is the second lock.)
//
// MV3 workers sleep between events, so state lives in chrome.storage.session (survives the worker, not the browser).

const STATE_KEY = 'ccState'   // { portalTabIds: number[], capturing: boolean }

/** ContactConnection portal addresses. Development hosts are included so local builds work unchanged. */
function isPortalUrl(url) {
  try {
    const u = new URL(url)
    const host = u.hostname.toLowerCase()
    if (u.protocol === 'https:' && (
      host === 'contactconnection.io' || host.endsWith('.contactconnection.io') ||
      host === 'contactconnection.cc' || host.endsWith('.contactconnection.cc'))) return true
    // Local development (Vite / preview, hosts-file tenants).
    return (u.protocol === 'http:' || u.protocol === 'https:')
      && (host === 'localhost' || host === '127.0.0.1' || host.endsWith('.hubion.local'))
  } catch {
    return false
  }
}

async function getState() {
  const s = (await chrome.storage.session.get(STATE_KEY))[STATE_KEY]
  return s ?? { portalTabIds: [], capturing: false }
}
async function setState(patch) {
  const next = { ...(await getState()), ...patch }
  await chrome.storage.session.set({ [STATE_KEY]: next })
  await showBadge(next.capturing)
  return next
}

/** A red REC on the toolbar icon whenever clicks are being reported — the agent can always see it. */
async function showBadge(capturing) {
  await chrome.action.setBadgeText({ text: capturing ? 'REC' : '' })
  if (capturing) await chrome.action.setBadgeBackgroundColor({ color: '#DC2626' })
  await chrome.action.setTitle({ title: capturing ? 'ContactConnection Agent — recording this call (clicks on ContactConnection pages)' : 'ContactConnection Agent' })
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
    const fromPortalSite = sender.frameId === 0 && !!sender.tab?.id && isPortalUrl(sender.url ?? sender.tab?.url ?? '')
    const fromKnownPortal = fromPortalSite && state.portalTabIds.includes(sender.tab.id)
    switch (msg.cc) {
      case 'portal-hello': {
        if (!fromPortalSite) { sendResponse({ ok: false }); return }
        if (!state.portalTabIds.includes(sender.tab.id))
          await setState({ portalTabIds: [...state.portalTabIds, sender.tab.id] })
        sendResponse({ ok: true, version: chrome.runtime.getManifest().version, capturing: state.capturing })
        return
      }
      case 'focus':
        if (fromKnownPortal) await focusTab(sender.tab)
        sendResponse({ ok: fromKnownPortal })
        return
      case 'capture': {
        if (!fromKnownPortal) { sendResponse({ ok: false }); return }
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

/** A portal tab that navigates away from the portal stops being one. */
chrome.tabs.onUpdated.addListener(async (tabId, info) => {
  if (!info.url) return
  const state = await getState()
  if (state.portalTabIds.includes(tabId) && !isPortalUrl(info.url)) await dropPortal(tabId)
})

chrome.tabs.onRemoved.addListener((tabId) => { void dropPortal(tabId) })

async function dropPortal(tabId) {
  const state = await getState()
  if (!state.portalTabIds.includes(tabId)) return
  const portalTabIds = state.portalTabIds.filter((id) => id !== tabId)
  // The last portal closed — nothing is recording any more.
  const next = await setState({ portalTabIds, capturing: portalTabIds.length > 0 && state.capturing })
  if (!next.capturing) await broadcastCapture(false)
}

// A fresh browser session starts with nothing recording.
chrome.runtime.onStartup.addListener(() => { void setState({ portalTabIds: [], capturing: false }) })
chrome.runtime.onInstalled.addListener(() => {
  void setState({ portalTabIds: [], capturing: false })
  void loadIntoOpenTabs()
})

/**
 * Chrome doesn't load content scripts into tabs that were open before an install or update, so the agent would have to
 * reload the portal (mid-shift, maybe mid-call). Load it into the ContactConnection tabs that are already open instead.
 */
async function loadIntoOpenTabs() {
  const { content_scripts: [cs] } = chrome.runtime.getManifest()
  const tabs = await chrome.tabs.query({ url: cs.matches })
  for (const t of tabs) {
    if (!t.id || t.discarded) continue
    chrome.scripting.executeScript({ target: { tabId: t.id, allFrames: true }, files: cs.js }).catch(() => {})
  }
}
