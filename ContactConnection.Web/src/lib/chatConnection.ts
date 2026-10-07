import * as signalR from '@microsoft/signalr'
import { chatApi, type ChatChannel, type ChatMessage, type ChatUserState, type HelpRequest } from '../api/chat'
import { useAuthStore } from '../stores/authStore'
import { useChatStore, channelTitle } from '../stores/chatStore'
import { getSubdomainFromHostname } from '../utils/subdomain'

/**
 * The page's one team-chat connection (S183). Started by whichever chat surface mounts first (agent portal panel or the
 * admin launcher) and kept for the page's life so badges, help alerts and status stay live even with the panel closed.
 * After a reconnect everything is reloaded — pushes sent while disconnected are gone.
 */

let connection: signalR.HubConnection | null = null
let starting: Promise<void> | null = null

export async function reloadChat() {
  const store = useChatStore.getState()
  try {
    const b = await chatApi.bootstrap()
    if (!b.enabled) { store.setDisabled(b.message ?? 'Team chat is not enabled.'); return }
    store.load(b)
    // Refresh the conversation on screen.
    const v = useChatStore.getState().view
    if (v.kind === 'channel' || v.kind === 'thread') {
      const page = await chatApi.messages(v.channelId)
      useChatStore.getState().setPage(v.channelId, page.messages, page.hasMore, false)
      if (v.kind === 'thread') {
        const t = await chatApi.thread(v.parentId)
        useChatStore.getState().setThread(v.parentId, t.replies)
      }
    }
  } catch {
    if (useChatStore.getState().status !== 'ready') useChatStore.setState({ status: 'error' })
  }
}

export function startChat() {
  if (starting) return starting
  const { token, tenantSubdomain } = useAuthStore.getState()
  if (!token) return Promise.resolve()
  useChatStore.setState({ status: 'loading' })
  starting = (async () => {
    await reloadChat()
    if (useChatStore.getState().status === 'disabled') return
    connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/chat?access_token=${token}`, {
        headers: { 'X-Tenant-Subdomain': getSubdomainFromHostname() ?? tenantSubdomain ?? '' },
      })
      .withAutomaticReconnect()
      .build()
    connection.on('receiveChatEvent', (type: string, json: string) => handle(type, JSON.parse(json)))
    connection.onreconnected(() => { void reloadChat() })
    try { await connection.start() } catch { /* the page still works; badges just won't be live */ }
  })()
  return starting
}

function handle(type: string, p: unknown) {
  const s = useChatStore.getState()
  switch (type) {
    case 'message': {
      const m = p as ChatMessage
      s.addMessage(m)
      alertForMessage(m)
      break
    }
    case 'message-updated': s.updateMessage(p as ChatMessage); break
    case 'channel': s.upsertChannel(p as ChatChannel); break
    case 'channel-removed': s.removeChannel((p as { channelId: string }).channelId); break
    case 'members': { const x = p as { channelId: string; memberIds: string[] }; s.setMembers(x.channelId, x.memberIds); break }
    case 'read': { const x = p as { channelId: string; at: string }; s.markRead(x.channelId, x.at); break }
    case 'typing': { const x = p as { channelId: string; agentId: string }; s.setTyping(x.channelId, x.agentId); break }
    case 'presence': {
      const x = p as { agentId: string; code: string; label: string; since: string }
      s.setPresence(x.agentId, { code: x.code, label: x.label, since: x.since } as ChatUserState)
      break
    }
    case 'help': {
      const h = p as HelpRequest
      const before = s.helpQueue.some((x) => x.id === h.id)
      s.setHelp(h)
      if (h.status === 'open' && h.agentId !== s.me?.id && !before) {
        chime(true)
        notify('Help requested', `${h.agentName ?? 'An agent'} needs help${h.note ? `: ${h.note}` : ''}`, `help-${h.id}`)
      }
      if (h.status === 'claimed' && h.agentId === s.me?.id) {
        chime(false)
        notify('Help is on the way', `${h.claimedByName ?? 'A supervisor'} picked up your request.`, `help-${h.id}`)
        if (h.channelId) useChatStore.setState({ view: { kind: 'channel', channelId: h.channelId } })
      }
      break
    }
  }
}

// ── Alerts ──────────────────────────────────────────────────────────────────

function alertForMessage(m: ChatMessage) {
  const s = useChatStore.getState()
  if (!s.me || m.agentId === s.me.id) return
  const c = s.channels[m.channelId]
  if (!c) return
  const viewing = s.visible && document.visibilityState === 'visible'
    && ((s.view.kind === 'channel' && s.view.channelId === m.channelId) || (s.view.kind === 'thread' && s.view.parentId === m.parentId))
  const direct = c.kind === 'dm'
  const mentioned = m.mentionIds.includes(s.me.id)
  if (viewing || !(direct || mentioned)) return
  chime(false)
  const who = m.agentId ? s.users[m.agentId]?.name ?? 'Someone' : 'ContactConnection'
  notify(direct ? who : `${who} in ${channelTitle(c, s.users, s.me.id)}`, plainText(m.body, s.users), `chat-${m.channelId}`)
}

export function plainText(body: string, users: Record<string, { name: string }>) {
  return body.replace(/<@([0-9a-f-]{36})>/gi, (_, id: string) => `@${users[id]?.name ?? 'someone'}`)
}

let audio: AudioContext | null = null
/** A soft two-note chime (help requests get a third, higher note). No audio files involved. */
function chime(urgent: boolean) {
  try {
    audio ??= new AudioContext()
    const notes = urgent ? [660, 880, 1100] : [660, 880]
    notes.forEach((f, i) => {
      const o = audio!.createOscillator(), g = audio!.createGain()
      o.frequency.value = f
      g.gain.setValueAtTime(0.0001, audio!.currentTime + i * 0.12)
      g.gain.exponentialRampToValueAtTime(0.15, audio!.currentTime + i * 0.12 + 0.02)
      g.gain.exponentialRampToValueAtTime(0.0001, audio!.currentTime + i * 0.12 + 0.25)
      o.connect(g).connect(audio!.destination)
      o.start(audio!.currentTime + i * 0.12); o.stop(audio!.currentTime + i * 0.12 + 0.3)
    })
  } catch { /* no audio — fine */ }
}

function notify(title: string, body: string, tag: string) {
  if (document.visibilityState === 'visible' && document.hasFocus()) return
  if (!('Notification' in window) || Notification.permission !== 'granted') return
  try {
    const n = new Notification(title, { body: body.slice(0, 200), tag })
    n.onclick = () => { window.focus(); n.close() }
  } catch { /* some browsers only allow notifications from a service worker */ }
}

export function canAskForNotifications() {
  return 'Notification' in window && Notification.permission === 'default'
}
export async function askForNotifications() {
  if ('Notification' in window) await Notification.requestPermission()
}
