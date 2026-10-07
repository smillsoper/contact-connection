import { Fragment, useEffect, useMemo, useRef, useState } from 'react'
import { chatApi, stateStyle, type ChatMessage, type ChatUser } from '../../api/chat'
import { useChatStore, channelTitle, canPost, canPinForEveryone, canDeleteOthers } from '../../stores/chatStore'
import DOMPurify from 'dompurify'
import { plainText, messagePreview } from '../../lib/chatConnection'
import { loadChatImage } from '../../lib/chatImages'
import ChatEditor, { type ChatEditorHandle } from './ChatEditor'

const QUICK_REACTIONS = ['👍', '❤️', '😂', '🎉', '👀', '✅']

// ── Conversation (a channel or DM) ───────────────────────────────────────────

export function Conversation({ channelId }: { channelId: string }) {
  const channel = useChatStore((s) => s.channels[channelId])
  const page = useChatStore((s) => s.messages[channelId])
  const users = useChatStore((s) => s.users)
  const me = useChatStore((s) => s.me)
  const visible = useChatStore((s) => s.visible)
  const setView = useChatStore((s) => s.setView)
  const wide = useChatStore((s) => s.wide)
  const [error, setError] = useState<string | null>(null)
  const [loadingOlder, setLoadingOlder] = useState(false)
  const scrollRef = useRef<HTMLDivElement>(null)
  const stickToBottom = useRef(true)

  useEffect(() => {
    if (page?.loaded) return
    chatApi.messages(channelId)
      .then((r) => useChatStore.getState().setPage(channelId, r.messages, r.hasMore, false))
      .catch((e: Error) => setError(e.message))
  }, [channelId, page?.loaded])

  // Read while on screen: on open and whenever something new arrives.
  const lastId = page?.messages[page.messages.length - 1]?.id
  useEffect(() => {
    if (!visible || document.visibilityState !== 'visible' || !channel) return
    if (channel.unread === 0 && channel.mentions === 0 && lastId === undefined) return
    const t = setTimeout(() => {
      useChatStore.getState().markRead(channelId, new Date().toISOString())
      chatApi.read(channelId).catch(() => {})
    }, 400)
    return () => clearTimeout(t)
  }, [channelId, lastId, visible, channel?.unread]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    const el = scrollRef.current
    if (el && stickToBottom.current) el.scrollTop = el.scrollHeight
  }, [lastId, page?.messages.length])

  async function older() {
    const first = page?.messages[0]
    if (!first || loadingOlder) return
    setLoadingOlder(true)
    const el = scrollRef.current
    const prevHeight = el?.scrollHeight ?? 0
    try {
      const r = await chatApi.messages(channelId, first.createdAt)
      useChatStore.getState().setPage(channelId, r.messages, r.hasMore, true)
      requestAnimationFrame(() => { if (el) el.scrollTop = el.scrollHeight - prevHeight })
    } finally { setLoadingOlder(false) }
  }

  if (!channel) return <p className="p-4 text-xs text-gray-500">That conversation isn't available.</p>
  const title = channelTitle(channel, users, me?.id)
  const others = channel.memberIds.filter((id) => id !== me?.id)
  const single = channel.kind === 'dm' && others.length === 1 ? users[others[0]] : null
  const allowed = canPost(channel, me?.id, !!me?.isManager, me?.roleId)

  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="px-3 py-2 border-b border-gray-800 flex items-center gap-2 shrink-0">
        {!wide && <button onClick={() => setView({ kind: 'list' })} className="text-gray-400 hover:text-white text-sm" title="Back">←</button>}
        <div className="min-w-0 flex-1">
          <p className="text-sm text-white font-medium truncate" title={title}>{title}</p>
          {single ? <StateLine user={single} /> : (
            <p className="text-[11px] text-gray-500 truncate">
              {channel.kind === 'channel' && channel.description ? channel.description : `${channel.memberIds.length} members`}
            </p>
          )}
        </div>
        {channel.canLeave && (
          <button onClick={() => chatApi.leave(channelId).catch((e: Error) => setError(e.message))}
            className="text-[11px] text-gray-500 hover:text-red-300" title="Leave this channel">Leave</button>
        )}
      </div>

      <PinnedBar channelId={channelId} onJump={(m) => {
        if (m.parentId) { setView({ kind: 'thread', channelId, parentId: m.parentId }); return }
        const el = document.getElementById(`chat-msg-${m.id}`)
        if (el) { el.scrollIntoView({ block: 'center' }); el.classList.add('ring-1', 'ring-amber-400'); setTimeout(() => el.classList.remove('ring-1', 'ring-amber-400'), 1500) }
        else setView({ kind: 'thread', channelId, parentId: m.id })   // older than the loaded page — open it on its own
      }} />

      <div ref={scrollRef} className="flex-1 min-h-0 overflow-y-auto px-3 py-2"
        onScroll={(e) => { const el = e.currentTarget; stickToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 60 }}>
        {page?.hasMore && (
          <button onClick={() => void older()} className="w-full text-[11px] text-gray-500 hover:text-gray-300 py-1">
            {loadingOlder ? 'Loading…' : 'Load earlier messages'}
          </button>
        )}
        {!page?.loaded && !error && <p className="text-xs text-gray-500">Loading…</p>}
        {page?.loaded && page.messages.length === 0 && (
          <p className="text-xs text-gray-500 mt-4 text-center">No messages yet{allowed ? ' — say hello.' : '.'}</p>
        )}
        <MessageList messages={page?.messages ?? []} users={users} meId={me?.id} isManager={!!me?.isManager}
          retired={channel.retired} onThread={(m) => setView({ kind: 'thread', channelId, parentId: m.id })}
          canPinAll={canPinForEveryone(channel, me?.id, !!me?.isManager, me?.roleId)}
          canDeleteAny={canDeleteOthers(channel, me?.id, !!me?.isManager, me?.roleId)} />
        <TypingLine channelId={channelId} />
      </div>

      {error && <p className="px-3 text-[11px] text-red-400">{error}</p>}
      {allowed
        ? <Composer channelId={channelId} placeholder={`Message ${title}`} />
        : <p className="px-3 py-2 text-[11px] text-amber-300 border-t border-gray-800">
            {channel.retired ? 'This channel is retired — its history is read-only.' : 'Only selected people can post in this channel.'}
          </p>}
    </div>
  )
}

