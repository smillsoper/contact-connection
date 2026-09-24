import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function RepeatNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const count = (data.repeatCount as number | undefined) ?? 1

  return (
    <TelNodeShell
      type="tf_repeat"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
    >
      <p className="text-[11px] text-yellow-300 mt-0.5 truncate">
        repeat body {count}x, then finished
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5 truncate">
        wire the loop body's tail back to this node's own entry
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">repeat / finished</p>
    </TelNodeShell>
  )
}
