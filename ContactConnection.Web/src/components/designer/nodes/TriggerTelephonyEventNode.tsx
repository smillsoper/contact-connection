import type { NodeProps } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'

export default function TriggerTelephonyEventNode({ data, selected }: NodeProps & { data: NodeData }) {
  const eventName = (data.eventName as string) ?? ''
  return (
    <NodeShell type="trigger_telephony_event" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      {eventName ? (
        <p className="text-xs text-gray-400 mt-0.5 truncate">custom:{eventName}</p>
      ) : (
        <p className="text-xs text-gray-500 mt-0.5 italic">No event name</p>
      )}
    </NodeShell>
  )
}