// ── Thread ───────────────────────────────────────────────────────────────────

export function ThreadView({ channelId, parentId }: { channelId: string; parentId: string }) {
  const channel = useChatStore((s) => s.channels[channelId])
  const parent = useChatStore((s) => s.messages[channelId]?.messages.find((m) => m.id === parentId))
  const replies = useChatStore((s) => s.threads[parentId])
  const users = useChatStore((s) => s.users)
  const me = useChatStore((s) => s.me)
  const setView = useChatStore((s) => s.setView)
  const [loadedParent, setLoadedParent] = useState<ChatMessage | null>(null)
  const scrollRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    chatApi.thread(parentId).then((t) => { setLoadedParent(t.parent); useChatStore.getState().setThread(parentId, t.replies) }).catch(() => {})
  }, [parentId])
  useEffect(() => { const el = scrollRef.current; if (el) el.scrollTop = el.scrollHeight }, [replies?.length])

  const p = parent ?? loadedParent
  const allowed = channel ? canPost(channel, me?.id, !!me?.isManager, me?.roleId) : false
  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="px-3 py-2 border-b border-gray-800 flex items-center gap-2 shrink-0">
        <button onClick={() => setView({ kind: 'channel', channelId })} className="text-gray-400 hover:text-white text-sm" title="Back">←</button>
        <div className="min-w-0">
          <p className="text-sm text-white font-medium">Thread</p>
          {channel && <p className="text-[11px] text-gray-500 truncate">{channelTitle(channel, users, me?.id)}</p>}
        </div>
      </div>
      <div ref={scrollRef} className="flex-1 min-h-0 overflow-y-auto px-3 py-2">
        {p && <MessageList messages={[p]} users={users} meId={me?.id} isManager={!!me?.isManager} retired={!!channel?.retired}
          canPinAll={!!channel && canPinForEveryone(channel, me?.id, !!me?.isManager, me?.roleId)}
          canDeleteAny={!!channel && canDeleteOthers(channel, me?.id, !!me?.isManager, me?.roleId)} />}
        <div className="border-t border-gray-800 my-2 text-[10px] text-gray-500 pt-1">
          {(replies?.length ?? 0)} {replies?.length === 1 ? 'reply' : 'replies'}
        </div>
        <MessageList messages={replies ?? []} users={users} meId={me?.id} isManager={!!me?.isManager} retired={!!channel?.retired}
          canPinAll={!!channel && canPinForEveryone(channel, me?.id, !!me?.isManager, me?.roleId)}
          canDeleteAny={!!channel && canDeleteOthers(channel, me?.id, !!me?.isManager, me?.roleId)} />
      </div>
      {allowed && <Composer channelId={channelId} parentId={parentId} placeholder="Reply…" />}
    </div>
  )
}

