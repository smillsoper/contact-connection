import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function IvrMenuNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const options = (data.options as { digit: string; transition: string; phrases?: string[] }[] | undefined) ?? []
  const hasPrompt = !!(data.promptAudioFileId as string)
  const isAsync = !!(data.alwaysListen as boolean)
  const hasVoice = !isAsync && options.some((o) => (o.phrases ?? []).length > 0)

  return (
    <TelNodeShell
      type="tf_ivr_menu"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
    >
      {isAsync ? (
        <p className="text-[11px] text-indigo-300 mt-0.5">🎧 always listening</p>
      ) : (
        <p className="text-[11px] text-teal-300 mt-0.5 truncate">
          {hasPrompt ? 'audio prompt' : '⚠ no prompt set'}{hasVoice ? ' · 🎙 voice' : ''}
        </p>
      )}
      <p className="text-[10px] text-gray-500 mt-0.5">
        {isAsync
          ? `${options.length} hot digit${options.length === 1 ? '' : 's'}`
          : <>{options.length} option{options.length === 1 ? '' : 's'} · {String((data.maxDigits as number) ?? 1)} digit
            {((data.maxDigits as number) ?? 1) === 1 ? '' : 's'} · {String((data.maxTries as number) ?? 3)} tries</>}
      </p>
    </TelNodeShell>
  )
}
