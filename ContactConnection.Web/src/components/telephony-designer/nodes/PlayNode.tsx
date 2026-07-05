import { Handle, Position, type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

const HANDLE_COLOR = '#0f766e'

interface PlayHandle { id: string; label: string }

function getPlayHandles(data: TelNodeData): PlayHandle[] {
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
  return handles
}

export default function PlayNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const handles = getPlayHandles(data)
  const audioSource = (data.audioSource as string) ?? 'file'
  const autoRestart = data.autoRestart as boolean | undefined

  // Always provide extraHandles so TelNodeShell's built-in single/dual logic is bypassed
  const extraHandles =
    handles.length > 0 ? (
      <>
        {handles.map((h, i) => (
          <Handle
            key={h.id}
            type="source"
            position={Position.Bottom}
            id={h.id}
            style={{
              left: `${((i + 1) / (handles.length + 1)) * 100}%`,
              background: HANDLE_COLOR,
            }}
          />
        ))}
      </>
    ) : (
      // No dynamic handles (looping file with no duration) — single pass-through handle
      <Handle type="source" position={Position.Bottom} id="default" style={{ background: '#9ca3af' }} />
    )

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
      extraHandles={extraHandles}
    >
      <p className="text-xs text-gray-400 mt-1 truncate">{sourceSummary}</p>
    </TelNodeShell>
  )
}
