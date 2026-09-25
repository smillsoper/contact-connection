import type { NodeProps } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'

export default function ResetCartNode({ data, selected }: NodeProps & { data: NodeData }) {
  return (
    <NodeShell type="reset_cart" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5">Clears the entire cart</p>
    </NodeShell>
  )
}
