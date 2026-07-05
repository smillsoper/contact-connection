import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function EventWaitNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const eventName = (data.eventName as string) ?? 'agent_answer'
  return (
    <TelNodeShell type="tf_event_wait" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-[11px] text-purple-300 mt-0.5">event: {eventName}</p>
    </TelNodeShell>
  )
}
