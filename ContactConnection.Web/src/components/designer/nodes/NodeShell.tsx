import { Handle, Position } from '@xyflow/react'
import type { ContactConnectionNodeType } from '../../../types/designer'
import { NODE_META } from '../../../types/designer'

interface NodeShellProps {
  type: ContactConnectionNodeType
  label: string
  isEntry?: boolean
  selected?: boolean
  children?: React.ReactNode
  sourceHandles?: React.ReactNode
}

export default function NodeShell({ type, label, isEntry, selected, children, sourceHandles }: NodeShellProps) {
  const meta = NODE_META[type]
  const hasSingle = meta.handles === 'single'

  return (
    <div
      style={{
        width: 210,
        position: 'relative',
        borderColor: selected ? meta.color : '#374151',
        borderWidth: selected ? 2 : 1,
      }}
      className="bg-gray-800 rounded-lg border shadow-sm"
    >
      {/* Target handle */}
      <Handle type="target" position={Position.Top} style={{ background: '#6b7280' }} />

      {/* Colored header — rounded-t so bg color respects outer border-radius without overflow-hidden */}
      <div
        style={{ backgroundColor: meta.color }}
        className="flex items-center justify-between px-3 py-1.5 rounded-t-[7px]"
      >
        <span className="text-white text-xs font-semibold uppercase tracking-wide">
          {meta.label}
        </span>
        {isEntry && (
          <span className="bg-white/30 text-white text-[10px] font-bold px-1.5 py-0.5 rounded">
            ENTRY
          </span>
        )}
      </div>

      {/* Body */}
      <div className="px-3 py-2">
        <p className="text-sm font-medium text-gray-100 truncate">{label}</p>
        {children}
      </div>

      {/* Custom source handles (e.g. per-option for input/select nodes) */}
      {sourceHandles}

      {/* Single source handle */}
      {!sourceHandles && hasSingle && (
        <Handle type="source" position={Position.Bottom} id="default" style={{ background: '#9ca3af' }} />
      )}
    </div>
  )
}
