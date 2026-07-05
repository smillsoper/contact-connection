import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function SetCallerIdNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const value = (data.callerIdValue as string) ?? ''
  return (
    <TelNodeShell type="tf_set_caller_id" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-[10px] text-sky-300 truncate font-mono mt-0.5">
        {value ? `CID = ${value}` : 'No value set'}
      </p>
    </TelNodeShell>
  )
}
