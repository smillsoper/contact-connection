import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function OnAgentAnswerNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  return (
    <TelNodeShell type="tf_on_agent_answer" label={data.label as string} selected={selected}>
      <p className="text-[11px] text-blue-300 mt-0.5">fires when call is bridged</p>
    </TelNodeShell>
  )
}
