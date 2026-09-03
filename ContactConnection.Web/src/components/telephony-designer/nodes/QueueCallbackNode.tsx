import { Handle, Position, type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

const SLOTS = ['queued', 'failed'] as const
const SLOT_COLOR: Record<(typeof SLOTS)[number], string> = {
  queued: '#0e7490',
  failed: '#b91c1c',
}

export default function QueueCallbackNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const source = (data.numberSource as string) ?? 'ani'

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
      type="tf_queue_callback"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
      extraHandles={extraHandles}
    >
      <p className="text-[11px] text-cyan-300 mt-0.5 truncate">virtual hold · keeps queue position</p>
      <p className="text-[10px] text-gray-500 mt-0.5 truncate">
        {source === 'collected' ? `number: {{${(data.collectedVar as string) || '…'}}}` : "number: caller's ANI"}
        {' · '}
        {`${(data.maxAttempts as number) ?? 3} tries`}
      </p>
    </TelNodeShell>
  )
}
