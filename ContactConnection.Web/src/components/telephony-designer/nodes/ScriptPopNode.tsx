import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function ScriptPopNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const flowId = data.flowId as string | undefined
  return (
    <TelNodeShell type="tf_script_pop" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-[11px] text-cyan-400 mt-0.5">
        {flowId ? 'custom flow override' : 'campaign default flow'}
      </p>
    </TelNodeShell>
  )
}
