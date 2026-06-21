import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function TelBranchNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const condition = (data.condition as string | undefined) || ''
  return (
    <TelNodeShell type="tf_branch" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      {condition && <p className="text-xs text-amber-300 mt-1 truncate font-mono">{condition}</p>}
      <p className="text-xs text-gray-400">true / false</p>
    </TelNodeShell>
  )
}
