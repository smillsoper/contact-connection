import { Handle, Position, type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

const SLOTS = ['repeat', 'finished'] as const
const SLOT_COLOR: Record<(typeof SLOTS)[number], string> = {
  repeat: '#a16207',
  finished: '#059669',
}

export default function RepeatNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const count = (data.repeatCount as number | undefined) ?? 1

  const extraHandles = (
    <>
      {SLOTS.map((id, i) => (
        <Handle
          key={id}
          type="source"
          position={Position.Bottom}
          id={id}
          style={{ left: `${((i + 1) / (SLOTS.length + 1)) * 100}%`, background: SLOT_COLOR[id] }}
        />
      ))}
    </>
  )

  return (
    <TelNodeShell
      type="tf_repeat"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
      extraHandles={extraHandles}
    >
      <p className="text-[11px] text-yellow-300 mt-0.5 truncate">
        repeat body {count}x, then finished
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5 truncate">
        wire the loop body's tail back to this node's own entry
      </p>
    </TelNodeShell>
  )
}
