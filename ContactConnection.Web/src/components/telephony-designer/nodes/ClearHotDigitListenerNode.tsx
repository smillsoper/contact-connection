import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function ClearHotDigitListenerNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  return (
    <TelNodeShell type="tf_clear_hot_digit" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-[10px] text-gray-500 mt-0.5">Disarms any active DTMF listener</p>
    </TelNodeShell>
  )
}
