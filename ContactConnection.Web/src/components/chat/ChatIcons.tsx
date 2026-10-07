/** Chat re-exports the platform icon set (components/icons/Icons.tsx) plus chat's hover accents. */
export * from '../icons/Icons'

/** Hover accents from the library's preview. */
export const ACCENT = {
  react: 'hover:text-[#e3b341]',
  reply: 'hover:text-[#58a6ff]',
  pin: 'hover:text-[#f78166]',
  edit: 'hover:text-[#e3b341]',
  delete: 'hover:text-[#ff7b72]',
} as const
