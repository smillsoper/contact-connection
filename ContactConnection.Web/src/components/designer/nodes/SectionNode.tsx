import { Handle, Position } from '@xyflow/react'
import type { NodeProps } from '@xyflow/react'
import type { NodeData } from '../../../types/designer'

export default function SectionNode({ data, selected }: NodeProps & { data: NodeData }) {
  const name = (data.name as string | undefined)?.trim()
  const outputVar = (data.outputVariable as string | undefined)?.trim()
  const allowJump = data.allowJumpFromAnywhere as boolean | undefined
  const clearPrev = data.clearPreviousValues as boolean | undefined
  const label = (data.label as string) || 'Section'

  return (
    <div
      style={{
        width: 210,
        borderColor: selected ? '#ffffff' : '#374151',
        borderWidth: selected ? 2 : 1,
      }}
      className="bg-gray-800 rounded-lg border shadow-sm"
    >
      <Handle type="target" position={Position.Top} style={{ background: '#9ca3af' }} />

      {/* White header with dark text — distinct from colored node headers */}
      <div
        style={{ backgroundColor: '#ffffff' }}
        className="flex items-center justify-between px-3 py-1.5 rounded-t-[7px]"
      >
        <span className="text-gray-500 text-xs font-semibold uppercase tracking-wide">Section</span>
        {data.isEntry && (
          <span className="bg-gray-200 text-gray-600 text-[10px] font-bold px-1.5 py-0.5 rounded">
            ENTRY
          </span>
        )}
      </div>

      {/* Body — same dark bg-gray-800 as all other nodes */}
      <div className="px-3 py-2">
        <p className="text-sm font-medium text-gray-100 truncate">{name || label}</p>
        {outputVar && (
          <p className="text-[11px] text-gray-400 truncate mt-0.5">var: {outputVar}</p>
        )}
        <div className="flex gap-1 mt-1 flex-wrap">
          {allowJump && (
            <span className="text-[10px] bg-blue-900/50 text-blue-300 rounded px-1.5 py-0.5">
              jump anywhere
            </span>
          )}
          {clearPrev && (
            <span className="text-[10px] bg-amber-900/50 text-amber-300 rounded px-1.5 py-0.5">
              clears values
            </span>
          )}
        </div>
      </div>

      <Handle type="source" position={Position.Bottom} id="default" style={{ background: '#9ca3af' }} />
    </div>
  )
}
