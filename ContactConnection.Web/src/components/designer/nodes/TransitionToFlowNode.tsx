import type { NodeProps } from '@xyflow/react'
import type { NodeData } from '../../../types/designer'
import NodeShell from './NodeShell'

export default function TransitionToFlowNode({ data, selected }: NodeProps & { data: NodeData }) {
  const flowName = (data.targetFlowName as string | undefined)?.trim()

  return (
    <NodeShell type="transition_to_flow" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      {flowName
        ? <p className="text-xs text-purple-300 truncate mt-0.5">→ {flowName}</p>
        : <p className="text-xs text-gray-500 italic mt-0.5">no flow selected</p>
      }
    </NodeShell>
  )
}
