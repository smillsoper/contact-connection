import type { NodeProps } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'

export default function GetCustomFieldNode({ data, selected }: NodeProps & { data: NodeData }) {
  const displayLabel   = (data.definitionDisplayLabel as string) ?? ''
  const outputVariable = (data.outputVariable as string) ?? ''

  return (
    <NodeShell type="get_custom_field" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5 truncate">
        {displayLabel ? `← ${displayLabel}` : '— no field selected'}
      </p>
      {outputVariable && (
        <p className="text-[10px] text-emerald-400 mt-0.5 font-mono truncate">
          → {'{{flow.' + outputVariable + '}}'}
        </p>
      )}
    </NodeShell>
  )
}
