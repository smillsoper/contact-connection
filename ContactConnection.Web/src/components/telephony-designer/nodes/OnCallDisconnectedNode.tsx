import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function OnCallDisconnectedNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  return (
    <TelNodeShell type="tf_on_call_disconnected" label={data.label as string} selected={selected}>
      <p className="text-[11px] text-red-300 mt-0.5">post-call actions</p>
    </TelNodeShell>
  )
}
