import type { NodeProps } from '@xyflow/react'
import { Handle, Position } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'
import { FileIcon } from '../../icons/Icons'

export default function AddressNode({ data, selected }: NodeProps & { data: NodeData }) {
  const outputVariable   = (data.outputVariable as string) ?? ''
  const allowIntl        = data.allowInternational as boolean
  const showMI           = data.showMiddleInitial as boolean
  const showCompany      = data.showCompany as boolean
  const scriptLabel      = (data.scriptLabel as string) ?? ''
  const scriptContent    = (data.scriptContent as string) ?? ''
  const hasScript        = scriptLabel || scriptContent
  const reqFields        = (data.requiredFields as string[]) ?? []

  return (
    <NodeShell
      type="address"
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
        address
        {allowIntl && ' · intl'}
        {showMI    && ' · MI'}
        {showCompany && ' · co.'}
        {reqFields.length > 0 && ` · ${reqFields.length} required`}
      </p>

      {outputVariable && (
        <p className="text-[10px] text-orange-400 mt-0.5 font-mono truncate">
          → {'{{flow.' + outputVariable + '}}'}
        </p>
      )}
    </NodeShell>
  )
}
