import { useCallback, useEffect, useRef, useState } from 'react'
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
import CheckBlockListNode from '../components/telephony-designer/nodes/CheckBlockListNode'
import CheckAgentAvailabilityNode from '../components/telephony-designer/nodes/CheckAgentAvailabilityNode'
import RejectNode from '../components/telephony-designer/nodes/RejectNode'
import AnswerNode from '../components/telephony-designer/nodes/AnswerNode'
import HangupNode from '../components/telephony-designer/nodes/HangupNode'
import RouteToQueueNode from '../components/telephony-designer/nodes/RouteToQueueNode'
import PlayNode from '../components/telephony-designer/nodes/PlayNode'
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
import WhisperNode from '../components/telephony-designer/nodes/WhisperNode'

import type { TelNodeData, TelephonyNodeType, TelephonyFlowDefinition, TelephonyNodeDef } from '../types/telephony-designer'
import { defaultTelNodeData, TELEPHONY_NODE_META } from '../types/telephony-designer'

const EVENT_LISTENER_TYPES: TelephonyNodeType[] = [
  'tf_on_agent_selected',
  'tf_on_agent_answer',
  'tf_on_call_disconnected',
  'tf_on_custom_event',
]

// Maps internal handle IDs to display labels shown on edges in the canvas
const HANDLE_DISPLAY_LABELS: Record<string, string> = {
  end_of_stream: 'End Of Play Stream',
  duration_reached: 'Duration Reached',
  tts_finished: 'TTS Finished',
}

const nodeTypes = {
  tf_check_block_list: CheckBlockListNode,
  tf_check_agent_availability: CheckAgentAvailabilityNode,
  tf_reject: RejectNode,
  tf_answer: AnswerNode,
  tf_hangup: HangupNode,
  tf_route_to_queue: RouteToQueueNode,
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
  tf_whisper: WhisperNode,
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
    for (const [handle, target] of Object.entries(nodeDef.transitions)) {
      const edgeId = `e-${id}-${target}-${handle}`
      const edgeDef = def._waypoints?.[edgeId]
      const normalizedHandle = normHandle(handle)
      const seen = edges.filter(
        (e) => e.source === id && normHandle(e.sourceHandle) === normalizedHandle && e.target === target
      )
      if (seen.length > 0) continue
      edges.push({
        id: edgeId,
        source: id,
        target,
        sourceHandle: normalizedHandle,
        label: normalizedHandle
          ? (HANDLE_DISPLAY_LABELS[normalizedHandle] ?? normalizedHandle)
          : undefined,
        type: 'editable',
        data: { waypoints: edgeDef ?? [], transition: normalizedHandle ?? 'default' },
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

  const [nodes, setNodes, onNodesChange] = useNodesState<Node<TelNodeData>>([])
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([])
  const [entryNodeId, setEntryNodeId] = useState<string | null>(null)
  const [flowId, setFlowId] = useState<string | null>(routeId ?? null)
  const [flowName, setFlowName] = useState('New Telephony Flow')
  const [flowDirection, setFlowDirection] = useState<string>('inbound')
  const [flowSubType, setFlowSubType] = useState<string>('')
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [status, setStatus] = useState('')
  const idCounter = useRef(0)

  // Load existing flow
  useEffect(() => {
    if (!routeId) return
    flowsApi.getDetail(routeId).then((detail) => {
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
  }, [routeId])

  const onConnect = useCallback(
    (connection: Connection) => {
      setEdges((eds) => {
        // Enforce one outgoing edge per source handle
        const filtered = eds.filter(
          (e) => !(e.source === connection.source && e.sourceHandle === connection.sourceHandle),
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
    [setEdges],
  )

  const onDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault()
      const type = e.dataTransfer.getData('application/tel-node-type') as TelephonyNodeType
      if (!type) return
      const position = screenToFlowPosition({ x: e.clientX, y: e.clientY })
      const id = `${type}_${++idCounter.current}`
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
            onKeyDown={(e) => {
              if (e.key === 'Delete') {
                if (selectedNodeId) onDeleteNode(selectedNodeId)
                setEdges((eds) => eds.filter((ed) => !ed.selected))
              }
            }}
            fitView
          >
            <Background color="#374151" gap={20} />
            <Controls />
            <MiniMap
              nodeColor={(n) => TELEPHONY_NODE_META[n.type as TelephonyNodeType]?.color ?? '#374151'}
              style={{ background: '#1f2937' }}
            />
          </ReactFlow>
        </div>

        {selectedNode && (
          <TelephonyNodePropertiesPanel
            node={selectedNode}
            entryNodeId={entryNodeId}
            onChange={onNodeChange}
            onSetEntry={onSetEntry}
            onDelete={onDeleteNode}
          />
        )}
      </div>
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
