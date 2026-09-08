import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function WhisperNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const audioSource = (data.audioSource as string) ?? 'file'
  const fileId = (data.audioFileId as string) ?? ''
  const ttsText = (data.ttsText as string) ?? ''

  const summary =
    audioSource === 'tts'
      ? ttsText.trim()
        ? `“${ttsText.trim().slice(0, 32)}${ttsText.trim().length > 32 ? '…' : ''}”`
        : '⚠ no TTS text'
      : fileId
        ? 'agent ear only'
        : '⚠ no file selected'

  return (
    <TelNodeShell type="tf_whisper" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-[11px] text-purple-300 mt-0.5 truncate">{summary}</p>
    </TelNodeShell>
  )
}
