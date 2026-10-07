import type { NodeProps } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'
import { CheckIcon, CloseIcon } from '../../icons/Icons'

export default function BranchNode({ data, selected }: NodeProps & { data: NodeData }) {
  return (
    <NodeShell type="branch" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      {data.condition ? (
        <p className="text-xs text-gray-400 mt-0.5 truncate font-mono">{data.condition as string}</p>
      ) : (
        <p className="text-xs text-gray-500 mt-0.5 italic">No condition</p>
      )}
      <div className="flex justify-between mt-1.5 text-[10px]">
        <span className="text-green-400 font-medium inline-flex items-center gap-0.5"><CheckIcon size={11} />true</span>
        <span className="text-red-400 font-medium inline-flex items-center gap-0.5"><CloseIcon size={11} />false</span>
      </div>
    </NodeShell>
  )
}
