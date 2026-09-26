import type { NodeProps } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'

export default function VoidPaymentNode({ data, selected }: NodeProps & { data: NodeData }) {
  return (
    <NodeShell type="void_payment" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5">Voids the call’s most recent approved transaction</p>
      <p className="text-[10px] text-gray-500 mt-0.5">voided / failed</p>
    </NodeShell>
  )
}
