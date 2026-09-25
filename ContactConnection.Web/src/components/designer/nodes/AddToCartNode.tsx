import type { NodeProps } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'

export default function AddToCartNode({ data, selected }: NodeProps & { data: NodeData }) {
  const offerDisplayName = (data.offerDisplayName as string) ?? ''
  const quantity = (data.quantity as number) ?? 1
  const mode = (data.mode as string) ?? 'add'
  const replacesCount = (data.replacesOfferNames as string[] | undefined)?.length ?? 0

  return (
    <NodeShell type="add_to_cart" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5 truncate">
        {offerDisplayName ? `${quantity}× ${offerDisplayName}` : '— no offer selected'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">
        {mode === 'replace'
          ? `replaces ${replacesCount} item${replacesCount === 1 ? '' : 's'}`
          : 'adds to cart'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">added / failed</p>
    </NodeShell>
  )
}
