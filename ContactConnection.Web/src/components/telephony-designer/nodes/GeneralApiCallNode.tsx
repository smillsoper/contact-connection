import type { NodeProps } from '@xyflow/react'
import { useEdges, useNodeId } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

const EXIT_OPTIONS = ['success', 'error', 'timeout']

export default function GeneralApiCallNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const nodeId = useNodeId()
  const edges  = useEdges()

  const apiDefinitionName = (data.apiDefinitionName as string) ?? ''
  const apiEndpointName   = (data.apiEndpointName as string) ?? ''
  const outputVariable    = (data.outputVariable as string) ?? ''

  const wiredOptions = new Set(
    edges
      .filter((e) => e.source === nodeId)
      .map((e) => (e.data as Record<string, unknown>)?.transition as string | undefined ?? e.sourceHandle)
      .filter(Boolean),
  )
  const missingOptions = EXIT_OPTIONS.filter((o) => !wiredOptions.has(o))

  return (
    <TelNodeShell type="tf_general_api_call" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5 truncate">
        {apiEndpointName ? `${apiDefinitionName} → ${apiEndpointName}` : '— no endpoint selected'}
      </p>

      {outputVariable && (
        <p className="text-[10px] text-emerald-400 mt-0.5 font-mono truncate">
          → {'{{flow.' + outputVariable + '}}'}
        </p>
      )}

      {missingOptions.length > 0 && (
        <p className="text-[10px] text-amber-400 mt-0.5 font-medium">
          ⚠ {missingOptions.length} option{missingOptions.length > 1 ? 's' : ''} not wired
        </p>
      )}
    </TelNodeShell>
  )
}