// ── Messages ─────────────────────────────────────────────────────────────────

function dayLabel(iso: string) {
  const d = new Date(iso), today = new Date()
  const y = new Date(); y.setDate(today.getDate() - 1)
  if (d.toDateString() === today.toDateString()) return 'Today'
  if (d.toDateString() === y.toDateString()) return 'Yesterday'
  return d.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' })
}
const time = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })

export function MessageList({ messages, users, meId, isManager, retired, onThread, canPinAll = false, canDeleteAny = false }: {
  messages: ChatMessage[]; users: Record<string, ChatUser>; meId?: string; isManager: boolean; retired: boolean
  onThread?: (m: ChatMessage) => void; canPinAll?: boolean; canDeleteAny?: boolean
}) {
  return (
    <>
      {messages.map((m, i) => {
        const prev = messages[i - 1]
        const newDay = !prev || new Date(prev.createdAt).toDateString() !== new Date(m.createdAt).toDateString()
        const grouped = !newDay && !!prev && prev.agentId === m.agentId && prev.kind === m.kind
          && new Date(m.createdAt).getTime() - new Date(prev.createdAt).getTime() < 5 * 60_000
        return (
          <Fragment key={m.id}>
            {newDay && <div className="text-center text-[10px] text-gray-500 my-2">{dayLabel(m.createdAt)}</div>}
            <MessageItem m={m} users={users} meId={meId} isManager={isManager} retired={retired} grouped={grouped && !m.pinnedAt && !prev?.pinnedAt}
              onThread={onThread} canPinAll={canPinAll} canDeleteAny={canDeleteAny} />
          </Fragment>
        )
      })}
    </>
  )
}

