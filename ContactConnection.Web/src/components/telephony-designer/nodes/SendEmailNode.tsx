import type { NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function SendEmailNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const to = (data.emailTo as string) ?? ''
  return (
    <TelNodeShell type="tf_send_email" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5 truncate">{to ? `→ ${to}` : '— no recipient'}</p>
    </TelNodeShell>
  )
}
