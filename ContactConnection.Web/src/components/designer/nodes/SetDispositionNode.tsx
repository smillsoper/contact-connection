import type { NodeProps } from '@xyflow/react'
import { useEdges, useNodeId } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'

// Set Disposition (S181): records this interaction's disposition from the catalog.
const EXIT_OPTIONS = ['success', 'error']

export default function SetDispositionNode({ data, selected }: NodeProps & { data: NodeData }) {
  const nodeId = useNodeId()
  const edges = useEdges()
  const name = (data.dispositionName as string) || ''
  const fromVariable = !data.dispositionId && (data.value as string)

  const wired = new Set(
    edges.filter((e) => e.source === nodeId)
      .map((e) => (e.data as Record<string, unknown>)?.transition as string | undefined ?? e.sourceHandle)
      .filter(Boolean),
  )
  const missing = EXIT_OPTIONS.filter((o) => !wired.has(o))

  return (
    <NodeShell type="set_disposition" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5 truncate">
        {name ? `→ ${name}` : fromVariable ? `→ ${fromVariable}` : '— no disposition chosen'}
      </p>
      {missing.length > 0 && (
        <p className="text-[10px] text-amber-400 mt-0.5 font-medium">⚠ {missing.length} option{missing.length > 1 ? 's' : ''} not wired</p>
      )}
    </NodeShell>
  )
}
