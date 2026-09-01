import type { NodeProps } from '@xyflow/react'
import type { TelNodeData } from '../../../types/telephony-designer'
import TelNodeShell from '../TelNodeShell'

const ACTION_LABEL: Record<string, string> = {
  start: 'START recording',
  stop: 'STOP recording',
  mask: 'MASK (silence)',
  unmask: 'UNMASK',
}

export default function RecordNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const action = (data.action as string) ?? 'start'
  const label = ACTION_LABEL[action] ?? action
  const maskFill = (data.maskFill as string) ?? 'silence'
  const limit = (data.recordLimitSeconds as number) ?? 0

  return (
    <TelNodeShell type="tf_record" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-[11px] text-rose-300 mt-0.5 font-mono">
        {action === 'mask' ? `MASK · ${maskFill}` : label}
      </p>
      {action === 'start' && limit > 0 && (
        <p className="text-[10px] text-gray-500 mt-0.5">limit {limit}s</p>
      )}
      {action === 'start' && (
        <p className="text-[10px] text-gray-500 mt-0.5">stereo / consent from campaign policy</p>
      )}
    </TelNodeShell>
  )
}
