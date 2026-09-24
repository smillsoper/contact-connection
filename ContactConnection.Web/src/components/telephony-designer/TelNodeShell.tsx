import { Handle, Position } from '@xyflow/react'
import type { TelephonyNodeType } from '../../types/telephony-designer'
import { TELEPHONY_NODE_META } from '../../types/telephony-designer'

interface TelNodeShellProps {
  type: TelephonyNodeType
  label: string
  isEntry?: boolean
  selected?: boolean
  children?: React.ReactNode
}

export default function TelNodeShell({ type, label, isEntry, selected, children }: TelNodeShellProps) {
  const meta = TELEPHONY_NODE_META[type]
  const hasSingle    = meta.handles === 'single'
  const isEventNode  = meta.handles === 'source-only'

  return (
    <div
      style={{
        width: 210,
        position: 'relative',
        borderColor: selected ? meta.color : isEventNode ? '#4b5563' : '#374151',
        borderWidth: selected ? 2 : 1,
        borderStyle: isEventNode ? 'dashed' : 'solid',
      }}
      className="bg-gray-800 rounded-lg border shadow-sm"
    >
      {/* Target handle (top) — event listener nodes are source-only */}
      {!isEventNode && (
        <Handle type="target" position={Position.Top} style={{ background: '#6b7280' }} />
      )}

      {/* Colored header */}
      <div
        style={{ backgroundColor: meta.color }}
        className="flex items-center justify-between px-3 py-1.5 rounded-t-[7px]"
      >
        <span className="text-white text-xs font-semibold uppercase tracking-wide">
          {meta.label}
        </span>
        {isEventNode && (
          <span className="bg-white/30 text-white text-[10px] font-bold px-1.5 py-0.5 rounded">
            EVENT
          </span>
        )}
        {!isEventNode && isEntry && (
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

      {/* Single source handle — regular nodes and event listener nodes. Every telephony node type
          now funnels its exit(s) through this one physical handle: a plain pass-through connect
          for single-transition nodes, or the option-picker modal (see FIXED_EXIT_OPTIONS /
          computePickerOptions in TelephonyDesignerPage.tsx) for nodes with named branches. */}
      {(hasSingle || isEventNode) && (
        <Handle type="source" position={Position.Bottom} id="default" style={{ background: '#9ca3af' }} />
      )}
    </div>
  )
}
