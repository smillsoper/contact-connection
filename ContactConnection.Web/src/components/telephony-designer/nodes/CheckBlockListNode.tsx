import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function CheckBlockListNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const checkVar = (data.checkVariable as string | undefined) || ''
  return (
    <TelNodeShell type="tf_check_block_list" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-[10px] text-gray-400 mt-0.5 truncate font-mono">
        {checkVar ? `checking: {{${checkVar}}}` : 'checking: ANI'}
      </p>
      <p className="text-[10px] text-gray-500">blocked / not_blocked</p>
    </TelNodeShell>
  )
}
