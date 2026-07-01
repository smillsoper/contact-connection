import { Handle, Position } from '@xyflow/react'
import type { TelephonyNodeType } from '../../types/telephony-designer'
import { TELEPHONY_NODE_META } from '../../types/telephony-designer'

interface TelNodeShellProps {
  type: TelephonyNodeType
  label: string
  isEntry?: boolean
  selected?: boolean
  children?: React.ReactNode
  extraHandles?: React.ReactNode
}

export default function TelNodeShell({ type, label, isEntry, selected, children, extraHandles }: TelNodeShellProps) {
  const meta = TELEPHONY_NODE_META[type]
  const hasSingle    = meta.handles === 'single'
  const hasDual      = meta.handles === 'dual'
  const isEventNode  = meta.handles === 'source-only'

  const isDualCheck      = type === 'tf_check_block_list' || type === 'tf_check_agent_availability'
  const isGenericBranch  = type === 'tf_branch'

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

      {/* Extra handles (e.g. time-of-day multiple transitions) */}
      {extraHandles}

      {/* Single source handle — regular nodes and event listener nodes */}
      {!extraHandles && (hasSingle || isEventNode) && (
        <Handle type="source" position={Position.Bottom} id="default" style={{ background: '#9ca3af' }} />
      )}

      {/* Dual handles for branch / check nodes */}
      {!extraHandles && hasDual && (
        <>
          <Handle
            type="source"
            position={Position.Bottom}
            id={isDualCheck ? 'blocked' : isGenericBranch ? 'true' : 'available'}
            style={{ left: '30%', background: isDualCheck ? '#ef4444' : '#22c55e' }}
          />
          <Handle
            type="source"
            position={Position.Bottom}
            id={isDualCheck ? 'not_blocked' : isGenericBranch ? 'false' : 'unavailable'}
            style={{ left: '70%', background: isDualCheck ? '#22c55e' : '#ef4444' }}
          />
        </>
      )}
    </div>
  )
}
