import { Handle, Position, type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

const DEST_LABEL: Record<string, string> = {
  campaign_queue: 'another campaign queue',
  agent: 'a specific agent',
  telephony_flow: 'another telephony flow',
  external_number: 'an external number',
}

export default function TransferNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const dest = (data.destinationType as string) ?? 'campaign_queue'

  let detail = DEST_LABEL[dest] ?? dest
  if (dest === 'agent' && data.agentExtension) detail = `agent ext ${data.agentExtension}`
  if (dest === 'external_number' && data.externalNumber) detail = String(data.externalNumber)

  const extraHandles = (
    <>
      <Handle type="source" position={Position.Bottom} id="transferred" style={{ left: '30%', background: '#22c55e' }} />
      <Handle type="source" position={Position.Bottom} id="failed" style={{ left: '70%', background: '#b91c1c' }} />
    </>
  )

  return (
    <TelNodeShell
      type="tf_transfer"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
      extraHandles={extraHandles}
    >
      <p className="text-[11px] text-indigo-300 mt-0.5 truncate">→ {detail}</p>
      <p className="text-[10px] text-gray-500 mt-0.5">
        {data.screenPopFlowId ? 'custom screen pop' : 'default screen pop'}
        {(data.announceAudioFileId || data.announceTtsText) ? ' · announces' : ''}
      </p>
    </TelNodeShell>
  )
}
