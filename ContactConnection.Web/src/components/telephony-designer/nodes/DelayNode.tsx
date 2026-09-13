import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function DelayNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const duration = (data.delayDurationMs as string) ?? ''
  return (
    <TelNodeShell type="tf_delay" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-[11px] text-lime-300 mt-0.5 font-mono truncate">
        {duration ? `wait ${duration}` : 'no duration set'}
      </p>
    </TelNodeShell>
  )
}
