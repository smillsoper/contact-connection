import type { NodeProps } from '@xyflow/react'
import type { TelNodeData } from '../../../types/telephony-designer'
import TelNodeShell from '../TelNodeShell'

export default function DtmfNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const digits = (data.digits as string) ?? ''
  const durationMs = (data.durationMs as number) ?? 100
  const wait = (data.waitForCompletion as boolean) ?? true
  return (
    <TelNodeShell type="tf_dtmf" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-[11px] text-yellow-300 mt-0.5 truncate font-mono">
        {digits ? `${digits} @ ${durationMs}ms` : '⚠ no digits set'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">
        {wait ? 'waits for completion' : 'fire and forget'}
      </p>
    </TelNodeShell>
  )
}
