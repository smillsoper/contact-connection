import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function SetSipHeaderNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const headerName = (data.sipHeaderName as string | undefined) || '…'
  const value = (data.sipHeaderValue as string | undefined) || '…'
  return (
    <TelNodeShell type="tf_set_sip_header" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-[10px] text-cyan-300 truncate font-mono mt-0.5">X-{headerName}: {value}</p>
    </TelNodeShell>
  )
}
