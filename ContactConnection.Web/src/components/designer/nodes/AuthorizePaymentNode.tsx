import type { NodeProps } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'

export default function AuthorizePaymentNode({ data, selected }: NodeProps & { data: NodeData }) {
  const provider = (data.provider as string) || 'authorize_net'
  const amountMode = (data.amountMode as string) ?? 'cart_total'
  const fixedAmount = data.fixedAmount as number | undefined

  return (
    <NodeShell type="authorize_payment" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5 truncate">{provider}</p>
      <p className="text-[10px] text-gray-500 mt-0.5">
        {amountMode === 'fixed' ? `fixed amount: $${(fixedAmount ?? 0).toFixed(2)}` : 'amount: current cart total'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">approved / declined / error</p>
    </NodeShell>
  )
}
