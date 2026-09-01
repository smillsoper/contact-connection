import { Handle, Position, type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function IvrMenuNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const options = (data.options as { digit: string; transition: string }[] | undefined) ?? []
  const hasPrompt = !!(data.promptAudioFileId as string)

  // one source handle per option's transition, plus a trailing no_match
  const slots = [...options.map((o) => o.transition), 'no_match']
  const extraHandles = (
    <>
      {slots.map((id, i) => (
        <Handle
          key={id}
          type="source"
          position={Position.Bottom}
          id={id}
          style={{
            left: `${((i + 1) / (slots.length + 1)) * 100}%`,
            background: id === 'no_match' ? '#6b7280' : '#0d9488',
          }}
        />
      ))}
    </>
  )

  return (
    <TelNodeShell
      type="tf_ivr_menu"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
      extraHandles={extraHandles}
    >
      <p className="text-[11px] text-teal-300 mt-0.5 truncate">
        {hasPrompt ? 'audio prompt' : '⚠ no prompt set'}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">
        {options.length} option{options.length === 1 ? '' : 's'} · {String((data.maxDigits as number) ?? 1)} digit
        {((data.maxDigits as number) ?? 1) === 1 ? '' : 's'} · {String((data.maxTries as number) ?? 3)} tries
      </p>
    </TelNodeShell>
  )
}
