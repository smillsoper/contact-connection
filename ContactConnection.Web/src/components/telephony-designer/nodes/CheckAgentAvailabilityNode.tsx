import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function CheckAgentAvailabilityNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  return (
    <TelNodeShell type="tf_check_agent_availability" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-1">available / unavailable</p>
    </TelNodeShell>
  )
}
