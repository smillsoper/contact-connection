import { useEffect, useMemo, useRef, useState } from 'react'
import { chatApi, stateStyle, type BrowseChannel, type ChatChannel, type ChatMessage, type ChatUser, type HelpRequest } from '../api/chat'
import { useChatStore, channelTitle } from '../stores/chatStore'
import { useCallStore } from '../stores/callStore'
import { startChat, messagePreview, canAskForNotifications, askForNotifications } from '../lib/chatConnection'
import { Conversation, ThreadView, StateLine, Lightbox } from './chat/ChatConversation'

/**
 * Team chat (S183) — the agent portal's right panel, and the body of the chat launcher on admin pages. Slack-like:
 * channels (set up on the Team Chat admin page), direct and group messages, threads, reactions, @mentions, live status
 * for everyone, and a raise-hand path to the agent's own supervisors.
 */
/** At this width and up the conversation list stays on the left beside the open conversation. */
const TWO_PANE_MIN = 560

export default function ChatPanel({ onClose }: { onClose?: () => void }) {
  const status = useChatStore((s) => s.status)
  const disabledMessage = useChatStore((s) => s.disabledMessage)
  const view = useChatStore((s) => s.view)
  const wide = useChatStore((s) => s.wide)
  const root = useRef<HTMLDivElement>(null)

  useEffect(() => {
    void startChat()
    useChatStore.getState().setVisible(true)
    return () => useChatStore.getState().setVisible(false)
  }, [])

  useEffect(() => {
    const el = root.current
    if (!el) return
    const ro = new ResizeObserver(([e]) => {
      const w = e.contentRect.width >= TWO_PANE_MIN
      if (w !== useChatStore.getState().wide) useChatStore.setState({ wide: w })
    })
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  let body: React.ReactNode
  if (status === 'disabled') {
    body = <Shell onClose={onClose}><p className="p-4 text-xs text-gray-500 text-center">{disabledMessage}</p></Shell>
  } else if (status !== 'ready') {
    body = <Shell onClose={onClose}><p className="p-4 text-xs text-gray-500">{status === 'error' ? 'Chat is unavailable right now.' : 'Loading chat…'}</p></Shell>
  } else {
    const detail =
      view.kind === 'channel' ? <div className="flex flex-col h-full"><Conversation channelId={view.channelId} /></div>
      : view.kind === 'thread' ? <div className="flex flex-col h-full"><ThreadView channelId={view.channelId} parentId={view.parentId} /></div>
      : view.kind === 'new-dm' ? <NewDirect />
      : view.kind === 'browse' ? <Browse />
      : view.kind === 'search' ? <Search />
      : null
    body = wide ? (
      <div className="flex h-full min-h-0">
        <div className="w-60 shrink-0 border-r border-gray-800 flex flex-col min-h-0"><ChannelList onClose={onClose} /></div>
        <div className="flex-1 min-w-0 flex flex-col min-h-0">
          {detail ?? (
            <div className="flex-1 flex items-center justify-center p-6">
              <p className="text-xs text-gray-500 text-center">Pick a channel or a person on the left to start chatting.</p>
            </div>
          )}
        </div>
      </div>
    ) : (detail ?? <ChannelList onClose={onClose} />)
  }
  return <div ref={root} className="h-full min-h-0 flex flex-col">{body}<Lightbox /></div>
}

function Shell({ children, onClose, actions }: { children: React.ReactNode; onClose?: () => void; actions?: React.ReactNode }) {
  return (
    <div className="flex flex-col h-full">
      <div className="px-4 py-2.5 border-b border-gray-800 shrink-0 flex items-center gap-2">
        <p className="text-sm font-medium text-gray-300 flex-1">Team Chat</p>
        {actions}
        {onClose && <button onClick={onClose} className="text-gray-500 hover:text-white text-sm" title="Close">✕</button>}
      </div>
      <div className="flex-1 min-h-0 overflow-y-auto">{children}</div>
    </div>
  )
}

function Header({ title, onBack }: { title: string; onBack: () => void }) {
  const wide = useChatStore((s) => s.wide)
  return (
    <div className="px-3 py-2 border-b border-gray-800 flex items-center gap-2 shrink-0">
      {!wide && <button onClick={onBack} className="text-gray-400 hover:text-white text-sm" title="Back">←</button>}
      <p className="text-sm text-white font-medium">{title}</p>
    </div>
  )
}

// ── List ─────────────────────────────────────────────────────────────────────

const STATE_ORDER = (u: ChatUser) => {
  const c = u.state?.code ?? 'logged_out'
  return c === 'available' ? 0 : c === 'on_call' || c === 'callback_pending' ? 1 : c === 'acw' ? 2 : c === 'logged_out' ? 4 : 3
}

function ChannelList({ onClose }: { onClose?: () => void }) {
  const channels = useChatStore((s) => s.channels)
  const users = useChatStore((s) => s.users)
  const me = useChatStore((s) => s.me)
  const supervisorIds = useChatStore((s) => s.supervisorIds)
  const setView = useChatStore((s) => s.setView)
  const [showRetired, setShowRetired] = useState(false)
  const [showTeam, setShowTeam] = useState(true)
  const [askNotify, setAskNotify] = useState(canAskForNotifications())

  const all = Object.values(channels)
  const named = all.filter((c) => c.kind === 'channel' && !c.retired).sort((a, b) => a.name.localeCompare(b.name))
  const retired = all.filter((c) => c.kind === 'channel' && c.retired).sort((a, b) => a.name.localeCompare(b.name))
  const dms = all.filter((c) => c.kind === 'dm').sort((a, b) => (b.lastMessageAt ?? '').localeCompare(a.lastMessageAt ?? ''))
  const team = Object.values(users).filter((u) => u.id !== me?.id).sort((a, b) => STATE_ORDER(a) - STATE_ORDER(b) || a.name.localeCompare(b.name))
  const onDuty = team.filter((u) => (u.state?.code ?? 'logged_out') !== 'logged_out').length

  const actions = (
    <>
      <button onClick={() => setView({ kind: 'search' })} className="text-gray-500 hover:text-white text-xs" title="Search messages">⌕</button>
      <button onClick={() => setView({ kind: 'browse' })} className="text-gray-500 hover:text-white text-xs" title="Browse channels">#</button>
      <button onClick={() => setView({ kind: 'new-dm' })} className="text-gray-500 hover:text-white text-xs" title="New message">✎</button>
    </>
  )

  return (
    <Shell onClose={onClose} actions={actions}>
      {askNotify && (
        <button onClick={() => { void askForNotifications().then(() => setAskNotify(false)) }}
          className="w-full text-left px-4 py-1.5 text-[11px] text-sky-300 hover:bg-gray-800/50 border-b border-gray-800">
          🔔 Turn on desktop notifications for messages and help requests
        </button>
      )}
      <HelpArea />

      {supervisorIds.length > 0 && (
        <Section title="Your supervisors">
          {supervisorIds.map((id) => users[id] && <PersonRow key={id} user={users[id]} />)}
        </Section>
      )}

      <Section title="Channels" action={<button onClick={() => setView({ kind: 'browse' })} className="text-[10px] text-gray-500 hover:text-white">Browse</button>}>
        {named.length === 0 && <p className="px-4 py-1 text-[11px] text-gray-600">You're not in any channels yet.</p>}
        {named.map((c) => <ChannelRow key={c.id} c={c} />)}
        {retired.length > 0 && (
          <>
            <button onClick={() => setShowRetired((v) => !v)} className="px-4 py-1 text-[10px] text-gray-500 hover:text-gray-300">
              {showRetired ? '▾' : '▸'} Retired ({retired.length})
            </button>
            {showRetired && retired.map((c) => <ChannelRow key={c.id} c={c} />)}
          </>
        )}
      </Section>

      <Section title="Direct messages" action={<button onClick={() => setView({ kind: 'new-dm' })} className="text-[10px] text-gray-500 hover:text-white">New</button>}>
        {dms.length === 0 && <p className="px-4 py-1 text-[11px] text-gray-600">No conversations yet.</p>}
        {dms.map((c) => <ChannelRow key={c.id} c={c} />)}
      </Section>

      <Section title={`Team · ${onDuty} on duty`} action={
        <button onClick={() => setShowTeam((v) => !v)} className="text-[10px] text-gray-500 hover:text-white">{showTeam ? 'Hide' : 'Show'}</button>}>
        {showTeam && team.map((u) => <PersonRow key={u.id} user={u} />)}
      </Section>
    </Shell>
  )
}

function Section({ title, action, children }: { title: string; action?: React.ReactNode; children: React.ReactNode }) {
  return (
    <div className="py-2 border-b border-gray-800/60">
      <div className="flex items-center justify-between px-4 mb-0.5">
        <p className="text-[10px] uppercase tracking-wide text-gray-500">{title}</p>
        {action}
      </div>
      {children}
    </div>
  )
}

function ChannelRow({ c }: { c: ChatChannel }) {
  const users = useChatStore((s) => s.users)
  const me = useChatStore((s) => s.me)
  const setView = useChatStore((s) => s.setView)
  const others = c.memberIds.filter((id) => id !== me?.id)
  const single = c.kind === 'dm' && others.length === 1 ? users[others[0]] : null
  const bold = c.unread > 0 || c.mentions > 0
  const active = useChatStore((s) => s.wide && 'channelId' in s.view && s.view.channelId === c.id)
  return (
    <button onClick={() => setView({ kind: 'channel', channelId: c.id })}
      className={`w-full text-left px-4 py-1 flex items-center gap-2 ${active ? 'bg-indigo-900/40' : 'hover:bg-gray-800/60'}`}>
      {single && <span className={`inline-block w-2 h-2 rounded-full shrink-0 ${stateStyle(single.state).dot}`} />}
      <span className={`text-xs truncate flex-1 ${bold ? 'text-white font-semibold' : c.retired ? 'text-gray-600' : 'text-gray-400'}`}>
        {channelTitle(c, users, me?.id)}
      </span>
      {c.postingRestricted && !c.retired && <span className="text-[10px]" title="Only selected people can post">📣</span>}
      {c.membershipLocked && <span className="text-[10px]" title="Assigned members can't leave">📌</span>}
      {c.mentions > 0 && <span className="text-[10px] bg-red-600 text-white rounded-full px-1.5">@{c.mentions}</span>}
      {c.unread > 0 && c.mentions === 0 && <span className="text-[10px] bg-indigo-600 text-white rounded-full px-1.5">{c.unread}</span>}
    </button>
  )
}

function PersonRow({ user }: { user: ChatUser }) {
  const [busy, setBusy] = useState(false)
  async function open() {
    setBusy(true)
    try {
      const c = await chatApi.openDirect([user.id])
      useChatStore.getState().upsertChannel(c)
      useChatStore.getState().setView({ kind: 'channel', channelId: c.id })
    } finally { setBusy(false) }
  }
  const st = stateStyle(user.state)
  return (
    <button onClick={() => void open()} disabled={busy} title={`Message ${user.name}`}
      className="w-full text-left px-4 py-1 flex items-center gap-2 hover:bg-gray-800/60">
      <span className={`inline-block w-2 h-2 rounded-full shrink-0 ${st.dot}`} />
      <span className="text-xs text-gray-300 truncate flex-1">{user.name}</span>
      <span className="text-[10px] text-gray-500 truncate max-w-[45%]">{st.text}</span>
    </button>
  )
}

// ── Raise a hand ─────────────────────────────────────────────────────────────

function since(iso: string) {
  const s = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 1000))
  return s < 60 ? `${s}s` : `${Math.floor(s / 60)}m ${s % 60}s`
}

function HelpArea() {
  const myHelp = useChatStore((s) => s.myHelp)
  const queue = useChatStore((s) => s.helpQueue)
  // Only people with an assigned supervisor can raise a hand (supervisors themselves usually have none).
  const hasSupervisors = useChatStore((s) => s.supervisorIds.length > 0)
  const [composing, setComposing] = useState(false)
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [, tick] = useState(0)
  useEffect(() => { const t = setInterval(() => tick((n) => n + 1), 1000); return () => clearInterval(t) }, [])

  if (queue.length === 0 && myHelp?.status !== 'open' && !hasSupervisors) return null

  async function raise() {
    setError(null)
    try {
      const callRecordId = useCallStore.getState().callStatus === 'on-call' ? useCallStore.getState().callRecordId : null
      useChatStore.getState().setHelp(await chatApi.raiseHand(note.trim() || null, callRecordId))
      setComposing(false); setNote('')
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not reach a supervisor.') }
  }

  return (
    <div className="border-b border-gray-800">
      {queue.map((h) => <HelpCard key={h.id} h={h} />)}

      {myHelp?.status === 'open' ? (
        <div className="m-2 rounded border border-amber-700 bg-amber-950/40 px-3 py-2">
          <p className="text-xs text-amber-200 font-medium">✋ Waiting for a supervisor… <span className="text-amber-400/80 font-normal">{since(myHelp.createdAt)}</span></p>
          <p className="text-[10px] text-amber-300/80 mt-0.5">
            {myHelp.notifiedIds.length === 0 ? 'No supervisors are set up yet — ask an admin.'
              : myHelp.wentToAllSupervisors ? `None of your supervisors is on duty — sent to all ${myHelp.notifiedIds.length} on-duty supervisors.`
              : `Sent to ${myHelp.notifiedIds.length} of your supervisors.`}
          </p>
          <button onClick={() => chatApi.cancelHelp(myHelp.id).then((h) => useChatStore.getState().setHelp(h)).catch(() => {})}
            className="text-[11px] text-gray-400 hover:text-white mt-1">Cancel</button>
        </div>
      ) : !hasSupervisors ? null : composing ? (
        <div className="m-2 rounded border border-gray-700 bg-gray-900 px-2 py-2">
          <input autoFocus value={note} onChange={(e) => setNote(e.target.value)} maxLength={300}
            onKeyDown={(e) => { if (e.key === 'Enter') void raise(); if (e.key === 'Escape') setComposing(false) }}
            placeholder="What do you need? (optional)"
            className="w-full bg-gray-800 text-white text-xs rounded px-2 py-1 outline-none focus:ring-1 focus:ring-amber-500" />
          <div className="flex gap-2 mt-1.5">
            <button onClick={() => void raise()} className="text-[11px] bg-amber-600 hover:bg-amber-500 text-white rounded px-2 py-0.5">Raise hand</button>
            <button onClick={() => setComposing(false)} className="text-[11px] text-gray-500 hover:text-white">Cancel</button>
          </div>
          {error && <p className="text-[11px] text-red-400 mt-1">{error}</p>}
        </div>
      ) : (
        <button onClick={() => setComposing(true)}
          className="w-[calc(100%-1rem)] m-2 text-xs rounded border border-amber-800/70 text-amber-200 hover:bg-amber-950/40 py-1.5">
          ✋ Ask a supervisor for help
        </button>
      )}
    </div>
  )
}

function HelpCard({ h }: { h: HelpRequest }) {
  const [error, setError] = useState<string | null>(null)
  async function claim() {
    setError(null)
    try {
      const r = await chatApi.claimHelp(h.id)
      useChatStore.getState().setHelp(r)
      if (r.channelId) useChatStore.getState().setView({ kind: 'channel', channelId: r.channelId })
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not pick it up.') }
  }
  return (
    <div className="m-2 rounded border border-red-700 bg-red-950/40 px-3 py-2 animate-[pulse_2s_ease-in-out_3]">
      <p className="text-xs text-red-100 font-medium">✋ {h.agentName ?? 'An agent'} needs help <span className="text-red-300/80 font-normal">{since(h.createdAt)}</span></p>
      {h.callerNumber && <p className="text-[10px] text-red-200/80">On a call{h.campaignName ? ` · ${h.campaignName}` : ''} · {h.callerNumber}</p>}
      {h.note && <p className="text-[11px] text-gray-200 mt-0.5">“{h.note}”</p>}
      <button onClick={() => void claim()} className="text-[11px] bg-red-600 hover:bg-red-500 text-white rounded px-2 py-0.5 mt-1">Pick up</button>
      {error && <p className="text-[11px] text-red-300 mt-1">{error}</p>}
    </div>
  )
}

// ── New message / browse / search ────────────────────────────────────────────

function NewDirect() {
  const users = useChatStore((s) => s.users)
  const me = useChatStore((s) => s.me)
  const setView = useChatStore((s) => s.setView)
  const [q, setQ] = useState('')
  const [chosen, setChosen] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)
  const list = useMemo(() => Object.values(users).filter((u) => u.id !== me?.id && u.name.toLowerCase().includes(q.toLowerCase()))
    .sort((a, b) => a.name.localeCompare(b.name)), [users, q, me?.id])

  async function start() {
    setError(null)
    try {
      const c = await chatApi.openDirect(chosen)
      useChatStore.getState().upsertChannel(c)
      setView({ kind: 'channel', channelId: c.id })
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not start the conversation.') }
  }

  return (
    <div className="flex flex-col h-full">
      <Header title="New message" onBack={() => setView({ kind: 'list' })} />
      <div className="p-2 shrink-0">
        <input autoFocus value={q} onChange={(e) => setQ(e.target.value)} placeholder="Find people…"
          className="w-full bg-gray-800 text-white text-xs rounded px-2 py-1.5 outline-none focus:ring-1 focus:ring-indigo-500" />
        {chosen.length > 0 && (
          <div className="flex flex-wrap gap-1 mt-1.5">
            {chosen.map((id) => (
              <button key={id} onClick={() => setChosen((c) => c.filter((x) => x !== id))}
                className="text-[11px] bg-indigo-900/60 text-indigo-200 rounded px-1.5">{users[id]?.name} ✕</button>
            ))}
          </div>
        )}
      </div>
      <div className="flex-1 min-h-0 overflow-y-auto">
        {list.map((u) => (
          <label key={u.id} className="flex items-center gap-2 px-4 py-1 hover:bg-gray-800/60 cursor-pointer">
            <input type="checkbox" checked={chosen.includes(u.id)}
              onChange={() => setChosen((c) => (c.includes(u.id) ? c.filter((x) => x !== u.id) : [...c, u.id]))} />
            <span className="flex-1 min-w-0"><span className="text-xs text-gray-200">{u.name}</span><StateLine user={u} /></span>
          </label>
        ))}
      </div>
      <div className="p-2 border-t border-gray-800 shrink-0">
        <button onClick={() => void start()} disabled={chosen.length === 0}
          className="w-full text-xs bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded py-1.5">
          {chosen.length > 1 ? `Start group message (${chosen.length + 1} people)` : 'Start conversation'}
        </button>
        {error && <p className="text-[11px] text-red-400 mt-1">{error}</p>}
      </div>
    </div>
  )
}

function Browse() {
  const setView = useChatStore((s) => s.setView)
  const [list, setList] = useState<BrowseChannel[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => { chatApi.browse().then(setList).catch((e: Error) => setError(e.message)) }, [])
  async function join(id: string) {
    try {
      const c = await chatApi.join(id)
      useChatStore.getState().upsertChannel(c)
      setView({ kind: 'channel', channelId: c.id })
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not join.') }
  }
  return (
    <div className="flex flex-col h-full">
      <Header title="Browse channels" onBack={() => setView({ kind: 'list' })} />
      <div className="flex-1 min-h-0 overflow-y-auto">
        {!list && !error && <p className="p-4 text-xs text-gray-500">Loading…</p>}
        {list?.length === 0 && <p className="p-4 text-xs text-gray-500">You're in every open channel. Private channels are by invitation from a chat manager.</p>}
        {list?.map((c) => (
          <div key={c.id} className="px-4 py-2 border-b border-gray-800/60 flex items-start gap-2">
            <div className="flex-1 min-w-0">
              <p className="text-xs text-gray-200"># {c.name}{c.retired && <span className="text-gray-500"> · retired</span>}</p>
              {c.description && <p className="text-[11px] text-gray-500">{c.description}</p>}
              <p className="text-[10px] text-gray-600">{c.memberCount} members</p>
            </div>
            <button onClick={() => void join(c.id)} className="text-[11px] bg-indigo-600 hover:bg-indigo-500 text-white rounded px-2 py-0.5">Join</button>
          </div>
        ))}
        {error && <p className="p-4 text-[11px] text-red-400">{error}</p>}
      </div>
    </div>
  )
}

function Search() {
  const users = useChatStore((s) => s.users)
  const channels = useChatStore((s) => s.channels)
  const me = useChatStore((s) => s.me)
  const setView = useChatStore((s) => s.setView)
  const [q, setQ] = useState('')
  const [hits, setHits] = useState<ChatMessage[] | null>(null)
  useEffect(() => {
    if (q.trim().length < 2) { setHits(null); return }
    const t = setTimeout(() => { chatApi.search(q.trim()).then(setHits).catch(() => setHits([])) }, 300)
    return () => clearTimeout(t)
  }, [q])
  return (
    <div className="flex flex-col h-full">
      <Header title="Search messages" onBack={() => setView({ kind: 'list' })} />
      <div className="p-2 shrink-0">
        <input autoFocus value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search your conversations…"
          className="w-full bg-gray-800 text-white text-xs rounded px-2 py-1.5 outline-none focus:ring-1 focus:ring-indigo-500" />
      </div>
      <div className="flex-1 min-h-0 overflow-y-auto">
        {hits?.length === 0 && <p className="p-4 text-xs text-gray-500">No messages match.</p>}
        {hits?.map((m) => {
          const c = channels[m.channelId]
          return (
            <button key={m.id} className="w-full text-left px-4 py-2 border-b border-gray-800/60 hover:bg-gray-800/50"
              onClick={() => setView(m.parentId ? { kind: 'thread', channelId: m.channelId, parentId: m.parentId } : { kind: 'channel', channelId: m.channelId })}>
              <p className="text-[10px] text-gray-500">{c ? channelTitle(c, users, me?.id) : ''} · {new Date(m.createdAt).toLocaleString()}</p>
              <p className="text-xs text-gray-300"><span className="text-gray-100 font-medium">{m.agentId ? users[m.agentId]?.name : ''}</span> {messagePreview(m, users)}</p>
            </button>
          )
        })}
      </div>
    </div>
  )
}
