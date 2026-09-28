import type { NodeProps } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'

export default function SendEmailNode({ data, selected }: NodeProps & { data: NodeData }) {
  const to = (data.emailTo as string) ?? ''
  return (
    <NodeShell type="send_email" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5 truncate">{to ? `→ ${to}` : '— no recipient'}</p>
    </NodeShell>
  )
}
