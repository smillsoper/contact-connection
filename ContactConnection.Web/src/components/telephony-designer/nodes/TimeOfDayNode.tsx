import { Handle, Position, type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData, TimeWindow } from '../../../types/telephony-designer'

export default function TimeOfDayNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const windows = (data.windows as TimeWindow[] | undefined) ?? []

  const extraHandles = (
    <>
      {windows.map((w, i) => (
        <Handle
          key={w.name}
          type="source"
          position={Position.Bottom}
          id={w.name}
          style={{
            left: `${((i + 1) / (windows.length + 1)) * 100}%`,
            background: '#92400e',
          }}
        />
      ))}
      <Handle
        type="source"
        position={Position.Bottom}
        id="no_match"
        style={{ left: `${(windows.length + 1) / (windows.length + 2) * 100}%`, background: '#6b7280' }}
      />
    </>
  )

  return (
    <TelNodeShell
      type="tf_time_of_day"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
      extraHandles={windows.length > 0 ? extraHandles : undefined}
    >
      <p className="text-xs text-gray-400 mt-1">{data.timezone as string || 'UTC'} • {windows.length} window(s)</p>
    </TelNodeShell>
  )
}
