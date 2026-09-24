import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function DataCollectNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const variableName = (data.variableName as string) ?? ''
  const allowVoice = !!(data.allowVoice as boolean)

  return (
    <TelNodeShell
      type="tf_data_collect"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
    >
      <p className="text-[11px] text-teal-300 mt-0.5 font-mono truncate">
        {variableName ? `→ {{flow.${variableName}}}` : 'no variable set'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5 truncate">
        {allowVoice ? 'DTMF + voice' : 'DTMF only'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">collected / timeout</p>
    </TelNodeShell>
  )
}
