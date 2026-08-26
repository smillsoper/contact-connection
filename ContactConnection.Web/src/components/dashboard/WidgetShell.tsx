import type { ReactNode } from 'react'

interface Props {
  title: string
  onConfigure?: () => void
  /** Omit for a view-only render (e.g. a viewer without reports.manage) — hides the button
   * instead of leaving a remove control a read-only user can't actually use. */
  onRemove?: () => void
  children: ReactNode
}

// Drag handle for react-grid-layout is the whole header bar (className matched by
// draggableHandle=".widget-drag-handle" on the grid) — clicks in the body or on the
// configure/remove buttons must not start a drag.
export default function WidgetShell({ title, onConfigure, onRemove, children }: Props) {
  return (
    <div className="h-full w-full flex flex-col bg-gray-900 border border-gray-800 rounded-xl overflow-hidden">
      <div className="widget-drag-handle flex items-center justify-between px-3 py-2 border-b border-gray-800 bg-gray-800/50 cursor-move shrink-0">
        <span className="text-xs font-semibold text-gray-300 truncate">{title}</span>
        <div className="flex items-center gap-1 shrink-0">
          {onConfigure && (
            <button
              onClick={onConfigure}
              title="Configure"
              className="text-gray-500 hover:text-gray-200 p-1 rounded transition-colors"
            >
              <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
              </svg>
            </button>
          )}
          {onRemove && (
            <button
              onClick={onRemove}
              title="Remove"
              className="text-gray-500 hover:text-red-400 p-1 rounded transition-colors"
            >
              <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          )}
        </div>
      </div>
      <div className="flex-1 overflow-auto p-3">{children}</div>
    </div>
  )
}
