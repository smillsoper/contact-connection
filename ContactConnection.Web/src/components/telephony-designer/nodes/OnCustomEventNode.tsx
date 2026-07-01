import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function OnCustomEventNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const eventName = (data.eventName as string) || '(unnamed)'
  return (
    <TelNodeShell type="tf_on_custom_event" label={data.label as string} selected={selected}>
      <p className="text-[11px] text-amber-300 mt-0.5 font-mono truncate">custom:{eventName}</p>
    </TelNodeShell>
  )
}
