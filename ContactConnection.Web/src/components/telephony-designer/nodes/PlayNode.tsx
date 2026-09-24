import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export interface PlayHandle { id: string; label: string }

// The set of exit options for a tf_play node depends on its own configuration — exported so
// TelephonyDesignerPage.tsx's computePickerOptions() can derive the live option list for the
// connection modal without re-deriving this logic.
export function getPlayHandles(data: TelNodeData): PlayHandle[] {
  const handles: PlayHandle[] = []
  const audioSource = (data.audioSource as string) ?? 'file'
  const autoRestart = data.autoRestart as boolean | undefined
  const durationSeconds = (data.durationSeconds as number | undefined) ?? 0

  if (audioSource === 'tts') {
    handles.push({ id: 'tts_finished', label: 'TTS Finished' })
  } else if (!autoRestart) {
    handles.push({ id: 'end_of_stream', label: 'End Of Play Stream' })
  }
  if (durationSeconds > 0) {
    handles.push({ id: 'duration_reached', label: 'Duration Reached' })
  }
  const interruptDigits = (data.interruptDigits as string | undefined)?.trim()
  if (interruptDigits) {
    handles.push({ id: 'interrupted', label: 'Interrupted' })
  }
  return handles
}

export default function PlayNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const audioSource = (data.audioSource as string) ?? 'file'
  const autoRestart = data.autoRestart as boolean | undefined

  const sourceSummary =
    audioSource === 'tts'
      ? `TTS: "${((data.ttsText as string) ?? '').slice(0, 22) || '(no text)'}"…`
      : autoRestart
        ? 'File — loops'
        : 'File — plays once'

  return (
    <TelNodeShell
      type="tf_play"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
    >
      <p className="text-xs text-gray-400 mt-1 truncate">{sourceSummary}</p>
    </TelNodeShell>
  )
}
