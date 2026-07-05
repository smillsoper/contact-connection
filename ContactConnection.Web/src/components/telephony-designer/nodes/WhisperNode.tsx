import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function WhisperNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const fileId = (data.audioFileId as string) ?? ''
  return (
    <TelNodeShell type="tf_whisper" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-[11px] text-purple-300 mt-0.5 truncate">
        {fileId ? 'agent ear only' : '⚠ no file selected'}
      </p>
    </TelNodeShell>
  )
}
