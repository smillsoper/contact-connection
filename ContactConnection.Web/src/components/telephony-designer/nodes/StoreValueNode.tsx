import type { NodeProps } from '@xyflow/react'
import TelNodeShell from '../TelNodeShell'
import type { TelNodeData } from '../../../types/telephony-designer'

const SCOPE_LABELS: Record<string, string> = { tenant: 'Tenant', client: 'Client', campaign: 'Campaign' }

export default function StoreValueNode({ data, selected }: NodeProps & { data: TelNodeData }) {
  const scope = (data.scope as string) ?? 'campaign'
  const keyName = (data.keyName as string) ?? ''

  return (
    <TelNodeShell type="tf_store_value" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5 truncate">
        {keyName ? `[${SCOPE_LABELS[scope] ?? scope}] ${keyName}` : '— no key set'}
      </p>
    </TelNodeShell>
  )
}
