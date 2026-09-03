import { Handle, Position, type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

const SLOTS = ['scheduled', 'invalid_time', 'failed'] as const
const SLOT_COLOR: Record<(typeof SLOTS)[number], string> = {
  scheduled: '#0891b2',
  invalid_time: '#b45309',
  failed: '#b91c1c',
}

export default function ScheduledCallbackNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const source = (data.numberSource as string) ?? 'ani'
  const date = (data.scheduledDateValue as string) || ''
  const time = (data.scheduledTimeValue as string) || ''

  const extraHandles = (
    <>
      {SLOTS.map((id, i) => (
        <Handle
          key={id}
          type="source"
          position={Position.Bottom}
          id={id}
          style={{ left: `${((i + 1) / (SLOTS.length + 1)) * 100}%`, background: SLOT_COLOR[id] }}
        />
      ))}
    </>
  )

  return (
    <TelNodeShell
      type="tf_scheduled_callback"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
      extraHandles={extraHandles}
    >
      <p className="text-[11px] text-cyan-300 mt-0.5 truncate">
        {date || time ? `when: ${date} ${time}`.trim() : '⚠ no date/time set'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5 truncate">
        {source === 'collected' ? `number: {{${(data.collectedVar as string) || '…'}}}` : "number: caller's ANI"}
        {' · '}
        {(data.targetFlowId as string) ? 'target flow set' : '⚠ no target flow'}
      </p>
    </TelNodeShell>
  )
}
