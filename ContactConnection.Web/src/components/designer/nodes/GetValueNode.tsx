import type { NodeProps } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'

const SCOPE_LABELS: Record<string, string> = { tenant: 'Tenant', client: 'Client', campaign: 'Campaign' }

export default function GetValueNode({ data, selected }: NodeProps & { data: NodeData }) {
  const scope = (data.scope as string) ?? 'campaign'
  const keyName = (data.keyName as string) ?? ''
  const outputVariable = (data.outputVariable as string) ?? ''

  return (
    <NodeShell type="get_value" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      <p className="text-xs text-gray-400 mt-0.5 truncate">
        {keyName ? `[${SCOPE_LABELS[scope] ?? scope}] ${keyName}` : '— no key set'}
      </p>
      {outputVariable && (
        <p className="text-[10px] text-emerald-400 mt-0.5 font-mono truncate">
          → {'{{flow.' + outputVariable + '}}'}
        </p>
      )}
    </NodeShell>
  )
}
