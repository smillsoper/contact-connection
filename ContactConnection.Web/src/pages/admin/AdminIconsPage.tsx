import AdminShell from '../../components/admin/AdminShell'
import * as I from '../../components/icons/Icons'

/**
 * The platform icon set at a glance (S183) — for signing off the drawn icons against Stephen's library. Each shows at 16, 20
 * and 32 px with what it means and where it's used.
 */

type Entry = { name: keyof typeof I; meaning: string; where: string }

const LIBRARY: Entry[] = [
  { name: 'SearchIcon', meaning: 'Search messages', where: 'Team chat header' },
  { name: 'BrowseChannelsIcon', meaning: 'Browse channels', where: 'Team chat header' },
  { name: 'NewMessageIcon', meaning: 'New message', where: 'Team chat header' },
  { name: 'AnnouncementIcon', meaning: 'Announcement-only channel', where: 'Team chat channel list · Team Chat config page (Posting column)' },
  { name: 'PinIcon', meaning: 'Pin / pinned · can’t leave', where: 'Chat hover bar, pinned messages and bar · channel list · Team Chat config page' },
  { name: 'ReactIcon', meaning: 'React', where: 'Chat message hover bar' },
  { name: 'AddEmojiIcon', meaning: 'Add emoji (full library)', where: 'Chat message hover bar' },
  { name: 'ReplyThreadIcon', meaning: 'Reply in thread', where: 'Chat message hover bar' },
  { name: 'EditIcon', meaning: 'Edit', where: 'Chat hover bar · campaign proficiency (Campaign → Agents) · Audio picker “Edit & regenerate TTS”' },
  { name: 'DeleteIcon', meaning: 'Delete / remove a row', where: 'Chat hover bar · Assignments grid · chart metrics · records columns · export columns · offer intervals · time-of-day windows · telephony list rows · API success rules · onboarding admin emails' },
  { name: 'FormattingIcon', meaning: 'Formatting toolbar', where: 'Chat composer' },
  { name: 'AddImageIcon', meaning: 'Add image', where: 'Chat composer' },
]

const DRAWN_APPROVED: Entry[] = [
  { name: 'PaperclipIcon', meaning: 'Attach a file', where: 'Chat composer' },
  { name: 'FileIcon', meaning: 'A file / attached script', where: 'Chat attachment cards and chips · Flow designer nodes “Script attached” (input, email, phone, address)' },
]

