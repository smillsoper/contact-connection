import type { NodeProps } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'

export default function RemoveCartItemNode({ data, selected }: NodeProps & { data: NodeData }) {
  const names = (data.removeOfferNames as string[] | undefined) ?? []

  return (
    <NodeShell type="remove_cart_item" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5 truncate">
        {names.length === 0 ? '— no offers selected' : names.join(', ')}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">removed / failed</p>
    </NodeShell>
  )
}
