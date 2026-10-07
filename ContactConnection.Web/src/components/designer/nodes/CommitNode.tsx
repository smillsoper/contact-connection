import type { NodeProps } from '@xyflow/react'
import { useEdges, useNodeId } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'
import { findPathsBackBeforeCommit } from '../../../utils/commitCheck'
import { LockIcon, WarningIcon } from '../../icons/Icons'

export default function CommitNode({ data, selected }: NodeProps & { data: NodeData }) {
  const nodeId = useNodeId()
  const edges = useEdges()
  const eventName = (data.eventName as string) || 'committed'
  const allowed = (data.allowedSectionIds as string[] | undefined) ?? []
  const backPaths = nodeId ? findPathsBackBeforeCommit(nodeId, edges) : []

  return (
    <NodeShell type="commit" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-300 mt-0.5 truncate"><LockIcon size={11} className="inline -mt-0.5 mr-1" />{eventName}</p>
      <p className="text-[10px] text-gray-500 mt-0.5">
        {allowed.length === 0 ? 'no jumping back after this point' : `${allowed.length} section${allowed.length > 1 ? 's' : ''} still jumpable`}
      </p>
      {backPaths.length > 0 && (
        <p className="text-[10px] text-amber-400 mt-0.5 font-medium">
          <WarningIcon size={10} className="inline -mt-0.5 mr-0.5" />{backPaths.length} path{backPaths.length > 1 ? 's' : ''} lead back before this point
        </p>
      )}
    </NodeShell>
  )
}
