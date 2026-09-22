import type { NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function GetCustomFieldNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const displayLabel = (data.definitionDisplayLabel as string) ?? ''
  const variableName = (data.variableName as string) ?? ''

  return (
    <TelNodeShell type="tf_get_custom_field" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5 truncate">
        {displayLabel ? `← ${displayLabel}` : '— no field selected'}
      </p>
      {variableName && (
        <p className="text-[10px] text-emerald-400 mt-0.5 font-mono truncate">
          → {'{{flow.' + variableName + '}}'}
        </p>
      )}
    </TelNodeShell>
  )
}
