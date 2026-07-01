import type { TelephonyNodeType } from '../../types/telephony-designer'
import { TELEPHONY_NODE_META } from '../../types/telephony-designer'

const INBOUND_NODES: TelephonyNodeType[] = [
  'tf_check_block_list',
  'tf_check_agent_availability',
  'tf_time_of_day',
  'tf_branch',
  'tf_answer',
  'tf_reject',
  'tf_hangup',
  'tf_route_to_queue',
  'tf_set_variable',
  'tf_get_sip_header',
  'tf_set_sip_header',
  'tf_end',
  // Event listener nodes (independent entry points)
  'tf_on_agent_selected',
  'tf_on_agent_answer',
  'tf_on_call_disconnected',
  'tf_on_custom_event',
  // Actions available in event branches
  'tf_script_pop',
]

const OUTBOUND_NODES: TelephonyNodeType[] = [
  'tf_time_of_day',
  'tf_branch',
  'tf_set_variable',
  'tf_set_caller_id',
  'tf_get_sip_header',
  'tf_set_sip_header',
  'tf_end',
]

const OUTBOUND_MANUAL_NODES: TelephonyNodeType[] = [
  'tf_time_of_day',
  'tf_branch',
  'tf_set_variable',
  'tf_set_caller_id',
  'tf_cancel_dial',
  'tf_get_sip_header',
  'tf_set_sip_header',
  'tf_end',
]

interface Props {
  direction: string
  subType: string
}

export default function TelephonyNodePalette({ direction, subType }: Props) {
  const onDragStart = (e: React.DragEvent, type: TelephonyNodeType) => {
    e.dataTransfer.setData('application/tel-node-type', type)
    e.dataTransfer.effectAllowed = 'move'
  }

  const nodes =
    direction === 'inbound'                             ? INBOUND_NODES :
    direction === 'outbound' && subType === 'manual'   ? OUTBOUND_MANUAL_NODES :
    direction === 'outbound'                            ? OUTBOUND_NODES :
    []

  return (
    <div className="w-44 bg-gray-900 border-r border-gray-700 flex flex-col py-3 gap-1 overflow-y-auto shrink-0">
      <p className="text-xs font-semibold text-gray-500 uppercase tracking-wide px-3 mb-1">Telephony Nodes</p>
      {nodes.length === 0 ? (
        <p className="text-[10px] text-gray-600 px-3 leading-snug mt-1">
          Select a direction in the navbar to see available nodes.
        </p>
      ) : nodes.map((type) => {
        const meta = TELEPHONY_NODE_META[type]
        return (
          <div
            key={type}
            draggable
            onDragStart={(e) => onDragStart(e, type)}
            className="mx-2 rounded cursor-grab active:cursor-grabbing select-none"
            style={{ borderLeft: `3px solid ${meta.color}` }}
          >
            <div className="bg-gray-800 hover:bg-gray-700 px-3 py-2 rounded-r">
              <p className="text-xs font-semibold text-gray-100">{meta.label}</p>
              <p className="text-[10px] text-gray-400 leading-tight">{meta.description}</p>
            </div>
          </div>
        )
      })}
    </div>
  )
}
