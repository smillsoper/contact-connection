import { create } from 'zustand'
import type { ChatBootstrap, ChatChannel, ChatMessage, ChatUser, ChatUserState, HelpRequest } from '../api/chat'

/**
 * Team chat client state (S183). One SignalR connection per page (lib/chatConnection) feeds it; the chat panel in the
 * agent portal and the launcher on admin pages both read it.
 */

export type ChatView =
  | { kind: 'list' }
  | { kind: 'channel'; channelId: string }
  | { kind: 'thread'; channelId: string; parentId: string }
  | { kind: 'new-dm' }
  | { kind: 'browse' }
  | { kind: 'search' }

interface ChannelMessages { messages: ChatMessage[]; hasMore: boolean; loaded: boolean }

interface ChatState {
  status: 'idle' | 'loading' | 'ready' | 'disabled' | 'error'
  disabledMessage: string | null
  me: ChatBootstrap['me'] | null
  users: Record<string, ChatUser>
  channels: Record<string, ChatChannel>
  supervisorIds: string[]
  myHelp: HelpRequest | null
  helpQueue: HelpRequest[]
  messages: Record<string, ChannelMessages>
  threads: Record<string, ChatMessage[]>       // parentId → replies
  typing: Record<string, Record<string, number>> // channelId → agentId → expires (ms)
  view: ChatView
  /** Is the chat UI on screen (portal panel always; admin launcher when open)? */
  visible: boolean

  load: (b: ChatBootstrap) => void
  setDisabled: (message: string) => void
  setView: (v: ChatView) => void
  setVisible: (v: boolean) => void
  upsertChannel: (c: ChatChannel) => void
  removeChannel: (id: string) => void
  setMembers: (channelId: string, memberIds: string[]) => void
  setPage: (channelId: string, messages: ChatMessage[], hasMore: boolean, older: boolean) => void
  setThread: (parentId: string, replies: ChatMessage[]) => void
  addMessage: (m: ChatMessage) => void
  updateMessage: (m: ChatMessage) => void
  markRead: (channelId: string, at: string) => void
  setPresence: (agentId: string, state: ChatUserState) => void
  setTyping: (channelId: string, agentId: string) => void
  setHelp: (h: HelpRequest) => void
}

const sortMsgs = (a: ChatMessage, b: ChatMessage) => a.createdAt.localeCompare(b.createdAt)

