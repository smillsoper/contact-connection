import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function CancelDialNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const message = (data.cancelMessage as string) ?? ''
  const preview = message.split('\n')[0].trim()
  return (
    <TelNodeShell type="tf_cancel_dial" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      {preview ? (
        <p className="text-[10px] text-orange-300 truncate mt-0.5 italic">"{preview}"</p>
      ) : (
        <p className="text-[10px] text-gray-600 mt-0.5">No message set</p>
      )}
    </TelNodeShell>
  )
}
