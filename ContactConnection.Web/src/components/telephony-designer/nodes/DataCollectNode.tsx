import { Handle, Position, type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

const SLOTS = ['collected', 'timeout'] as const
const SLOT_COLOR: Record<(typeof SLOTS)[number], string> = {
  collected: '#059669',
  timeout: '#b45309',
}

export default function DataCollectNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const variableName = (data.variableName as string) ?? ''
  const allowVoice = !!(data.allowVoice as boolean)

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
      type="tf_data_collect"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
      extraHandles={extraHandles}
    >
      <p className="text-[11px] text-teal-300 mt-0.5 font-mono truncate">
        {variableName ? `→ {{flow.${variableName}}}` : 'no variable set'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5 truncate">
        {allowVoice ? 'DTMF + voice' : 'DTMF only'}
      </p>
    </TelNodeShell>
  )
}
