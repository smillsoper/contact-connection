import type { NodeProps } from '@xyflow/react'
import NodeShell from './NodeShell'
import type { NodeData } from '../../../types/designer'

function stripHtml(html: string): string {
  return html.replace(/<[^>]+>/g, '').replace(/&nbsp;/g, ' ').trim()
}

export default function ScriptNode({ data, selected }: NodeProps & { data: NodeData }) {
  const preview = data.content ? stripHtml(data.content as string) : ''
  return (
    <NodeShell type="script" label={data.label as string} isEntry={data.isEntry as boolean} selected={selected}>
      {preview ? (
        <p className="text-xs text-gray-400 mt-0.5 truncate">{preview}</p>
      ) : (
        <p className="text-xs text-gray-500 mt-0.5 italic">No content</p>
      )}
      {data.waitForTelephonyEventName ? (
        <p className="text-[10px] text-rose-400 mt-1">⏱ auto-advances on '{data.waitForTelephonyEventName as string}' end</p>
      ) : null}
    </NodeShell>
  )
}
