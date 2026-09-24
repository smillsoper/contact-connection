import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function ScheduledCallbackNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const source = (data.numberSource as string) ?? 'ani'
  const date = (data.scheduledDateValue as string) || ''
  const time = (data.scheduledTimeValue as string) || ''

  return (
    <TelNodeShell
      type="tf_scheduled_callback"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
    >
      <p className="text-[11px] text-cyan-300 mt-0.5 truncate">
        {date || time ? `when: ${date} ${time}`.trim() : '⚠ no date/time set'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5 truncate">
        {source === 'collected'
          ? `number: {{${((data.collectedVar as string) || '…').replace(/^\{\{|\}\}$/g, '').replace(/^flow\./i, '')}}}`
          : "number: caller's ANI"}
        {' · '}
        {(data.targetFlowId as string) ? 'target flow set' : '⚠ no target flow'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">scheduled / invalid_time / failed</p>
    </TelNodeShell>
  )
}
