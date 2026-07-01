import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function OnAgentSelectedNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  return (
    <TelNodeShell type="tf_on_agent_selected" label={data.label as string} selected={selected}>
      <p className="text-[11px] text-violet-300 mt-0.5">fires before agent answers</p>
    </TelNodeShell>
  )
}
