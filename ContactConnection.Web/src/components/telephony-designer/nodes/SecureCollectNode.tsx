import { type NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

export default function SecureCollectNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const fields = (data.fields as { key: string }[] | undefined) ?? []

  return (
    <TelNodeShell
      type="tf_secure_collect"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
    >
      <p className="text-[11px] text-rose-300 mt-0.5 truncate">PCI · masks recording · guided DTMF</p>
      <p className="text-[10px] text-gray-500 mt-0.5 truncate">
        {fields.length === 0 ? 'no fields configured' : fields.map((f) => f.key).join(' · ')}
      </p>
      <p className="text-[10px] text-gray-500 mt-0.5">collected / failed / timeout</p>
    </TelNodeShell>
  )
}