const DRAWN_NEW: Entry[] = [
  { name: 'CloseIcon', meaning: 'Close / dismiss / cancel', where: 'Caller history & call detail popups · flow panel notices · flows import notices · Active Calls panel · supervisor error · Assignments grid cancel · campaign proficiency cancel · chat picker chips · export delivery “Not sent” · branch node “false”' },
  { name: 'CheckIcon', meaning: 'Done / valid / set', where: 'Assignments grid save · onboarding steps · credential “exists” marks · gateway “configured” marks · KPI formula “Valid” · entry-node badges · branch node “true” · copied-to-clipboard · audio picker save buttons · export “Sent”' },
  { name: 'WarningIcon', meaning: 'Needs attention', where: 'Designer node warnings (“option not wired”, “no prompt set”…) · credential “not found” · cart “tax not calculated” · address city mismatch (agent) · API definition test shortcuts' },
  { name: 'LockIcon', meaning: 'Locked', where: 'Softphone “Unavailable — locked” · Users page / agent list lock badges · call detail lock note · commit node · script commit banner (agent)' },
  { name: 'PhoneIcon', meaning: 'Call / internal call', where: 'Agent list “call this agent” · softphone intercom banner' },
  { name: 'HeadsetIcon', meaning: 'Listen in / monitor', where: 'Agent list supervise menu · Active Calls & agent list monitoring banners · softphone monitor banner · IVR “always listening”' },
  { name: 'PlayIcon', meaning: 'Play / run', where: 'Recording player · audio picker preview · API definition “Run & Capture”' },
  { name: 'StopIcon', meaning: 'Stop', where: 'Audio picker recording' },
  { name: 'CopyIcon', meaning: 'Copy to clipboard', where: 'JSON tree (captured responses) · API definition variable tags' },
  { name: 'ChevronRightIcon', meaning: 'Expand (collapsed)', where: 'JSON tree · KPI variable groups · media change log · call detail sections · agent groups' },
  { name: 'ChevronDownIcon', meaning: 'Collapse (expanded) · move down · sort descending', where: 'Same expanders · reorder buttons (chart metrics, records columns, export columns, announcements) · supervise menu · API preferences · agent list sort' },
  { name: 'ChevronUpIcon', meaning: 'Move up · sort ascending', where: 'Reorder buttons · API preferences · agent list sort' },
  { name: 'UploadIcon', meaning: 'Upload', where: 'Audio picker' },
  { name: 'SendIcon', meaning: 'Send a test', where: 'API definition “Source” / “Payload” capture' },
  { name: 'ExternalLinkIcon', meaning: 'Open elsewhere', where: 'Agent list “Open” call' },
  { name: 'SparkleIcon', meaning: 'Generated / platform audio', where: 'Audio picker (platform phrase, generate TTS)' },
  { name: 'MicIcon', meaning: 'Voice input', where: 'IVR menu node “voice”' },
  { name: 'MailIcon', meaning: 'Email', where: 'Voicemail node “emails the message”' },
  { name: 'TimerIcon', meaning: 'Waits / timed', where: 'Script node “auto-advances”' },
  { name: 'CartIcon', meaning: 'Cart', where: 'Agent portal cart bar' },
  { name: 'BoltIcon', meaning: 'Test now', where: 'API definition “Test Authentication”' },
  { name: 'RefreshIcon', meaning: 'Retrying · re-wire', where: 'Export delivery status · designer option picker “re-wire”' },
  { name: 'WrenchIcon', meaning: 'Remote fixes', where: 'Agent List widget · remote fixes window' },
  { name: 'EyeIcon', meaning: 'Follow the script', where: 'Agent List widget · follow-along window' },
  { name: 'CoachIcon', meaning: 'Coaching note', where: 'Agent List widget · coach window · agent portal pinned notes' },
  { name: 'ScreenIcon', meaning: 'Live screen view', where: 'Agent List widget · screen view window' },
  { name: 'PointerIcon', meaning: 'Point here', where: 'Screen view window · agent portal marker' },
  { name: 'BulletListIcon', meaning: 'Bulleted list', where: 'Script editor toolbar (Flow Designer) · chat composer toolbar' },
  { name: 'NumberedListIcon', meaning: 'Numbered list', where: 'Script editor toolbar · chat composer toolbar' },
  { name: 'ClearFormattingIcon', meaning: 'Clear formatting', where: 'Script editor toolbar · chat composer toolbar' },
  { name: 'EmojiMissingIcon', meaning: 'An emoji this device can’t display', where: 'Chat messages, reactions, pinned previews and search — stands in for emoji newer than the device’s font (tooltip names it)' },
]

function Row({ e }: { e: Entry }) {
  const Icon = I[e.name] as (p: I.IconProps) => React.ReactElement
  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-4 flex gap-4 items-start">
      <div className="flex items-end gap-3 text-gray-200 shrink-0 w-28">
        <Icon size={32} /><Icon size={20} /><Icon size={16} />
      </div>
      <div className="min-w-0">
        <p className="text-sm text-white font-medium">{e.meaning}</p>
        <p className="text-[11px] text-gray-500 font-mono">{e.name}</p>
        <p className="text-xs text-gray-400 mt-1 leading-snug">{e.where}</p>
      </div>
    </div>
  )
}

function Section({ title, note, list }: { title: string; note: string; list: Entry[] }) {
  return (
    <section className="mb-8">
      <h2 className="text-white text-base font-semibold">{title}</h2>
      <p className="text-gray-500 text-sm mb-3">{note}</p>
      <div className="grid md:grid-cols-2 gap-3">{list.map((e) => <Row key={e.name} e={e} />)}</div>
    </section>
  )
}

export default function AdminIconsPage() {
  return (
    <AdminShell>
      <div className="p-6 max-w-6xl">
        <h1 className="text-white text-xl font-semibold">Icons</h1>
        <p className="text-gray-500 text-sm mt-0.5 mb-6">
          One set across the platform — 24×24 grid, 2px round strokes, coloured by the surrounding text.
        </p>
        <Section title="Library" note="From the team-chat-icons library." list={LIBRARY} />
        <Section title="Drawn to match the library" note="Approved. Replace any with a library version in components/icons/Icons.tsx and every place it's used follows."
          list={[...DRAWN_APPROVED, ...DRAWN_NEW]} />
      </div>
    </AdminShell>
  )
}
