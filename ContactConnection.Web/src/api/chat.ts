import { api } from './client'

// Team chat (S183) — see ChatEndpoints.cs.

export interface ChatUserState { code: string; label: string; since: string }

export interface ChatUser {
  id: string
  name: string
  email: string
  roleName: string | null
  state: ChatUserState | null
}

export interface ChatChannel {
  id: string
  kind: 'channel' | 'dm'
  name: string
  description: string | null
  isPrivate: boolean
  postingRestricted: boolean
  posterIds: string[]
  posterRoleIds: string[]
  pinnerIds: string[]
  pinnerRoleIds: string[]
  /** May pin for everyone here (computed for the viewer; recompute with canPin() for pushed updates). */
  canPin: boolean
  moderatorIds: string[]
  moderatorRoleIds: string[]
  /** May delete other people's messages here. */
  canDeleteAny: boolean
  membershipLocked: boolean
  retired: boolean
  lastMessageAt: string | null
  lastReadAt: string
  memberIds: string[]
  canPost: boolean
  postBlockedReason: string | null
  canLeave: boolean
  unread: number
  mentions: number
}

export interface ChatReactionGroup { emoji: string; agentIds: string[] }

export interface ChatMessage {
  id: string
  channelId: string
  agentId: string | null
  kind: 'user' | 'system'
  parentId: string | null
  body: string
  mentionIds: string[]
  replyCount: number
  lastReplyAt: string | null
  createdAt: string
  editedAt: string | null
  deleted: boolean
  /** Pinned for everyone in the channel. */
  pinnedAt: string | null
  pinnedById: string | null
  reactions: ChatReactionGroup[]
}

export interface ChatPins { everyone: ChatMessage[]; mine: ChatMessage[] }

export interface HelpRequest {
  id: string
  agentId: string
  agentName: string | null
  status: 'open' | 'claimed' | 'cancelled'
  note: string | null
  callRecordId: string | null
  callerNumber: string | null
  campaignName: string | null
  wentToAllSupervisors: boolean
  notifiedIds: string[]
  claimedById: string | null
  claimedByName: string | null
  channelId: string | null
  createdAt: string
  closedAt: string | null
}

export interface ChatBootstrap {
  enabled: boolean
  message?: string
  me: { id: string; isManager: boolean; isSupervisor: boolean; roleId: string | null }
  users: ChatUser[]
  channels: ChatChannel[]
  supervisorIds: string[]
  myHelp: HelpRequest | null
  helpQueue: HelpRequest[]
}

export interface BrowseChannel { id: string; name: string; description: string | null; retired: boolean; memberCount: number }

export const chatApi = {
  bootstrap: () => api.get<ChatBootstrap>('/api/v1/chat/bootstrap'),
  browse: () => api.get<BrowseChannel[]>('/api/v1/chat/channels/browse'),
  join: (id: string) => api.post<ChatChannel>(`/api/v1/chat/channels/${id}/join`),
  leave: (id: string) => api.post<void>(`/api/v1/chat/channels/${id}/leave`),
  messages: (id: string, before?: string) =>
    api.get<{ messages: ChatMessage[]; hasMore: boolean }>(`/api/v1/chat/channels/${id}/messages${before ? `?before=${encodeURIComponent(before)}` : ''}`),
  thread: (messageId: string) => api.get<{ parent: ChatMessage; replies: ChatMessage[] }>(`/api/v1/chat/messages/${messageId}/thread`),
  post: (channelId: string, body: string, parentId?: string | null) =>
    api.post<ChatMessage>(`/api/v1/chat/channels/${channelId}/messages`, { body, parentId: parentId ?? null }),
  edit: (messageId: string, body: string) => api.patch<ChatMessage>(`/api/v1/chat/messages/${messageId}`, { body }),
  remove: (messageId: string) => api.delete<void>(`/api/v1/chat/messages/${messageId}`),
  pins: (channelId: string) => api.get<ChatPins>(`/api/v1/chat/channels/${channelId}/pins`),
  pin: (messageId: string, scope: 'me' | 'everyone') => api.post<unknown>(`/api/v1/chat/messages/${messageId}/pin`, { scope }),
  unpin: (messageId: string, scope: 'me' | 'everyone') => api.delete<unknown>(`/api/v1/chat/messages/${messageId}/pin?scope=${scope}`),
  react: (messageId: string, emoji: string) => api.post<ChatMessage>(`/api/v1/chat/messages/${messageId}/reactions`, { emoji }),
  read: (channelId: string) => api.post<void>(`/api/v1/chat/channels/${channelId}/read`),
  typing: (channelId: string) => api.post<void>(`/api/v1/chat/channels/${channelId}/typing`),
  openDirect: (agentIds: string[]) => api.post<ChatChannel>('/api/v1/chat/direct', { agentIds }),
  search: (q: string) => api.get<ChatMessage[]>(`/api/v1/chat/search?q=${encodeURIComponent(q)}`),
  raiseHand: (note: string | null, callRecordId: string | null) => api.post<HelpRequest>('/api/v1/chat/help', { note, callRecordId }),
  claimHelp: (id: string) => api.post<HelpRequest>(`/api/v1/chat/help/${id}/claim`),
  cancelHelp: (id: string) => api.post<HelpRequest>(`/api/v1/chat/help/${id}/cancel`),
}

