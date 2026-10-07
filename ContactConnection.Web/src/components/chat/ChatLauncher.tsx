import { useEffect, useState } from 'react'
import ChatPanel from '../ChatPanel'
import { startChat } from '../../lib/chatConnection'
import { useChatStore, useChatUnread } from '../../stores/chatStore'

/**
 * Team chat on admin and dashboard pages (S183): a corner button with the unread count that opens the same chat panel the
 * agent portal has. The connection starts with the page, so supervisors working from a dashboard still get messages and
 * help requests (the button pulses red while someone is waiting for help).
 */
export default function ChatLauncher() {
  const status = useChatStore((s) => s.status)
  const unread = useChatUnread()
  const helpWaiting = useChatStore((s) => s.helpQueue.length)
  const [open, setOpen] = useState(false)

  useEffect(() => { void startChat() }, [])
  // A help request arriving while closed opens nothing by itself — the red pulse + chime + notification do the asking.

  if (status === 'disabled' || status === 'error') return null

  return (
    <>
      {open && (
        <div className="fixed bottom-20 right-4 z-40 w-[760px] max-w-[calc(100vw-2rem)] h-[75vh] bg-gray-950 border border-gray-700 rounded-xl shadow-2xl overflow-hidden flex flex-col">
          <ChatPanel onClose={() => setOpen(false)} />
        </div>
      )}
      <button onClick={() => setOpen((v) => !v)} title="Team chat"
        className={`fixed bottom-4 right-4 z-40 w-12 h-12 rounded-full shadow-lg flex items-center justify-center text-white text-lg transition-colors
          ${helpWaiting > 0 ? 'bg-red-600 hover:bg-red-500 animate-pulse' : 'bg-indigo-600 hover:bg-indigo-500'}`}>
        {open ? '✕' : '💬'}
        {!open && (unread > 0 || helpWaiting > 0) && (
          <span className="absolute -top-1 -right-1 min-w-5 h-5 px-1 rounded-full bg-white text-[11px] font-semibold text-gray-900 flex items-center justify-center">
            {helpWaiting > 0 ? '✋' : unread > 99 ? '99+' : unread}
          </span>
        )}
      </button>
    </>
  )
}