export const useChatStore = create<ChatState>((set, get) => ({
  status: 'idle', disabledMessage: null, me: null, users: {}, channels: {}, supervisorIds: [], myHelp: null, helpQueue: [],
  messages: {}, threads: {}, typing: {}, view: { kind: 'list' }, visible: false,

  load: (b) => set({
    status: 'ready', me: b.me,
    users: Object.fromEntries(b.users.map((u) => [u.id, u])),
    channels: Object.fromEntries(b.channels.map((c) => [c.id, c])),
    supervisorIds: b.supervisorIds, myHelp: b.myHelp, helpQueue: b.helpQueue,
  }),
  setDisabled: (message) => set({ status: 'disabled', disabledMessage: message }),
  setView: (view) => set({ view }),
  setVisible: (visible) => set({ visible }),

  upsertChannel: (c) => set((s) => ({ channels: { ...s.channels, [c.id]: { ...s.channels[c.id], ...c } } })),
  removeChannel: (id) => set((s) => {
    const channels = { ...s.channels }; delete channels[id]
    const view = 'channelId' in s.view && s.view.channelId === id ? { kind: 'list' as const } : s.view
    return { channels, view }
  }),
  setMembers: (channelId, memberIds) => set((s) => s.channels[channelId]
    ? { channels: { ...s.channels, [channelId]: { ...s.channels[channelId], memberIds } } } : s),

  setPage: (channelId, page, hasMore, older) => set((s) => {
    const cur = s.messages[channelId]
    const merged = older && cur ? [...page, ...cur.messages] : page
    const seen = new Set<string>()
    const messages = merged.filter((m) => (seen.has(m.id) ? false : (seen.add(m.id), true))).sort(sortMsgs)
    return { messages: { ...s.messages, [channelId]: { messages, hasMore, loaded: true } } }
  }),
  setThread: (parentId, replies) => set((s) => ({ threads: { ...s.threads, [parentId]: [...replies].sort(sortMsgs) } })),

  addMessage: (m) => set((s) => {
    const patch: Partial<ChatState> = {}
    if (m.parentId) {
      const t = s.threads[m.parentId]
      if (t && !t.some((x) => x.id === m.id)) patch.threads = { ...s.threads, [m.parentId]: [...t, m].sort(sortMsgs) }
    } else {
      const cur = s.messages[m.channelId]
      if (cur && !cur.messages.some((x) => x.id === m.id))
        patch.messages = { ...s.messages, [m.channelId]: { ...cur, messages: [...cur.messages, m].sort(sortMsgs) } }
    }
    const c = s.channels[m.channelId]
    if (c) {
      const mine = m.agentId === s.me?.id
      const viewing = s.visible && document.visibilityState === 'visible'
        && ((s.view.kind === 'channel' && s.view.channelId === m.channelId) || (s.view.kind === 'thread' && s.view.parentId === m.parentId))
      patch.channels = {
        ...s.channels,
        [c.id]: {
          ...c, lastMessageAt: m.createdAt,
          unread: mine || viewing || m.parentId ? c.unread : c.unread + 1,
          mentions: !mine && !viewing && s.me && m.mentionIds.includes(s.me.id) ? c.mentions + 1 : c.mentions,
          lastReadAt: mine ? m.createdAt : c.lastReadAt,
        },
      }
    }
    return patch
  }),
  updateMessage: (m) => set((s) => {
    const patch: Partial<ChatState> = {}
    const cur = s.messages[m.channelId]
    if (cur?.messages.some((x) => x.id === m.id))
      patch.messages = { ...s.messages, [m.channelId]: { ...cur, messages: cur.messages.map((x) => (x.id === m.id ? m : x)) } }
    if (m.parentId && s.threads[m.parentId]?.some((x) => x.id === m.id))
      patch.threads = { ...s.threads, [m.parentId]: s.threads[m.parentId].map((x) => (x.id === m.id ? m : x)) }
    return patch
  }),
  markRead: (channelId, at) => set((s) => s.channels[channelId]
    ? { channels: { ...s.channels, [channelId]: { ...s.channels[channelId], unread: 0, mentions: 0, lastReadAt: at } } } : s),

  setPresence: (agentId, state) => set((s) => s.users[agentId]
    ? { users: { ...s.users, [agentId]: { ...s.users[agentId], state } } } : s),
  setTyping: (channelId, agentId) => set((s) => ({
    typing: { ...s.typing, [channelId]: { ...(s.typing[channelId] ?? {}), [agentId]: Date.now() + 4000 } },
  })),

  setHelp: (h) => set((s) => {
    const meId = get().me?.id
    const myHelp = h.agentId === meId ? (h.status === 'open' ? h : (s.myHelp?.id === h.id ? h : s.myHelp)) : s.myHelp
    const others = s.helpQueue.filter((x) => x.id !== h.id)
    const helpQueue = h.agentId !== meId && h.status === 'open' ? [...others, h] : others
    return { myHelp, helpQueue }
  }),
}))

/** Unread badge total (mentions and DMs count; channel chatter counts too). */
export function useChatUnread() {
  return useChatStore((s) => Object.values(s.channels).reduce((n, c) => n + (c.unread > 0 ? c.unread : 0), 0))
}

export function channelTitle(c: ChatChannel, users: Record<string, ChatUser>, meId: string | undefined) {
  if (c.kind === 'channel') return `# ${c.name}`
  const others = c.memberIds.filter((id) => id !== meId).map((id) => users[id]?.name ?? 'Someone')
  return others.length ? others.join(', ') : 'Just you'
}

/** canPost from the flags with this viewer's own role (pushed channel updates are computed without it). */
export function canPost(c: ChatChannel, meId: string | undefined, isManager: boolean) {
  if (c.retired) return false
  if (c.kind === 'dm') return true
  return !c.postingRestricted || isManager || (!!meId && c.posterIds.includes(meId))
}