function MessageItem({ m, users, meId, isManager, retired, grouped, onThread, canPinAll, canDeleteAny }: {
  m: ChatMessage; users: Record<string, ChatUser>; meId?: string; isManager: boolean; retired: boolean; grouped: boolean
  onThread?: (m: ChatMessage) => void; canPinAll: boolean; canDeleteAny: boolean
}) {
  const [editing, setEditing] = useState(false)
  const [picker, setPicker] = useState(false)
  const [pinMenu, setPinMenu] = useState(false)
  const pinnedForMe = useChatStore((s) => !!s.pins[m.channelId]?.mine.some((x) => x.id === m.id))
  const [error, setError] = useState<string | null>(null)
  const author = m.agentId ? users[m.agentId] : null
  const mine = !!meId && m.agentId === meId

  if (m.kind === 'system') {
    return <p className="text-[11px] text-sky-300/90 italic my-1.5 px-1">{plainText(m.body, users)} <span className="text-gray-600">{time(m.createdAt)}</span></p>
  }

  async function pin(scope: 'me' | 'everyone', on: boolean) {
    setPinMenu(false); setError(null)
    try {
      await (on ? chatApi.pin(m.id, scope) : chatApi.unpin(m.id, scope))
      const pins = await chatApi.pins(m.channelId)
      useChatStore.getState().setPins(m.channelId, pins)
    } catch (e) { setError(e instanceof Error ? e.message : 'Pin failed.') }
  }

  // Pinned for everyone: amber band + label, so it stands out wherever it sits in the conversation.
  const pinnedAll = !!m.pinnedAt && !m.deleted
  return (
    <div id={`chat-msg-${m.id}`}
      className={`group relative rounded px-1 ${grouped ? 'mt-0.5' : 'mt-2'} ${pinnedAll ? 'bg-amber-500/10 border-l-2 border-amber-400 pl-2 py-1' : 'hover:bg-gray-800/40'}`}>
      {pinnedAll && (
        <p className="text-[10px] text-amber-300 font-medium mb-0.5">
          📌 Pinned for everyone{m.pinnedById ? ` by ${users[m.pinnedById]?.name ?? 'someone'}` : ''}
        </p>
      )}
      {!grouped && (
        <div className="flex items-baseline gap-2">
          <span className="text-xs font-semibold text-gray-100">{author?.name ?? 'Former user'}</span>
          <span className="text-[10px] text-gray-500">{time(m.createdAt)}</span>
          {pinnedForMe && <span className="text-[10px] text-sky-300" title="Pinned for you">🔖</span>}
        </div>
      )}
      {m.deleted ? <p className="text-xs text-gray-600 italic">Message deleted</p>
        : editing ? <EditBox m={m} users={users} onDone={() => setEditing(false)} />
        : m.format === 'html'
          ? <div className="text-xs text-gray-200 break-words leading-relaxed"><RichBody html={m.body} meId={meId} />
              {m.editedAt && <span className="text-[10px] text-gray-500">(edited)</span>}</div>
          : <p className="text-xs text-gray-200 whitespace-pre-wrap break-words leading-relaxed"><Body text={m.body} users={users} meId={meId} />
              {m.editedAt && <span className="text-[10px] text-gray-500"> (edited)</span>}</p>}

      {m.reactions.length > 0 && !m.deleted && (
        <div className="flex flex-wrap gap-1 mt-1">
          {m.reactions.map((r) => {
            const reacted = !!meId && r.agentIds.includes(meId)
            const who = r.agentIds.map((id) => users[id]?.name ?? 'Someone').join(', ')
            return (
              <button key={r.emoji} disabled={retired} title={who}
                onClick={() => chatApi.react(m.id, r.emoji).catch((e: Error) => setError(e.message))}
                className={`text-[11px] rounded-full px-1.5 py-0.5 border ${reacted ? 'border-indigo-500 bg-indigo-950/60 text-indigo-200' : 'border-gray-700 bg-gray-800/60 text-gray-300'}`}>
                {r.emoji} {r.agentIds.length}
              </button>
            )
          })}
        </div>
      )}
      {onThread && m.replyCount > 0 && (
        <button onClick={() => onThread(m)} className="text-[11px] text-indigo-300 hover:text-indigo-200 mt-0.5">
          {m.replyCount} {m.replyCount === 1 ? 'reply' : 'replies'}{m.lastReplyAt ? ` · last ${time(m.lastReplyAt)}` : ''}
        </button>
      )}
      {error && <p className="text-[10px] text-red-400">{error}</p>}

      {!m.deleted && !editing && !retired && (
        <div className="absolute -top-3 right-1 hidden group-hover:flex items-center gap-0.5 bg-gray-900 border border-gray-700 rounded px-1 shadow">
          <button onClick={() => setPicker((v) => !v)} className="text-xs px-1 hover:bg-gray-800 rounded" title="React">☺</button>
          {onThread && <button onClick={() => onThread(m)} className="text-xs px-1 hover:bg-gray-800 rounded" title="Reply in thread">↩</button>}
          <button onClick={() => setPinMenu((v) => !v)} className="text-xs px-1 hover:bg-gray-800 rounded" title="Pin">📌</button>
          {mine && <button onClick={() => setEditing(true)} className="text-xs px-1 hover:bg-gray-800 rounded" title="Edit">✎</button>}
          {(mine || isManager || canDeleteAny) && (
            <button onClick={() => chatApi.remove(m.id).catch((e: Error) => setError(e.message))}
              className="text-xs px-1 hover:bg-gray-800 rounded text-red-300" title="Delete">🗑</button>
          )}
        </div>
      )}
      {pinMenu && (
        <div className="absolute right-1 top-4 z-10 flex flex-col bg-gray-900 border border-gray-700 rounded py-0.5 shadow text-[11px]">
          <button onClick={() => void pin('me', !pinnedForMe)} className="text-left px-2 py-1 hover:bg-gray-800 text-gray-200">
            {pinnedForMe ? 'Unpin for me' : 'Pin for me'}
          </button>
          {canPinAll && (
            <button onClick={() => void pin('everyone', !m.pinnedAt)} className="text-left px-2 py-1 hover:bg-gray-800 text-amber-300">
              {m.pinnedAt ? 'Unpin for everyone' : 'Pin for everyone'}
            </button>
          )}
        </div>
      )}
      {picker && (
        <div className="absolute right-1 top-4 z-10 flex gap-0.5 bg-gray-900 border border-gray-700 rounded px-1 py-0.5 shadow">
          {QUICK_REACTIONS.map((e) => (
            <button key={e} onClick={() => { setPicker(false); chatApi.react(m.id, e).catch((x: Error) => setError(x.message)) }}
              className="text-sm px-0.5 hover:bg-gray-800 rounded">{e}</button>
          ))}
        </div>
      )}
    </div>
  )
}

