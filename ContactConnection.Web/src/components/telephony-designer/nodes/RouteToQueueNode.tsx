import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function RouteToQueueNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const ext = data.agentExtension as string | undefined
  return (
    <TelNodeShell type="tf_route_to_queue" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      {ext ? (
        <p className="text-xs text-blue-300 mt-1">→ ext {ext}</p>
      ) : (
        <p className="text-xs text-gray-400 mt-1">Campaign queue</p>
      )}
    </TelNodeShell>
  )
}