// ── Configuration (chat.manage) ──────────────────────────────────────────────

export interface AdminChatChannel {
  id: string
  name: string
  description: string | null
  isPrivate: boolean
  postingRestricted: boolean
  posterIds: string[]
  posterRoleIds: string[]
  pinnerIds: string[]
  pinnerRoleIds: string[]
  moderatorIds: string[]
  moderatorRoleIds: string[]
  membershipLocked: boolean
  assignedRoleIds: string[]
  retired: boolean
  retiredAt: string | null
  createdAt: string
  lastMessageAt: string | null
  assignedIds: string[]
  memberCount: number
}

export interface SaveChatChannel {
  name: string
  description: string | null
  isPrivate: boolean
  postingRestricted: boolean
  posterIds: string[]
  posterRoleIds: string[]
  pinnerIds: string[]
  pinnerRoleIds: string[]
  moderatorIds: string[]
  moderatorRoleIds: string[]
  membershipLocked: boolean
  assignedRoleIds: string[]
  assignedIds: string[]
}

export const chatAdminApi = {
  list: () => api.get<{ enabled: boolean; channels: AdminChatChannel[] }>('/api/v1/chat/admin/channels'),
  create: (body: SaveChatChannel) => api.post<{ id: string }>('/api/v1/chat/admin/channels', body),
  update: (id: string, body: SaveChatChannel) => api.put<{ id: string }>(`/api/v1/chat/admin/channels/${id}`, body),
  retire: (id: string) => api.post<void>(`/api/v1/chat/admin/channels/${id}/retire`),
  unretire: (id: string) => api.post<void>(`/api/v1/chat/admin/channels/${id}/unretire`),
  getSupervisors: (agentId: string) => api.get<string[]>(`/api/v1/admin/agents/${agentId}/supervisors`),
  setSupervisors: (agentId: string, supervisorIds: string[]) =>
    api.put<string[]>(`/api/v1/admin/agents/${agentId}/supervisors`, { supervisorIds }),
}

/** Colour + wording for a live agent state. */
export function stateStyle(state: ChatUserState | null): { dot: string; text: string } {
  const code = state?.code ?? 'logged_out'
  if (code === 'available') return { dot: 'bg-emerald-400', text: state!.label }
  if (code === 'on_call' || code === 'callback_pending') return { dot: 'bg-red-400', text: state!.label }
  if (code === 'acw') return { dot: 'bg-amber-400', text: state!.label }
  if (code === 'logged_out') return { dot: 'border border-gray-500 bg-transparent', text: 'Signed out' }
  return { dot: 'bg-gray-400', text: state?.label ?? 'Unavailable' }
}
