import { Handle, Position, type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

const SLOTS = ['requested', 'failed'] as const

export default function RequestCallbackNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const source = (data.numberSource as string) ?? 'ani'
  const window = (data.windowMinutes as number) ?? 120
  const attempts = (data.maxAttempts as number) ?? 3

  const extraHandles = (
    <>
      {SLOTS.map((id, i) => (
        <Handle
          key={id}
          type="source"
          position={Position.Bottom}
          id={id}
          style={{
            left: `${((i + 1) / (SLOTS.length + 1)) * 100}%`,
            background: id === 'failed' ? '#b91c1c' : '#0891b2',
          }}
        />
      ))}
    </>
  )

  return (
    <TelNodeShell
      type="tf_request_callback"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
      extraHandles={extraHandles}
    >
      <p className="text-[11px] text-cyan-300 mt-0.5 truncate">
        {source === 'collected'
          ? `number: {{${(data.collectedVar as string) || '…'}}}`
          : "number: caller's ANI"}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">
        {window}m window · up to {attempts} attempt{attempts === 1 ? '' : 's'}
      </p>
    </TelNodeShell>
  )
}