/** Text with mentions as highlighted names and links clickable — React escapes everything else. */
function Body({ text, users, meId }: { text: string; users: Record<string, ChatUser>; meId?: string }) {
  const parts = text.split(/(<@[0-9a-fA-F-]{36}>|https?:\/\/[^\s<]+)/g)
  return (
    <>
      {parts.map((p, i) => {
        const mention = /^<@([0-9a-fA-F-]{36})>$/.exec(p)
        if (mention) {
          const isMe = mention[1].toLowerCase() === meId?.toLowerCase()
          return <span key={i} className={`rounded px-0.5 ${isMe ? 'bg-amber-500/25 text-amber-200' : 'bg-indigo-500/20 text-indigo-200'}`}>@{users[mention[1]]?.name ?? 'someone'}</span>
        }
        if (/^https?:\/\//.test(p)) return <a key={i} href={p} target="_blank" rel="noreferrer noopener" className="text-sky-300 underline break-all">{p}</a>
        return <Fragment key={i}>{p}</Fragment>
      })}
    </>
  )
}

/** The conversation's pins, at the top: pinned for everyone (amber) and the viewer's own. */
function PinnedBar({ channelId, onJump }: { channelId: string; onJump: (m: ChatMessage) => void }) {
  const pins = useChatStore((s) => s.pins[channelId])
  const users = useChatStore((s) => s.users)
  const [open, setOpen] = useState(false)
  useEffect(() => {
    chatApi.pins(channelId).then((p) => useChatStore.getState().setPins(channelId, p)).catch(() => {})
  }, [channelId])
  const everyone = pins?.everyone ?? []
  const mine = (pins?.mine ?? []).filter((m) => !everyone.some((e) => e.id === m.id))
  if (everyone.length + mine.length === 0) return null

  const row = (m: ChatMessage, forAll: boolean) => (
    <button key={`${forAll ? 'a' : 'm'}-${m.id}`} onClick={() => onJump(m)}
      className={`w-full text-left px-3 py-1.5 border-t border-gray-800/60 hover:bg-gray-800/60 ${forAll ? 'bg-amber-500/5' : ''}`}>
      <p className={`text-[10px] ${forAll ? 'text-amber-300' : 'text-sky-300'}`}>
        {forAll ? '📌' : '🔖'} {m.agentId ? users[m.agentId]?.name ?? 'Someone' : 'ContactConnection'} · {new Date(m.createdAt).toLocaleDateString()}
      </p>
      <p className="text-xs text-gray-200 line-clamp-2 break-words">{messagePreview(m, users)}</p>
    </button>
  )

  return (
    <div className="border-b border-gray-800 shrink-0 max-h-[40%] overflow-y-auto">
      <button onClick={() => setOpen((v) => !v)} className="w-full text-left px-3 py-1.5 text-[11px] flex items-center gap-2 hover:bg-gray-800/40">
        {everyone.length > 0 && <span className="text-amber-300">📌 {everyone.length} pinned for everyone</span>}
        {mine.length > 0 && <span className="text-sky-300">🔖 {mine.length} pinned for you</span>}
        <span className="ml-auto text-gray-500">{open ? '▾' : '▸'}</span>
      </button>
      {open && (
        <>
          {everyone.map((m) => row(m, true))}
          {mine.map((m) => row(m, false))}
        </>
      )}
      {!open && everyone[0] && (
        <button onClick={() => onJump(everyone[0])} className="w-full text-left px-3 pb-1.5 text-xs text-gray-300 truncate block">
          {messagePreview(everyone[0], users)}
        </button>
      )}
    </div>
  )
}

function TypingLine({ channelId }: { channelId: string }) {
  const typing = useChatStore((s) => s.typing[channelId])
  const users = useChatStore((s) => s.users)
  const [, tick] = useState(0)
  useEffect(() => { const t = setInterval(() => tick((n) => n + 1), 1000); return () => clearInterval(t) }, [])
  const now = Date.now()
  const names = Object.entries(typing ?? {}).filter(([, until]) => until > now).map(([id]) => users[id]?.name.split(' ')[0] ?? 'Someone')
  if (names.length === 0) return null
  return <p className="text-[10px] text-gray-500 italic mt-1">{names.join(', ')} {names.length === 1 ? 'is' : 'are'} typing…</p>
}

export function StateLine({ user }: { user: ChatUser }) {
  const st = stateStyle(user.state)
  return (
    <p className="text-[11px] text-gray-500 flex items-center gap-1.5 truncate">
      <span className={`inline-block w-2 h-2 rounded-full shrink-0 ${st.dot}`} />{st.text}
    </p>
  )
}

// ── Rich messages ────────────────────────────────────────────────────────────

const ALLOWED_TAGS = ['p', 'br', 'strong', 'b', 'em', 'i', 'u', 's', 'span', 'mark', 'ul', 'ol', 'li', 'a', 'img', 'code', 'pre', 'blockquote']
const ALLOWED_ATTR = ['style', 'href', 'data-chat-file', 'data-mention', 'data-color']

/** A formatted message: sanitized again here (the server already did), then images loaded with the viewer's sign-in,
 *  mentions highlighted (amber when it's you) and links opened in a new tab. */
function RichBody({ html, meId }: { html: string; meId?: string }) {
  const ref = useRef<HTMLDivElement>(null)
  const clean = useMemo(() => DOMPurify.sanitize(html, { ALLOWED_TAGS, ALLOWED_ATTR, ALLOW_DATA_ATTR: false }), [html])
  useEffect(() => {
    const el = ref.current
    if (!el) return
    el.querySelectorAll('a[href]').forEach((a) => { a.setAttribute('target', '_blank'); a.setAttribute('rel', 'noreferrer noopener') })
    el.querySelectorAll('span[data-mention]').forEach((m) => m.classList.toggle('me', m.getAttribute('data-mention') === meId))
    el.querySelectorAll<HTMLImageElement>('img[data-chat-file]').forEach((img) => {
      const id = img.getAttribute('data-chat-file')!
      img.alt = 'Image'
      loadChatImage(id).then((url) => {
        img.src = url
        img.onclick = () => useChatStore.setState({ lightbox: url })
      }).catch(() => { img.alt = 'Image unavailable'; img.classList.add('missing') })
    })
  }, [clean, meId])
  return <div ref={ref} className="chat-rich" dangerouslySetInnerHTML={{ __html: clean }} />
}

export function Lightbox() {
  const url = useChatStore((s) => s.lightbox)
  if (!url) return null
  return (
    <div className="fixed inset-0 z-[60] bg-black/80 flex items-center justify-center p-6 cursor-zoom-out"
      onClick={() => useChatStore.setState({ lightbox: null })}>
      <img src={url} alt="" className="max-w-full max-h-full rounded shadow-2xl" />
    </div>
  )
}

// ── Composer ─────────────────────────────────────────────────────────────────

function Composer({ channelId, parentId, placeholder }: { channelId: string; parentId?: string; placeholder: string }) {
  const users = useChatStore((s) => s.users)
  const meId = useChatStore((s) => s.me?.id)
  const ref = useRef<ChatEditorHandle>(null)
  const lastTyping = useRef(0)
  return (
    <div className="border-t border-gray-800 p-2 shrink-0">
      <ChatEditor ref={ref} key={`${channelId}:${parentId ?? ''}`} users={users} meId={meId} placeholder={placeholder}
        onTyping={() => {
          if (Date.now() - lastTyping.current > 3000) { lastTyping.current = Date.now(); chatApi.typing(channelId).catch(() => {}) }
        }}
        onSubmit={async (html) => {
          await chatApi.post(channelId, html, parentId, 'html')
          ref.current?.clear()
          ref.current?.focus()
        }} />
    </div>
  )
}

function EditBox({ m, users, onDone }: { m: ChatMessage; users: Record<string, ChatUser>; onDone: () => void }) {
  const meId = useChatStore((s) => s.me?.id)
  // Plain-text messages open as their words (mentions shown as @Name); saving makes them formatted messages.
  const initial = m.format === 'html' ? m.body
    : `<p>${escapeHtml(m.body).replace(/&lt;@([0-9a-fA-F-]{36})&gt;/g, (_, id: string) =>
        `<span data-mention="${id}">@${escapeHtml(users[id]?.name ?? 'someone')}</span>`).replace(/\n/g, '<br>')}</p>`
  return (
    <div className="mt-1">
      <ChatEditor users={users} meId={meId} initialHtml={initial} placeholder="Edit message" submitLabel="Save" onCancel={onDone}
        onSubmit={async (html) => { await chatApi.edit(m.id, html, 'html'); onDone() }} />
    </div>
  )
}

function escapeHtml(s: string) {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}
