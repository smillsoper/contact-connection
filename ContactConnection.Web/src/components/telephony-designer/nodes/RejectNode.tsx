import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

const CAUSE_LABELS: Record<string, string> = {
  busy: 'SIP 486 Busy Here',
  unavailable: 'SIP 480 Unavailable',
  declined: 'SIP 603 Decline',
}

export default function RejectNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const cause = (data.cause as string) || 'busy'
  return (
    <TelNodeShell type="tf_reject" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-red-400 mt-1">{CAUSE_LABELS[cause] ?? cause}</p>
    </TelNodeShell>
  )
}
