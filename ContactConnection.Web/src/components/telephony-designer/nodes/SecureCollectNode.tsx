import { Handle, Position, type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

const SLOTS = ['collected', 'failed', 'timeout'] as const
const SLOT_COLOR: Record<(typeof SLOTS)[number], string> = {
  collected: '#059669',
  failed: '#b91c1c',
  timeout: '#b45309',
}

export default function SecureCollectNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const fields = (data.secureFields as { key: string }[] | undefined) ?? []

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
      type="tf_secure_collect"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
      extraHandles={extraHandles}
    >
      <p className="text-[11px] text-rose-300 mt-0.5 truncate">PCI · masks recording · guided DTMF</p>
      <p className="text-[10px] text-gray-500 mt-0.5 truncate">
        {fields.length === 0 ? 'no fields configured' : fields.map((f) => f.key).join(' · ')}
      </p>
    </TelNodeShell>
  )
}
