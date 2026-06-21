import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData, TelVariableAssignment } from '../../../types/telephony-designer'

export default function TelSetVariableNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const assignments = (data.assignments as TelVariableAssignment[] | undefined) ?? []
  return (
    <TelNodeShell type="tf_set_variable" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      {assignments.slice(0, 3).map((a, i) => (
        <p key={i} className="text-[10px] text-violet-300 truncate font-mono mt-0.5">
          {a.key || '…'} = {a.value || '…'}
        </p>
      ))}
      {assignments.length > 3 && (
        <p className="text-[10px] text-gray-500">+{assignments.length - 3} more</p>
      )}
    </TelNodeShell>
  )
}
