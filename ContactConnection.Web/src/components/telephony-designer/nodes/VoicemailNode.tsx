import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function VoicemailNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const hasGreeting = !!(data.greetingAudioFileId as string) || !!(data.greetingTtsText as string)
  const emailOn = (data.deliveryEmailEnabled as boolean) ?? false

  return (
    <TelNodeShell
      type="tf_voicemail"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
    >
      <p className="text-[11px] text-purple-300 mt-0.5 truncate">
        {hasGreeting ? 'greeting set' : '⚠ no greeting'} · {String((data.maxLengthSeconds as number) ?? 120)}s max
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">
        {emailOn ? '✉ emails the message' : 'inbox only'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">recorded / no_message</p>
    </TelNodeShell>
  )
}
