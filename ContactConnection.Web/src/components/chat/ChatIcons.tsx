/**
 * Team chat icon set (S183 — Stephen's team-chat-icons library): 24×24 grid, 2px round strokes, currentColor, so each
 * icon takes the colour of its text. Paths copied as-is from the library's SVGs.
 */

type IconProps = { size?: number; className?: string; title?: string }

function Icon({ size = 16, className = '', title, children }: IconProps & { children: React.ReactNode }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="none" stroke="currentColor" strokeWidth={2}
      strokeLinecap="round" strokeLinejoin="round" className={`shrink-0 ${className}`} aria-hidden={title ? undefined : true}
      role={title ? 'img' : undefined}>
      {title && <title>{title}</title>}
      {children}
    </svg>
  )
}

export const SearchIcon = (p: IconProps) => <Icon {...p}><circle cx="11" cy="11" r="7" /><path d="M20 20l-4-4" /></Icon>
export const BrowseChannelsIcon = (p: IconProps) => <Icon {...p}><path d="M4 9h16M4 15h16M10 3L8 21M16 3l-2 18" /></Icon>
export const NewMessageIcon = (p: IconProps) => <Icon {...p}><path d="M12 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-6" /><path d="M17 3l4 4-9 9-5 1 1-5z" /></Icon>
export const AnnouncementIcon = (p: IconProps) => <Icon {...p}><path d="M3 11v2a1 1 0 0 0 1 1h2l11 5V5L6 10H4a1 1 0 0 0-1 1z" /><path d="M7 14.5l1 4.5a1 1 0 0 0 1 1h1a1 1 0 0 0 1-1.2l-.7-3" /><path d="M20.5 9.5v5" /></Icon>
export const PinIcon = (p: IconProps) => <Icon {...p}><path d="M9 3h6l-1 6 4 4v2H6v-2l4-4z" /><path d="M12 15v6" /></Icon>
export const ReactIcon = (p: IconProps) => <Icon {...p}><circle cx="12" cy="12" r="9" /><path d="M8.5 14.5a4.5 4.5 0 0 0 7 0" /><path d="M9 9.5h.01M15 9.5h.01" /></Icon>
export const AddEmojiIcon = (p: IconProps) => <Icon {...p}><path d="M19 13a8 8 0 1 1-8-8" /><path d="M7.5 15.5a4 4 0 0 0 7 0" /><path d="M8.5 11h.01M13.5 11h.01" /><path d="M19 2v6M16 5h6" /></Icon>
export const ReplyThreadIcon = (p: IconProps) => <Icon {...p}><path d="M9 14L4 9l5-5" /><path d="M4 9h10.5a5.5 5.5 0 0 1 0 11H11" /></Icon>
export const EditIcon = (p: IconProps) => <Icon {...p}><path d="M17 3l4 4L8 20l-5 1 1-5z" /><path d="M14 6l4 4" /></Icon>
export const DeleteIcon = (p: IconProps) => <Icon {...p}><path d="M4 7h16" /><path d="M10 7V4h4v3" /><path d="M6 7l1 13a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1l1-13" /><path d="M10 11v6M14 11v6" /></Icon>
export const FormattingIcon = (p: IconProps) => <Icon {...p}><path d="M3 19L8 5l5 14M5 14.5h6" /><circle cx="17.5" cy="15.5" r="3.5" /><path d="M21 12v7" /></Icon>
export const AddImageIcon = (p: IconProps) => <Icon {...p}><rect x="3" y="4" width="18" height="16" rx="2" /><circle cx="8.5" cy="9.5" r="1.5" /><path d="M21 15l-5-5L5 20" /></Icon>

/** Hover accents from the library's preview. */
export const ACCENT = {
  react: 'hover:text-[#e3b341]',
  reply: 'hover:text-[#58a6ff]',
  pin: 'hover:text-[#f78166]',
  edit: 'hover:text-[#e3b341]',
  delete: 'hover:text-[#ff7b72]',
} as const
