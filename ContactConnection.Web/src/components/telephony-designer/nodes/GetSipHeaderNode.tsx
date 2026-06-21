import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function GetSipHeaderNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const headerName = (data.headerName as string | undefined) || '…'
  const variableName = (data.variableName as string | undefined) || '…'
  return (
    <TelNodeShell type="tf_get_sip_header" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-[10px] text-teal-300 truncate font-mono mt-0.5">{headerName} → {variableName}</p>
    </TelNodeShell>
  )
}
