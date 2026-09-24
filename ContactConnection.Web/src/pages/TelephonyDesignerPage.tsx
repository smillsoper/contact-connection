import { useCallback, useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import {
  ReactFlow,
  ReactFlowProvider,
  useNodesState,
  useEdgesState,
  addEdge,
  Background,
  Controls,
  MiniMap,
  useReactFlow,
  type Node,
  type Edge,
  type Connection,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'

import { flowsApi } from '../api/flows'
import TelephonyNodePalette from '../components/telephony-designer/TelephonyNodePalette'
import TelephonyNodePropertiesPanel from '../components/telephony-designer/TelephonyNodePropertiesPanel'
import EditableEdge from '../components/designer/EditableEdge'
import CanvasSelectionToggle from '../components/designer/CanvasSelectionToggle'
import { useCanvasClipboard } from '../components/designer/useCanvasClipboard'
import { useNodesChangeWithWaypoints } from '../components/designer/useNodesChangeWithWaypoints'
import OptionPickerModal from '../components/designer/OptionPickerModal'
import CheckBlockListNode from '../components/telephony-designer/nodes/CheckBlockListNode'
import CheckAgentAvailabilityNode from '../components/telephony-designer/nodes/CheckAgentAvailabilityNode'
import RejectNode from '../components/telephony-designer/nodes/RejectNode'
import AnswerNode from '../components/telephony-designer/nodes/AnswerNode'
import HangupNode from '../components/telephony-designer/nodes/HangupNode'
import RouteToQueueNode from '../components/telephony-designer/nodes/RouteToQueueNode'
import TransferNode from '../components/telephony-designer/nodes/TransferNode'
import PlayNode, { getPlayHandles } from '../components/telephony-designer/nodes/PlayNode'
import TimeOfDayNode from '../components/telephony-designer/nodes/TimeOfDayNode'
import TelBranchNode from '../components/telephony-designer/nodes/TelBranchNode'
import TelEndNode from '../components/telephony-designer/nodes/TelEndNode'
import TelSetVariableNode from '../components/telephony-designer/nodes/TelSetVariableNode'
import GetSipHeaderNode from '../components/telephony-designer/nodes/GetSipHeaderNode'
import SetSipHeaderNode from '../components/telephony-designer/nodes/SetSipHeaderNode'
import SetCallerIdNode from '../components/telephony-designer/nodes/SetCallerIdNode'
import CancelDialNode from '../components/telephony-designer/nodes/CancelDialNode'
import ScriptPopNode from '../components/telephony-designer/nodes/ScriptPopNode'
import OnAgentSelectedNode from '../components/telephony-designer/nodes/OnAgentSelectedNode'
import OnAgentAnswerNode from '../components/telephony-designer/nodes/OnAgentAnswerNode'
import OnCallDisconnectedNode from '../components/telephony-designer/nodes/OnCallDisconnectedNode'
import OnCustomEventNode from '../components/telephony-designer/nodes/OnCustomEventNode'
import DtmfNode from '../components/telephony-designer/nodes/DtmfNode'
import IvrMenuNode from '../components/telephony-designer/nodes/IvrMenuNode'
import ClearHotDigitListenerNode from '../components/telephony-designer/nodes/ClearHotDigitListenerNode'
import SecureCollectNode from '../components/telephony-designer/nodes/SecureCollectNode'
import DataCollectNode from '../components/telephony-designer/nodes/DataCollectNode'
import DelayNode from '../components/telephony-designer/nodes/DelayNode'
import RepeatNode from '../components/telephony-designer/nodes/RepeatNode'
import RecordNode from '../components/telephony-designer/nodes/RecordNode'
import VoicemailNode from '../components/telephony-designer/nodes/VoicemailNode'
import ScheduledCallbackNode from '../components/telephony-designer/nodes/ScheduledCallbackNode'
import QueueCallbackNode from '../components/telephony-designer/nodes/QueueCallbackNode'
import WhisperNode from '../components/telephony-designer/nodes/WhisperNode'
import GeneralApiCallNode from '../components/telephony-designer/nodes/GeneralApiCallNode'
import SetCustomFieldNode from '../components/telephony-designer/nodes/SetCustomFieldNode'
import GetCustomFieldNode from '../components/telephony-designer/nodes/GetCustomFieldNode'
import StoreValueNode from '../components/telephony-designer/nodes/StoreValueNode'
import GetValueNode from '../components/telephony-designer/nodes/GetValueNode'

import type { TelNodeData, TelephonyNodeType, TelephonyFlowDefinition, TelephonyNodeDef, TimeWindow } from '../types/telephony-designer'
import { defaultTelNodeData, TELEPHONY_NODE_META } from '../types/telephony-designer'
import VersionHistoryPanel from '../components/versioning/VersionHistoryPanel'

const EVENT_LISTENER_TYPES: TelephonyNodeType[] = [
  'tf_on_agent_selected',
  'tf_on_agent_answer',
  'tf_on_call_disconnected',
  'tf_on_custom_event',
]

// Maps internal handle/option IDs to display labels shown in the picker modal and on edges
const HANDLE_DISPLAY_LABELS: Record<string, string> = {
  end_of_stream: 'End Of Play Stream',
  duration_reached: 'Duration Reached',
  tts_finished: 'TTS Finished',
  interrupted: 'Interrupted',
  available: 'Available',
  unavailable: 'Not Available',
  not_blocked: 'Not Blocked',
  no_match: 'No Match',
  invalid_time: 'Invalid Time',
  no_message: 'No Message',
}

function formatOptionLabel(opt: string): string {
  return HANDLE_DISPLAY_LABELS[opt] ?? (opt.charAt(0).toUpperCase() + opt.slice(1))
}

// Every telephony node type now funnels its exit(s) through a single physical handle — a plain
// pass-through connect for a single-transition node, or (for a node with named branches) the
// same option-picker modal UX as the CRM designer's select-input node. Static option lists live
// here; a handful of node types compute their live option list from their own data instead (see
// computePickerOptions below) because the branch set is user-configured per node instance.
const FIXED_EXIT_OPTIONS: Partial<Record<TelephonyNodeType, string[]>> = {
  tf_general_api_call: ['success', 'error', 'timeout'],
  tf_set_custom_field: ['success', 'invalid_value', 'error'],
  tf_check_block_list: ['blocked', 'not_blocked'],
  tf_check_agent_availability: ['available', 'unavailable'],
  tf_branch: ['true', 'false'],
  tf_route_to_queue: ['default', 'on_timeout'],
  tf_transfer: ['transferred', 'failed'],
  tf_secure_collect: ['collected', 'failed', 'timeout'],
  tf_data_collect: ['collected', 'timeout'],
  tf_repeat: ['repeat', 'finished'],
  tf_voicemail: ['recorded', 'no_message'],
  tf_scheduled_callback: ['scheduled', 'invalid_time', 'failed'],
  tf_queue_callback: ['queued', 'failed'],
}

// Live exit-option list for a node instance: static lookup first, then per-type dynamic
// derivation for the three node types whose branch set depends on their own configuration.
// Returns null for a node with no picker at all (plain single-handle pass-through connect).
function computePickerOptions(type: TelephonyNodeType, data: TelNodeData): string[] | null {
  const fixed = FIXED_EXIT_OPTIONS[type]
  if (fixed) return fixed
  if (type === 'tf_time_of_day') {
    const windows = (data.windows as TimeWindow[] | undefined) ?? []
    return [...windows.map((w) => w.name), 'no_match']
  }
  if (type === 'tf_ivr_menu') {
    const options = (data.options as { transition: string }[] | undefined) ?? []
    const trailing = (data.alwaysListen as boolean) ? 'default' : 'no_match'
    return [...options.map((o) => o.transition), trailing]
  }
  if (type === 'tf_play') {
    const handles = getPlayHandles(data)
    // A looping file with no duration/interrupt gate has no named branches at all — plain handle.
    return handles.length > 0 ? handles.map((h) => h.id) : null
  }
  return null
}

const nodeTypes = {
  tf_check_block_list: CheckBlockListNode,
  tf_check_agent_availability: CheckAgentAvailabilityNode,
  tf_reject: RejectNode,
  tf_answer: AnswerNode,
  tf_hangup: HangupNode,
  tf_route_to_queue: RouteToQueueNode,
  tf_transfer: TransferNode,
  tf_play: PlayNode,
  tf_time_of_day: TimeOfDayNode,
  tf_branch: TelBranchNode,
  tf_end: TelEndNode,
  tf_set_variable: TelSetVariableNode,
  tf_get_sip_header: GetSipHeaderNode,
  tf_set_sip_header: SetSipHeaderNode,
  tf_set_caller_id: SetCallerIdNode,
  tf_cancel_dial: CancelDialNode,
  tf_script_pop: ScriptPopNode,
  tf_dtmf: DtmfNode,
  tf_ivr_menu: IvrMenuNode,
  tf_clear_hot_digit: ClearHotDigitListenerNode,
  tf_secure_collect: SecureCollectNode,
  tf_data_collect: DataCollectNode,
  tf_delay: DelayNode,
  tf_repeat: RepeatNode,
  tf_record: RecordNode,
  tf_voicemail: VoicemailNode,
  tf_scheduled_callback: ScheduledCallbackNode,
  tf_queue_callback: QueueCallbackNode,
  tf_whisper: WhisperNode,
  tf_general_api_call: GeneralApiCallNode,
  tf_set_custom_field: SetCustomFieldNode,
  tf_get_custom_field: GetCustomFieldNode,
  tf_store_value: StoreValueNode,
  tf_get_value: GetValueNode,
  tf_on_agent_selected: OnAgentSelectedNode,
  tf_on_agent_answer: OnAgentAnswerNode,
  tf_on_call_disconnected: OnCallDisconnectedNode,
  tf_on_custom_event: OnCustomEventNode,
}

const edgeTypes = { editable: EditableEdge }

// ── Conversion helpers ──────────────────────────────────────────────────────

function toTelDef(
  nodes: Node<TelNodeData>[],
  edges: Edge[],
  entryNodeId: string | null,
  flowName: string,
): TelephonyFlowDefinition {
  const waypointsMap: Record<string, { x: number; y: number }[]> = {}
  for (const e of edges) {
    const wps = e.data?.waypoints as { x: number; y: number }[] | undefined
    if (wps && wps.length > 0) waypointsMap[e.id] = wps
  }

  const defNodes: TelephonyFlowDefinition['nodes'] = {}
  for (const n of nodes) {
    const outgoing = edges.filter((e) => e.source === n.id)
    const transitions: Record<string, string> = {}
    for (const e of outgoing) {
      const key = (e.data?.transition as string | undefined) ?? e.sourceHandle ?? 'default'
      transitions[key] = e.target
    }
    const { isEntry: _entry, ...rest } = n.data
    defNodes[n.id] = {
      ...rest,
      type: (n.type ?? 'tf_end') as TelephonyNodeType,
      label: n.data.label as string,
      _pos: n.position,
      transitions,
    } as TelephonyNodeDef
  }

  return {
    flow_type: 'telephony',
    name: flowName,
    entry_node: entryNodeId ?? nodes[0]?.id ?? '',
    nodes: defNodes,
    ...(Object.keys(waypointsMap).length > 0 ? { _waypoints: waypointsMap } : {}),
  }
}

// Every node type has at most one physical source handle now, so "default" is always the
// implicit/only handle — collapsed to null so React Flow matches it against that one unlabeled
// handle and no redundant "default" label shows on the wire.
//
// (Historical note: this used to take a `collapseDefault` flag, because some node types rendered
// several REAL positioned handles where "default" was one of several genuine handle ids
// (tf_route_to_queue's default+on_timeout; tf_ivr_menu's per-digit options + default in
// async/hot-digit mode) — collapsing it there was wrong and caused a real mis-attachment bug
// (S141). Those node types are now on the single-handle option-picker pattern instead — see
// FIXED_EXIT_OPTIONS / computePickerOptions — so there is no longer a case where "default" is a
// real handle id rather than the implicit one; picker-node transitions bypass normHandle entirely
// below, for the same reason.)
function normHandle(h: string | null | undefined): string | null {
  return !h || h === 'default' ? null : h
}

function fromTelDef(def: TelephonyFlowDefinition): {
  nodes: Node<TelNodeData>[]
  edges: Edge[]
  entryNodeId: string
} {
  const nodes: Node<TelNodeData>[] = []
  const edges: Edge[] = []
  let x = 100, y = 100

  for (const [id, nodeDef] of Object.entries(def.nodes)) {
    nodes.push({
      id,
      type: nodeDef.type,
      position: nodeDef._pos ?? { x, y },
      data: {
        ...nodeDef,
        isEntry: id === def.entry_node,
      } as TelNodeData,
    })
    x += 260
    if (x > 900) { x = 100; y += 160 }
  }

  for (const [id, nodeDef] of Object.entries(def.nodes)) {
    // Picker-pattern nodes (single physical handle; the branch name travels as edge data, never
    // as a distinct handle id) use their RAW transition key for the label/transition, bypassing
    // normHandle's "default" collapse — some of these legitimately use the literal string
    // "default" as one real, named branch (tf_route_to_queue's default queue-chain exit; async
    // tf_ivr_menu's default continuation), and collapsing it here would blank that branch's label.
    const isPickerNode = computePickerOptions(nodeDef.type, nodeDef as unknown as TelNodeData) !== null
    for (const [handle, target] of Object.entries(nodeDef.transitions)) {
      const edgeId = `e-${id}-${target}-${handle}`
      const edgeDef = def._waypoints?.[edgeId]
      const normalizedHandle = normHandle(handle)
      const visualHandle = isPickerNode ? null : normalizedHandle
      // Dedup on the visual handle only makes sense for a real positioned handle. Every picker-node
      // edge shares the same null visual handle by design, so this check must be skipped for them —
      // otherwise two genuinely different branches that happen to both point at the same target
      // (e.g. tf_route_to_queue's "default" and "on_timeout" both wired to the same next node, which
      // the multi-select picker modal makes easy to create in one action) would collide here and one
      // would silently vanish on the next reload.
      const seen = !isPickerNode && edges.some(
        (e) => e.source === id && normHandle(e.sourceHandle) === normalizedHandle && e.target === target
      )
      if (seen) continue
      edges.push({
        id: edgeId,
        source: id,
        target,
        sourceHandle: visualHandle,
        label: isPickerNode
          ? formatOptionLabel(handle)
          : (normalizedHandle ? (HANDLE_DISPLAY_LABELS[normalizedHandle] ?? normalizedHandle) : undefined),
        type: 'editable',
        data: { waypoints: edgeDef ?? [], transition: isPickerNode ? handle : (normalizedHandle ?? 'default') },
      })
    }
  }

  return { nodes, edges, entryNodeId: def.entry_node }
}

// ── Canvas (inner — has access to useReactFlow) ──────────────────────────────

function DesignerCanvas() {
  const { id: routeId } = useParams()
  const navigate = useNavigate()
  const { screenToFlowPosition } = useReactFlow()

  const [nodes, setNodes, onNodesChangeRaw] = useNodesState<Node<TelNodeData>>([])
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([])
  const [entryNodeId, setEntryNodeId] = useState<string | null>(null)
  const [flowId, setFlowId] = useState<string | null>(routeId ?? null)
  const [flowName, setFlowName] = useState('New Telephony Flow')
  const [flowDirection, setFlowDirection] = useState<string>('inbound')
  const [flowSubType, setFlowSubType] = useState<string>('')
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [status, setStatus] = useState('')
  const [showHistory, setShowHistory] = useState(false)
  const [selectionModeOn, setSelectionModeOn] = useState(false)

  // Option-picker modal for fixed-exit-option nodes (e.g. tf_general_api_call)
  const [pendingConn, setPendingConn] = useState<Connection | null>(null)

  const makeEdgeId = useCallback(
    (newSource: string, newTarget: string, key: string) => `e-${newSource}-${newTarget}-${key}`,
    [],
  )
  const onNodesRemoved = useCallback(
    (removedIds: string[]) => {
      setSelectedNodeId((cur) => (cur && removedIds.includes(cur) ? null : cur))
      setEntryNodeId((cur) => (cur && removedIds.includes(cur) ? null : cur))
    },
    [],
  )
  useCanvasClipboard<TelNodeData>({ nodes, edges, setNodes, setEdges, makeEdgeId, onNodesRemoved })
  const onNodesChange = useNodesChangeWithWaypoints(nodes, onNodesChangeRaw, setEdges)

  // Load an existing flow's current definition into the canvas — used on mount and again after
  // a version-history revert (the revert response carries the newly-active definition).
  const loadFlow = useCallback((id: string) => {
    return flowsApi.getDetail(id).then((detail) => {
      if (!detail.definition) return
      const def = JSON.parse(detail.definition) as TelephonyFlowDefinition
      const { nodes: n, edges: e, entryNodeId: entry } = fromTelDef(def)
      setNodes(n)
      setEdges(e)
      setEntryNodeId(entry)
      setFlowName(detail.name)
      setFlowDirection(detail.flow_direction ?? 'inbound')
      setFlowSubType(detail.flow_sub_type ?? '')
      setFlowId(detail.id)
    })
  }, [setNodes, setEdges])

  // Load existing flow
  useEffect(() => {
    if (!routeId) return
    loadFlow(routeId)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [routeId])

  // When one or more options are confirmed in the modal, wire each to the pending target in one
  // update (single physical "default" handle; each chosen option travels as edge data, not as a
  // distinct handle id) — re-wiring any that were already wired elsewhere.
  const onOptionsConfirmed = useCallback((optionValues: string[]) => {
    if (!pendingConn) return
    const conn = pendingConn
    setPendingConn(null)
    const optionSet = new Set(optionValues)
    setEdges((eds) => {
      const withoutOld = eds.filter((e) => {
        if (e.source !== conn.source) return true
        const key = (e.data?.transition as string | undefined) ?? e.sourceHandle
        return !key || !optionSet.has(key)
      })
      const newEdges: Edge[] = optionValues.map((optionValue) => ({
        ...conn,
        id: `e-${conn.source}-${conn.target}-${optionValue}`,
        sourceHandle: null,
        type: 'editable',
        label: formatOptionLabel(optionValue),
        data: { waypoints: [], transition: optionValue },
      }))
      return [...withoutOld, ...newEdges]
    })
  }, [pendingConn, setEdges])

  const onConnect = useCallback(
    (connection: Connection) => {
      const sourceNode = nodes.find((n) => n.id === connection.source)
      const options = sourceNode ? computePickerOptions(sourceNode.type as TelephonyNodeType, sourceNode.data) : null
      if (options !== null) {
        setPendingConn(connection)
        return
      }
      // Every remaining node type has exactly one physical handle, so normalize both sides the
      // same way normHandle() does when loading saved flows before comparing. Without this, a live
      // drag off a handle whose JSX id="default" reports sourceHandle="default" while an
      // already-loaded edge from that same handle was normalized to null on load — the raw `===`
      // check would then miss the old edge and leave it dangling alongside the new one.
      setEdges((eds) => {
        // Enforce one outgoing edge per source handle
        const filtered = eds.filter(
          (e) => !(e.source === connection.source
            && normHandle(e.sourceHandle) === normHandle(connection.sourceHandle)),
        )
        const transition = connection.sourceHandle && connection.sourceHandle !== 'default'
          ? connection.sourceHandle
          : 'default'
        const displayLabel = transition !== 'default'
          ? (HANDLE_DISPLAY_LABELS[transition] ?? transition)
          : undefined
        return addEdge(
          {
            ...connection,
            type: 'editable',
            label: displayLabel,
            data: { waypoints: [], transition },
          },
          filtered,
        )
      })
    },
    [nodes, setEdges],
  )

  const onDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault()
      const type = e.dataTransfer.getData('application/tel-node-type') as TelephonyNodeType
      if (!type) return
      const position = screenToFlowPosition({ x: e.clientX, y: e.clientY })
      // Date.now()-based, not a per-session counter (which resets to 0 on every page load and
      // collides with same-typed nodes already in a loaded flow — e.g. re-dropping a "Play" node
      // after reopening the designer would regenerate "tf_play_1", clobbering the existing node
      // of that id: React Flow keys nodes by id, so the drop's fresh blank data silently
      // overwrote the old node's properties while its edges — unaffected, since they reference
      // the same id — stayed attached, making it look like the old node "moved" to the drop spot).
      const id = `${type}_${Date.now()}`
      const isEventNode = EVENT_LISTENER_TYPES.includes(type)
      setNodes((nds) => {
        const noEntry = !nds.some((n) => n.data.isEntry)
        if (noEntry && !isEventNode) setEntryNodeId(id)
        return [...nds, {
          id,
          type,
          position,
          data: { ...defaultTelNodeData(type), isEntry: noEntry && !isEventNode },
        }]
      })
    },
    [screenToFlowPosition, setNodes],
  )

  const onDragOver = (e: React.DragEvent) => { e.preventDefault(); e.dataTransfer.dropEffect = 'move' }

  const onNodeChange = (id: string, data: Partial<TelNodeData>) => {
    setNodes((nds) => nds.map((n) => (n.id === id ? { ...n, data: { ...n.data, ...data } } : n)))
  }

  const onSetEntry = (id: string) => {
    setEntryNodeId(id)
    setNodes((nds) => nds.map((n) => ({ ...n, data: { ...n.data, isEntry: n.id === id } })))
  }

  const onDeleteNode = (id: string) => {
    setNodes((nds) => nds.filter((n) => n.id !== id))
    setEdges((eds) => eds.filter((e) => e.source !== id && e.target !== id))
    setSelectedNodeId(null)
  }

  const selectedNode = nodes.find((n) => n.id === selectedNodeId) ?? null

  const handleSave = async () => {
    const def = toTelDef(nodes, edges, entryNodeId, flowName)
    setStatus('Saving…')
    try {
      const dir = flowDirection || undefined
      const sub = (flowDirection === 'outbound' && flowSubType) ? flowSubType : undefined
      if (flowId) {
        await flowsApi.updateDefinition(flowId, flowName, def as unknown as import('../types/designer').ContactConnectionFlowDefinition, dir, sub)
        setStatus('Saved')
      } else {
        const created = await flowsApi.create(flowName, 'telephony', def as unknown as import('../types/designer').ContactConnectionFlowDefinition, dir, sub)
        setFlowId(created.id)
        navigate(`/telephony-designer/${created.id}`, { replace: true })
        setStatus('Saved')
      }
    } catch { setStatus('Save failed') }
    setTimeout(() => setStatus(''), 3000)
  }

  const handlePublish = async () => {
    if (!flowId) { await handleSave(); return }
    setStatus('Publishing…')
    try {
      await flowsApi.publish(flowId)
      setStatus('Published')
    } catch { setStatus('Publish failed') }
    setTimeout(() => setStatus(''), 3000)
  }

  return (
    <div className="flex flex-col h-screen bg-gray-950 text-gray-100">
      {/* Top bar */}
      <div className="flex items-center gap-3 px-4 py-2 bg-gray-900 border-b border-gray-700 shrink-0">
        <button
          onClick={() => navigate('/flows')}
          className="text-sm text-gray-400 hover:text-gray-200"
        >
          ← Flows
        </button>
        <div className="w-px h-5 bg-gray-700" />
        <input
          className="bg-transparent text-sm font-semibold text-gray-100 border-b border-transparent hover:border-gray-600 focus:border-blue-500 focus:outline-none px-1 min-w-48"
          value={flowName}
          onChange={(e) => setFlowName(e.target.value)}
        />
        <div className="w-px h-5 bg-gray-700" />
        <select
          value={flowDirection}
          onChange={(e) => { setFlowDirection(e.target.value); if (e.target.value === 'inbound') setFlowSubType('') }}
          className="bg-gray-800 border border-gray-600 text-gray-200 text-sm rounded px-2 py-1 focus:outline-none focus:border-blue-500"
        >
          <option value="inbound">Inbound</option>
          <option value="outbound">Outbound</option>
        </select>
        {flowDirection === 'outbound' && (
          <select
            value={flowSubType}
            onChange={(e) => setFlowSubType(e.target.value)}
            className="bg-gray-800 border border-gray-600 text-gray-200 text-sm rounded px-2 py-1 focus:outline-none focus:border-blue-500"
          >
            <option value="">Sub-type…</option>
            <option value="manual">Manual</option>
            <option value="progressive">Progressive</option>
            <option value="predictive">Predictive</option>
          </select>
        )}
        <div className="flex-1" />
        {status && <span className="text-xs text-gray-400">{status}</span>}
        {flowId && (
          <button
            onClick={() => setShowHistory(true)}
            className="text-sm border border-gray-600 text-gray-300 hover:bg-gray-800 rounded px-3 py-1"
          >
            History
          </button>
        )}
        <button
          onClick={handleSave}
          className="text-sm bg-gray-700 hover:bg-gray-600 text-gray-100 rounded px-3 py-1"
        >
          Save
        </button>
        <button
          onClick={handlePublish}
          className="text-sm bg-blue-600 hover:bg-blue-500 text-white rounded px-3 py-1"
        >
          Publish
        </button>
      </div>

      {/* Main content */}
      <div className="flex flex-1 overflow-hidden">
        <TelephonyNodePalette direction={flowDirection} subType={flowSubType} />

        <div className="flex-1">
          <ReactFlow
            nodes={nodes}
            edges={edges}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            onDrop={onDrop}
            onDragOver={onDragOver}
            onNodeClick={(_, n) => setSelectedNodeId(n.id)}
            onPaneClick={() => setSelectedNodeId(null)}
            onNodesDelete={(deleted) => onNodesRemoved(deleted.map((n) => n.id))}
            fitView
            deleteKeyCode="Delete"
            colorMode="dark"
            selectionOnDrag={selectionModeOn}
            panOnDrag={selectionModeOn ? [2] : true}
          >
            <Background color="#374151" gap={20} />
            <Controls>
              <CanvasSelectionToggle active={selectionModeOn} onToggle={() => setSelectionModeOn((v) => !v)} />
            </Controls>
            <MiniMap
              nodeColor={(n) => TELEPHONY_NODE_META[n.type as TelephonyNodeType]?.color ?? '#374151'}
              style={{ background: '#1f2937' }}
            />
          </ReactFlow>
        </div>

        {selectedNode && (
          // key by node id so every field/picker re-initialises from the newly-selected node —
          // without it, sub-picker state (e.g. the TTS provider tab) leaks from the previous node
          // and can show the wrong thing as "selected".
          <TelephonyNodePropertiesPanel
            key={selectedNode.id}
            node={selectedNode}
            entryNodeId={entryNodeId}
            onChange={onNodeChange}
            onSetEntry={onSetEntry}
            onDelete={onDeleteNode}
          />
        )}
      </div>

      {/* Option picker modal — shown when connecting from a node whose branches funnel through one handle */}
      {pendingConn && (() => {
        const srcNode = nodes.find((n) => n.id === pendingConn.source)
        const options = (srcNode ? computePickerOptions(srcNode.type as TelephonyNodeType, srcNode.data) : null) ?? []
        const wiredMap = new Map(
          edges
            .filter((e) => e.source === pendingConn.source)
            .map((e) => {
              const key = (e.data?.transition as string | undefined) ?? e.sourceHandle
              return key ? ([key, e.target] as [string, string]) : null
            })
            .filter((x): x is [string, string] => x !== null),
        )
        return (
          <OptionPickerModal
            options={options}
            wiredMap={wiredMap}
            formatLabel={formatOptionLabel}
            onConfirm={onOptionsConfirmed}
            onCancel={() => setPendingConn(null)}
          />
        )
      })()}

      {showHistory && flowId && (
        <VersionHistoryPanel
          title="Flow Version History"
          subtitle={flowName}
          listVersions={() => flowsApi.listVersions(flowId)}
          onRevert={async (versionNumber) => {
            await flowsApi.revert(flowId, versionNumber)
            await loadFlow(flowId)
          }}
          onClose={() => setShowHistory(false)}
        />
      )}
    </div>
  )
}

export default function TelephonyDesignerPage() {
  return (
    <ReactFlowProvider>
      <DesignerCanvas />
    </ReactFlowProvider>
  )
}
