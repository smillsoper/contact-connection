import { Handle, Position, type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function RouteToQueueNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const ext = data.agentExtension as string | undefined

  // Two handles, both optional-to-wire: "default" (unchanged — chain into e.g. hold music while
  // the call waits) and "on_timeout" (new — where the flow resumes if the call exceeds the
  // campaign's Queue Timeout or the queue is already at Max Queue Size; unwired, the call just
  // hangs up gracefully instead, same as leaving "default" unwired always has).
  const extraHandles = (
    <>
      <Handle type="source" position={Position.Bottom} id="default" style={{ left: '30%', background: '#9ca3af' }} />
      <Handle type="source" position={Position.Bottom} id="on_timeout" style={{ left: '70%', background: '#b91c1c' }} />
    </>
  )

  return (
    <TelNodeShell
      type="tf_route_to_queue"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
      extraHandles={extraHandles}
    >
      {ext ? (
        <p className="text-xs text-blue-300 mt-1">→ ext {ext}</p>
      ) : (
        <p className="text-xs text-gray-400 mt-1">Campaign queue</p>
      )}
    </TelNodeShell>
  )
}
