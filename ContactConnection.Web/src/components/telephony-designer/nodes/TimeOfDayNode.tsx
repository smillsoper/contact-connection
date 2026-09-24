import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData, TimeWindow } from '../../../types/telephony-designer'

export default function TimeOfDayNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const windows = (data.windows as TimeWindow[] | undefined) ?? []

  return (
    <TelNodeShell
      type="tf_time_of_day"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
    >
      <p className="text-xs text-gray-400 mt-1">{data.timezone as string || 'UTC'} • {windows.length} window(s)</p>
    </TelNodeShell>
  )
}
