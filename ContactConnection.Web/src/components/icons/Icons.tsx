/**
 * The platform's icon set (S183). 24×24 grid, 2px round strokes, currentColor — each icon takes the colour of its text.
 *
 *  • LIBRARY — Stephen's team-chat-icons library; paths copied as supplied.
 *  • DRAWN   — drawn here to match it (same grid, stroke and caps), approved by Stephen. Listed with where they're used
 *              on the Icons page (/admin/icons); any can be swapped for a library version later without touching callers.
 */

export type IconProps = { size?: number; className?: string; title?: string }

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

// ── LIBRARY ──────────────────────────────────────────────────────────────────
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

// ── DRAWN (approved S183) ────────────────────────────────────────────────────
export const PaperclipIcon = (p: IconProps) => <Icon {...p}><path d="M20 11.5l-8.2 8.2a5 5 0 0 1-7.1-7.1l8.5-8.5a3.3 3.3 0 0 1 4.7 4.7l-8.5 8.5a1.7 1.7 0 0 1-2.4-2.4l7.8-7.8" /></Icon>
export const FileIcon = (p: IconProps) => <Icon {...p}><path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z" /><path d="M14 3v5h5" /><path d="M9 13h6M9 17h4" /></Icon>

// ── DRAWN (S183 platform pass — approved by Stephen) ─────────────────────────
export const CloseIcon = (p: IconProps) => <Icon {...p}><path d="M18 6L6 18M6 6l12 12" /></Icon>
export const CheckIcon = (p: IconProps) => <Icon {...p}><path d="M20 6L9 17l-5-5" /></Icon>
export const WarningIcon = (p: IconProps) => <Icon {...p}><path d="M10.3 3.9L2.4 18a2 2 0 0 0 1.7 3h15.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" /><path d="M12 9v4M12 17h.01" /></Icon>
export const LockIcon = (p: IconProps) => <Icon {...p}><rect x="5" y="11" width="14" height="10" rx="2" /><path d="M8 11V7a4 4 0 0 1 8 0v4" /><path d="M12 15v2" /></Icon>
export const PhoneIcon = (p: IconProps) => <Icon {...p}><path d="M5 3h3l2 5-2.5 1.5a11 11 0 0 0 7 7L16 14l5 2v3a2 2 0 0 1-2 2A17 17 0 0 1 3 5a2 2 0 0 1 2-2z" /></Icon>
export const HeadsetIcon = (p: IconProps) => <Icon {...p}><path d="M4 14v-2a8 8 0 0 1 16 0v2" /><rect x="3" y="14" width="4" height="6" rx="1.5" /><rect x="17" y="14" width="4" height="6" rx="1.5" /><path d="M20 20a3 3 0 0 1-3 3h-3" /></Icon>
export const PlayIcon = (p: IconProps) => <Icon {...p}><path d="M7 4l13 8-13 8z" /></Icon>
export const StopIcon = (p: IconProps) => <Icon {...p}><rect x="6" y="6" width="12" height="12" rx="1.5" /></Icon>
export const CopyIcon = (p: IconProps) => <Icon {...p}><rect x="9" y="9" width="12" height="12" rx="2" /><path d="M5 15H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v1" /></Icon>
export const ChevronDownIcon = (p: IconProps) => <Icon {...p}><path d="M6 9l6 6 6-6" /></Icon>
export const ChevronRightIcon = (p: IconProps) => <Icon {...p}><path d="M9 6l6 6-6 6" /></Icon>
export const ChevronUpIcon = (p: IconProps) => <Icon {...p}><path d="M6 15l6-6 6 6" /></Icon>
export const UploadIcon = (p: IconProps) => <Icon {...p}><path d="M12 15V4" /><path d="M7 9l5-5 5 5" /><path d="M4 15v4a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-4" /></Icon>
export const SendIcon = (p: IconProps) => <Icon {...p}><path d="M21 3L10 14" /><path d="M21 3l-7 18-4-7-7-4z" /></Icon>
export const ExternalLinkIcon = (p: IconProps) => <Icon {...p}><path d="M14 4h6v6" /><path d="M20 4l-9 9" /><path d="M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5" /></Icon>
export const SparkleIcon = (p: IconProps) => <Icon {...p}><path d="M11 3l1.8 5.2L18 10l-5.2 1.8L11 17l-1.8-5.2L4 10l5.2-1.8z" /><path d="M19 15v6M16 18h6" /></Icon>
export const MicIcon = (p: IconProps) => <Icon {...p}><rect x="9" y="3" width="6" height="11" rx="3" /><path d="M5 11a7 7 0 0 0 14 0" /><path d="M12 18v3" /></Icon>
export const MailIcon = (p: IconProps) => <Icon {...p}><rect x="3" y="5" width="18" height="14" rx="2" /><path d="M3 7l9 6 9-6" /></Icon>
export const TimerIcon = (p: IconProps) => <Icon {...p}><circle cx="12" cy="13" r="8" /><path d="M12 9v4l2.5 2.5" /><path d="M9 2h6" /></Icon>
export const CartIcon = (p: IconProps) => <Icon {...p}><circle cx="9" cy="20" r="1.5" /><circle cx="18" cy="20" r="1.5" /><path d="M2 3h3l2.6 12.4a1 1 0 0 0 1 .8h9.8a1 1 0 0 0 1-.8L21 7H6" /></Icon>
export const BoltIcon = (p: IconProps) => <Icon {...p}><path d="M13 2L4 14h7l-1 8 9-12h-7z" /></Icon>
export const BulletListIcon = (p: IconProps) => <Icon {...p}><path d="M9 6h11M9 12h11M9 18h11" /><path d="M4.5 6h.01M4.5 12h.01M4.5 18h.01" /></Icon>
export const NumberedListIcon = (p: IconProps) => <Icon {...p}><path d="M10 6h10M10 12h10M10 18h10" /><path d="M4 4.5L5.5 3.5V9" /><path d="M3.5 14a1.5 1.5 0 0 1 3 .4c0 1.4-3 2.4-3 3.6h3" /></Icon>
export const ClearFormattingIcon = (p: IconProps) => <Icon {...p}><path d="M7 20l-3.3-3.3a1 1 0 0 1 0-1.4L14.3 4.7a1 1 0 0 1 1.4 0l4.6 4.6a1 1 0 0 1 0 1.4L11 20z" /><path d="M9 11l5 5" /><path d="M11 20h10" /></Icon>
/** Default emoticon — stands in for an emoji this device's font can't draw (dashed face: "an emoji was here"). */
export const EmojiMissingIcon = (p: IconProps) => <Icon {...p}><circle cx="12" cy="12" r="9" strokeDasharray="3 2.6" /><path d="M9 9.5h.01M15 9.5h.01" /><path d="M9 15h6" /></Icon>
/** Same drawing as markup, for HTML rendered outside React (formatted chat messages). */
export const EMOJI_MISSING_SVG = '<svg viewBox="0 0 24 24" width="1.15em" height="1.15em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9" stroke-dasharray="3 2.6"/><path d="M9 9.5h.01M15 9.5h.01"/><path d="M9 15h6"/></svg>'
export const RefreshIcon = (p: IconProps) => <Icon {...p}><path d="M20 11a8 8 0 0 0-14.5-4.6L4 8" /><path d="M4 3v5h5" /><path d="M4 13a8 8 0 0 0 14.5 4.6L20 16" /><path d="M20 21v-5h-5" /></Icon>
/** Live screen view (S183). */
export const ScreenIcon = (p: IconProps) => <Icon {...p}><rect x="3" y="4" width="18" height="12" rx="2" /><path d="M8 20h8" /><path d="M12 16v4" /></Icon>
/** In-call coaching note (S183): a speech bubble with an exclamation. */
export const CoachIcon = (p: IconProps) => <Icon {...p}><path d="M21 12a8 8 0 0 1-11.8 7l-5.2 1.5 1.5-4.8A8 8 0 1 1 21 12z" /><path d="M12 8v4" /><path d="M12 15.5h.01" /></Icon>
/** Remote fixes (S184) — Stephen's supplied wrench.svg (path as supplied). */
export const WrenchIcon = (p: IconProps) => <Icon {...p}><path d="M14.5 6.5a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.6-3.6a6 6 0 0 1-7.9 7.9l-6.8 6.8a2.1 2.1 0 0 1-3-3l6.8-6.8a6 6 0 0 1 7.9-7.9z" /></Icon>
/** Follow-along script view (S184). */
export const EyeIcon = (p: IconProps) => <Icon {...p}><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></Icon>
/** Point here (S183). */
export const PointerIcon = (p: IconProps) => <Icon {...p}><path d="M5 3l14 7-6 2-2 6z" /><path d="M13 12l6 6" /></Icon>
