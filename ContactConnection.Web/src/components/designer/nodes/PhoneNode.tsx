import type { NodeProps } from '@xyflow/react'
import { Handle, Position } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'
import { FileIcon } from '../../icons/Icons'

export default function PhoneNode({ data, selected }: NodeProps & { data: NodeData }) {
  const outputVariable    = (data.outputVariable as string) ?? ''
  const allowInternational = data.allowInternational as boolean
  const dncCheck          = data.dncCheck as boolean
  const scriptLabel       = (data.scriptLabel as string) ?? ''
  const scriptContent     = (data.scriptContent as string) ?? ''
  const hasScript         = scriptLabel || scriptContent

  return (
    <NodeShell
      type="phone"
      label={data.label as string}
      isEntry={data.isEntry as boolean}
      selected={selected}
      sourceHandles={
        <Handle
          type="source"
          position={Position.Bottom}
          id="default"
          style={{ background: '#6b7280' }}
        />
      }
    >
      {hasScript && (
        <p className="text-[10px] text-sky-400 mt-0.5 font-medium truncate">
          <FileIcon size={11} className="inline -mt-0.5 mr-1" />{scriptLabel || 'Script attached'}
        </p>
      )}
      <p className="text-xs text-gray-400 mt-0.5">
        phone
        {(data.required as boolean) && ' · required'}
        {allowInternational && ' · intl'}
      </p>

      {outputVariable && (
        <p className="text-[10px] text-teal-400 mt-0.5 font-mono truncate">
          → {'{{flow.' + outputVariable + '}}'}
        </p>
      )}

      {dncCheck && (
        <p className="text-[10px] text-gray-500 mt-0.5">DNC check enabled</p>
      )}
    </NodeShell>
  )
}
