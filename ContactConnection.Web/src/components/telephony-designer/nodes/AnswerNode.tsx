import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function AnswerNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  return (
    <TelNodeShell type="tf_answer" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected} />
  )
}
