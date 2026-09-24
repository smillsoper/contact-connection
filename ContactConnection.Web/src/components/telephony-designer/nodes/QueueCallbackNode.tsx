import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function QueueCallbackNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const source = (data.numberSource as string) ?? 'ani'

  return (
    <TelNodeShell
      type="tf_queue_callback"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
    >
      <p className="text-[11px] text-cyan-300 mt-0.5 truncate">virtual hold · keeps queue position</p>
      <p className="text-[10px] text-gray-500 mt-0.5 truncate">
        {source === 'collected'
          ? `number: {{${((data.collectedVar as string) || '…').replace(/^\{\{|\}\}$/g, '').replace(/^flow\./i, '')}}}`
          : "number: caller's ANI"}
        {' · '}
        {`${(data.maxAttempts as number) ?? 3} tries`}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">queued / failed</p>
    </TelNodeShell>
  )
}
