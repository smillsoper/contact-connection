import type { NodeProps } from '@xyflow/react'
import { useEdges, useNodeId } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'

const EXIT_OPTIONS = ['scheduled', 'invalid_time', 'failed']

export default function ScheduledCallbackNode({ data, selected }: NodeProps & { data: NodeData }) {
  const nodeId = useNodeId()
  const edges = useEdges()

  const date = (data.scheduledDateValue as string) || ''
  const time = (data.scheduledTimeValue as string) || ''
  const hasTargetFlow = !!(data.targetFlowId as string)

  const wiredOptions = new Set(
    edges
      .filter((e) => e.source === nodeId)
      .map((e) => ((e.data as Record<string, unknown>)?.transition as string | undefined) ?? e.sourceHandle)
      .filter(Boolean),
  )
  const missing = EXIT_OPTIONS.filter((o) => !wiredOptions.has(o))

  return (
    <NodeShell type="scheduled_callback" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-cyan-300 mt-0.5 truncate">
        {date || time ? `when: ${date} ${time}`.trim() : '⚠ no date/time set'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5 truncate">
        {hasTargetFlow ? 'target flow set' : '⚠ no target flow'}
      </p>
      {missing.length > 0 && (
        <p className="text-[10px] text-amber-400 mt-0.5 font-medium">
          ⚠ {missing.length} option{missing.length > 1 ? 's' : ''} not wired
        </p>
      )}
    </NodeShell>
  )
}
