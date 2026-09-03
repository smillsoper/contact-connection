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
import NodePalette from '../components/designer/NodePalette'
import NodePropertiesPanel from '../components/designer/NodePropertiesPanel'
import EditableEdge from '../components/designer/EditableEdge'
import ScriptNode from '../components/designer/nodes/ScriptNode'
import InputNode from '../components/designer/nodes/InputNode'
import EmailNode from '../components/designer/nodes/EmailNode'
import PhoneNode from '../components/designer/nodes/PhoneNode'
import AddressNode from '../components/designer/nodes/AddressNode'
import SectionNode from '../components/designer/nodes/SectionNode'
import ExecuteFlowNode from '../components/designer/nodes/ExecuteFlowNode'
import TransitionToFlowNode from '../components/designer/nodes/TransitionToFlowNode'
import BranchNode from '../components/designer/nodes/BranchNode'
import SetVariableNode from '../components/designer/nodes/SetVariableNode'
import ApiCallNode from '../components/designer/nodes/ApiCallNode'
import ScheduledCallbackNode from '../components/designer/nodes/ScheduledCallbackNode'
import EndNode from '../components/designer/nodes/EndNode'

import type { NodeData, ContactConnectionNodeType, ContactConnectionFlowDefinition, FlowOption } from '../types/designer'
import { defaultNodeData } from '../types/designer'
import VersionHistoryPanel from '../components/versioning/VersionHistoryPanel'

const nodeTypes = {
  script: ScriptNode,
  input: InputNode,
  email: EmailNode,
  phone: PhoneNode,
  address: AddressNode,
  section: SectionNode,
  execute_flow: ExecuteFlowNode,
  transition_to_flow: TransitionToFlowNode,
  branch: BranchNode,
  set_variable: SetVariableNode,
  api_call: ApiCallNode,
  scheduled_callback: ScheduledCallbackNode,
  end: EndNode,
}

const edgeTypes = {
  editable: EditableEdge,
}

// Node types with a fixed (non-user-editable) exit-option list, wired via the same single
// physical handle + picker-modal mechanism as select-type input nodes.
const FIXED_EXIT_OPTIONS: Partial<Record<ContactConnectionNodeType, string[]>> = {
  api_call: ['success', 'error', 'timeout'],
  scheduled_callback: ['scheduled', 'invalid_time', 'failed'],
}

// Options for a node that uses the fixed-handle picker (select-type input, or a
// FIXED_EXIT_OPTIONS node type) — null if the node doesn't use this mechanism at all.
function pickerOptions(node: Node<NodeData> | undefined): string[] | null {
  if (!node) return null
  const fixed = FIXED_EXIT_OPTIONS[node.type as ContactConnectionNodeType]
  if (fixed) return fixed
  if (node.type === 'input' && (node.data.fieldType as string) === 'select') {
    const raw = (node.data.options as string | undefined) ?? ''
    const opts = raw.split(',').map((o) => o.trim()).filter(Boolean)
    return opts.length > 0 ? opts : null
  }
  return null
}

// ── Conversion helpers ──────────────────────────────────────────────────────

function toContactConnectionDef(
  nodes: Node<NodeData>[],
  edges: Edge[],
  entryNodeId: string | null,
  flowName: string,
): ContactConnectionFlowDefinition {
  // Collect waypoints keyed by edge id
  const waypointsMap: Record<string, { x: number; y: number }[]> = {}
  for (const e of edges) {
    const wps = e.data?.waypoints as { x: number; y: number }[] | undefined
    if (wps && wps.length > 0) waypointsMap[e.id] = wps
  }

  const flowNodes: ContactConnectionFlowDefinition['nodes'] = {}
  for (const n of nodes) {
    const outgoing = edges.filter((e) => e.source === n.id)
    const transitions: Record<string, string> = {}
    for (const e of outgoing) {
      // Select-input edges store the option name in data.transition; fall back to sourceHandle
      const key = (e.data?.transition as string | undefined) ?? e.sourceHandle ?? 'default'
      transitions[key] = e.target
    }
    const { isEntry: _entry, options: optionsStr, ...rest } = n.data

    // Convert comma-separated options string → [{value, label}] array for the engine
    const options: FlowOption[] | undefined =
      n.type === 'input' && typeof optionsStr === 'string' && optionsStr.trim()
        ? (optionsStr as string).split(',').map((o) => o.trim()).filter(Boolean).map((o) => ({ value: o, label: o }))
        : undefined

    flowNodes[n.id] = {
      ...rest,
      ...(options ? { options } : {}),
      type: (n.type ?? 'script') as ContactConnectionNodeType,
      label: n.data.label as string,
      _pos: n.position,
      transitions,
    }
  }
  return {
    flow_type: 'crm',
    name: flowName,
    entry_node: entryNodeId ?? nodes[0]?.id ?? '',
    nodes: flowNodes,
    ...(Object.keys(waypointsMap).length > 0 ? { _waypoints: waypointsMap } : {}),
  }
}

// Treat null, undefined, and "default" as the same handle slot for de-dup purposes.
function normHandle(h: string | null | undefined): string | null {
  return !h || h === 'default' ? null : h
}

function fromContactConnectionDef(def: ContactConnectionFlowDefinition): {
  nodes: Node<NodeData>[]
  edges: Edge[]
  entryNodeId: string
} {
  const nodes: Node<NodeData>[] = []
  const edges: Edge[] = []
  let x = 100

  for (const [id, nodeDef] of Object.entries(def.nodes)) {
    const { type, label, _pos, transitions, options: optionsDef, ...rest } = nodeDef

    // Convert [{value, label}] array back to comma-separated string for the designer textarea
    const options: string | undefined = Array.isArray(optionsDef)
      ? (optionsDef as FlowOption[]).map((o) => o.label).join(', ')
      : undefined

    nodes.push({
      id,
      type,
      position: _pos ?? { x, y: 100 },
      data: { label, isEntry: id === def.entry_node, ...(options !== undefined ? { options } : {}), ...rest },
    })
    x += 260

    const isOptionNodeType = (type === 'input' && nodeDef.fieldType === 'select') || FIXED_EXIT_OPTIONS[type] !== undefined
    for (const [handle, targetId] of Object.entries(transitions)) {
      const edgeId = `${id}-${handle}-${targetId}`
      // Option-node edges (select-input, api_call, ...): visual handle = null (→ default), semantic key stored in data.transition
      const isOptionEdge = isOptionNodeType && handle !== 'default'
      edges.push({
        id: edgeId,
        source: id,
        target: targetId,
        sourceHandle: isOptionEdge ? null : (handle === 'default' ? null : handle),
        type: 'editable',
        label: handle !== 'default' ? handle : undefined,
        data: {
          waypoints: def._waypoints?.[edgeId] ?? [],
          ...(isOptionEdge ? { transition: handle } : {}),
        },
      })
    }
  }

  return { nodes, edges, entryNodeId: def.entry_node }
}

// ── Inner canvas (needs useReactFlow) ──────────────────────────────────────

function DesignerCanvas({
  initialFlowId,
  initialFlowName,
}: {
  initialFlowId: string | null
  initialFlowName: string
}) {
  const navigate = useNavigate()
  const { screenToFlowPosition } = useReactFlow()
  const wrapperRef = useRef<HTMLDivElement>(null)

  const [nodes, setNodes, onNodesChange] = useNodesState<Node<NodeData>>([])
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([])
  const [flowName, setFlowName] = useState(initialFlowName)
  const [flowId, setFlowId] = useState<string | null>(initialFlowId)
  const [flowType, setFlowType] = useState<'crm' | 'telephony'>('crm')
  const [flowDirection, setFlowDirection] = useState<'inbound' | 'outbound' | ''>('')
  const [flowSubType, setFlowSubType] = useState<'manual' | 'progressive' | 'predictive' | ''>('')
  const [entryNodeId, setEntryNodeId] = useState<string | null>(null)
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [publishing, setPublishing] = useState(false)
  const [statusMsg, setStatusMsg] = useState('')
  const [showHistory, setShowHistory] = useState(false)

  // Option-picker modal for select-type input nodes
  const [pendingConn, setPendingConn] = useState<Connection | null>(null)

  // Use refs so the keydown handler always sees current state without re-registering
  const nodesRef = useRef(nodes)
  const edgesRef = useRef(edges)
  const clipboardRef = useRef<{ nodes: Node<NodeData>[]; edges: Edge[] } | null>(null)
  useEffect(() => { nodesRef.current = nodes }, [nodes])
  useEffect(() => { edgesRef.current = edges }, [edges])

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      // Skip when typing in any input/textarea/contenteditable
      const tag = (e.target as HTMLElement).tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || (e.target as HTMLElement).isContentEditable) return

      const meta = e.metaKey || e.ctrlKey
      if (!meta) return

      if (e.key === 'c') {
        const selected = nodesRef.current.filter((n) => n.selected)
        if (selected.length === 0) return
        e.preventDefault()
        const selectedIds = new Set(selected.map((n) => n.id))
        clipboardRef.current = {
          nodes: selected,
          edges: edgesRef.current.filter(
            (ed) => selectedIds.has(ed.source) && selectedIds.has(ed.target),
          ),
        }
      }

      if (e.key === 'v') {
        const cb = clipboardRef.current
        if (!cb) return
        e.preventDefault()
        const ts = Date.now()
        const idMap = new Map<string, string>()
        cb.nodes.forEach((n, i) => idMap.set(n.id, `node_${ts}_${i}`))

        const newNodes: Node<NodeData>[] = cb.nodes.map((n) => ({
          ...n,
          id: idMap.get(n.id)!,
          position: { x: n.position.x + 40, y: n.position.y + 40 },
          selected: true,
          data: { ...n.data, isEntry: false },
        }))

        const newEdges: Edge[] = cb.edges.map((ed) => {
          const newSrc = idMap.get(ed.source)!
          const newTgt = idMap.get(ed.target)!
          const key = (ed.data?.transition as string | undefined) ?? ed.sourceHandle ?? 'default'
          return { ...ed, id: `${newSrc}-${key}-${newTgt}`, source: newSrc, target: newTgt }
        })

        setNodes((nds) => [...nds.map((n) => ({ ...n, selected: false })), ...newNodes])
        setEdges((eds) => [...eds, ...newEdges])
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [setNodes, setEdges])  // stable setters — refs handle current values

  // Load an existing flow's current definition into the canvas — used on mount and again after
  // a version-history revert (the revert response carries the newly-active definition).
  const loadFlow = useCallback((id: string) => {
    return flowsApi.getDetail(id).then((detail) => {
      setFlowName(detail.name)
      if (detail.flow_type === 'telephony') setFlowType('telephony')
      if (detail.flow_direction) setFlowDirection(detail.flow_direction as 'inbound' | 'outbound')
      if (detail.flow_sub_type) setFlowSubType(detail.flow_sub_type as 'manual' | 'progressive' | 'predictive')
      try {
        const def: ContactConnectionFlowDefinition = JSON.parse(detail.definition)
        const { nodes: n, edges: e, entryNodeId: entry } = fromContactConnectionDef(def)
        setNodes(n)
        setEdges(e)
        setEntryNodeId(entry)
      } catch {
        // definition not yet set — start with empty canvas
      }
    })
  }, [setNodes, setEdges])

  // Load existing flow
  useEffect(() => {
    if (!initialFlowId) return
    loadFlow(initialFlowId)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialFlowId])

  // When an option is picked in the modal, complete the pending edge.
  // Removes any existing edge for this option first (re-wire case).
  // NOTE: bypasses addEdge() intentionally — all option edges use sourceHandle=null for rendering,
  // so addEdge()'s duplicate check (source+sourceHandle+target+targetHandle) would reject any
  // option beyond the first one that connects to the same target node.
  const onOptionPicked = useCallback((optionValue: string) => {
    if (!pendingConn) return
    const conn = pendingConn
    setPendingConn(null)
    setEdges((eds) => {
      // Remove existing edge for this option (both old sourceHandle format and new data.transition format)
      const withoutOld = eds.filter((e) => {
        if (e.source !== conn.source) return true
        const key = (e.data?.transition as string | undefined) ?? e.sourceHandle
        return key !== optionValue
      })
      const newEdge: Edge = {
        ...conn,
        id: `${conn.source}-${optionValue}-${conn.target}`,
        sourceHandle: null,          // visual: renders from the single default handle
        type: 'editable',
        label: optionValue,
        data: { waypoints: [], transition: optionValue },  // semantic: carries the option name
      }
      return [...withoutOld, newEdge]
    })
  }, [pendingConn, setEdges])

  // Connection — intercept fixed-option nodes (select-type input, api_call, ...) to show option picker
  const onConnect = useCallback(
    (params: Connection) => {
      const sourceNode = (nodes as Node<NodeData>[]).find((n) => n.id === params.source)
      if (pickerOptions(sourceNode) !== null) {
        setPendingConn(params)
        return
      }
      setEdges((eds) =>
        addEdge(
          {
            ...params,
            id: `${params.source}-${params.sourceHandle ?? 'default'}-${params.target}`,
            type: 'editable',
            label: params.sourceHandle && params.sourceHandle !== 'default' ? params.sourceHandle : undefined,
            data: { waypoints: [] },
          } as Edge,
          eds.filter((e) =>
            !(e.source === params.source && normHandle(e.sourceHandle) === normHandle(params.sourceHandle))
          ),
        )
      )
    },
    [nodes, setEdges],
  )

  // Node click → open properties
  const onNodeClick = useCallback((_e: React.MouseEvent, node: Node) => {
    setSelectedNodeId(node.id)
  }, [])

  // Deselect on background click
  const onPaneClick = useCallback(() => {
    setSelectedNodeId(null)
  }, [])

  // Drag-and-drop from palette
  const onDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault()
    e.dataTransfer.dropEffect = 'move'
  }, [])

  const onDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault()
      const type = e.dataTransfer.getData('application/reactflow-node-type') as ContactConnectionNodeType
      if (!type) return

      const position = screenToFlowPosition({ x: e.clientX, y: e.clientY })
      const id = `node_${Date.now()}`
      const isFirst = nodes.length === 0

      const newNode: Node<NodeData> = {
        id,
        type,
        position,
        data: { ...defaultNodeData(type), isEntry: isFirst },
      }

      setNodes((nds) => [...nds, newNode])
      if (isFirst) setEntryNodeId(id)
      setSelectedNodeId(id)
    },
    [screenToFlowPosition, setNodes, nodes.length],
  )

  // Update node data from properties panel
  const updateNodeData = useCallback(
    (id: string, patch: Partial<NodeData>) => {
      setNodes((nds) =>
        nds.map((n) => (n.id === id ? { ...n, data: { ...n.data, ...patch } } : n)),
      )
    },
    [setNodes],
  )

  // Set entry node
  const setEntryNode = useCallback(
    (id: string) => {
      setEntryNodeId(id)
      setNodes((nds) =>
        nds.map((n) => ({ ...n, data: { ...n.data, isEntry: n.id === id } })),
      )
    },
    [setNodes],
  )

  // Delete node + connected edges
  const deleteNode = useCallback(
    (id: string) => {
      setNodes((nds) => nds.filter((n) => n.id !== id))
      setEdges((eds) => eds.filter((e) => e.source !== id && e.target !== id))
      setSelectedNodeId(null)
      if (entryNodeId === id) setEntryNodeId(null)
    },
    [setNodes, setEdges, entryNodeId],
  )

  // Save
  const onSave = async () => {
    setSaving(true)
    setStatusMsg('')
    try {
      const def = toContactConnectionDef(nodes as Node<NodeData>[], edges, entryNodeId, flowName)
      const dir = flowType === 'telephony' && flowDirection ? flowDirection : undefined
      const sub = flowType === 'telephony' && flowSubType   ? flowSubType   : undefined
      if (flowId) {
        await flowsApi.updateDefinition(flowId, flowName, def, dir, sub)
        setStatusMsg('Saved')
      } else {
        const detail = await flowsApi.create(flowName, flowType, def, dir, sub)
        setFlowId(detail.id)
        navigate(`/designer/${detail.id}`, { replace: true })
        setStatusMsg('Saved')
      }
    } catch (err) {
      setStatusMsg(`Error: ${String(err)}`)
    } finally {
      setSaving(false)
      setTimeout(() => setStatusMsg(''), 3000)
    }
  }

  // Publish
  const onPublish = async () => {
    if (!flowId) {
      await onSave()
    }
    if (!flowId) return
    setPublishing(true)
    setStatusMsg('')
    try {
      await flowsApi.publish(flowId)
      setStatusMsg('Published — flow is now active')
    } catch (err) {
      setStatusMsg(`Error: ${String(err)}`)
    } finally {
      setPublishing(false)
      setTimeout(() => setStatusMsg(''), 4000)
    }
  }

  const selectedNode = selectedNodeId ? (nodes as Node<NodeData>[]).find((n) => n.id === selectedNodeId) : null

  return (
    <div className="flex flex-col bg-gray-950" style={{ height: '100vh' }}>
      {/* Top bar */}
      <div className="flex items-stretch bg-gray-900 border-b border-gray-800 shrink-0">
        {/* Logo: flush to top-left, fills full navbar height */}
        <img src="/cc-navbar-dark.svg" alt="Contact Connection" className="shrink-0 block" />

        {/* Remaining content: padded, vertically centred */}
        <div className="flex items-center gap-3 px-4 py-2 flex-1">
          <div className="w-px h-5 bg-gray-700 shrink-0" />
          <button
            className="text-gray-400 hover:text-gray-200 text-sm flex items-center gap-1 transition-colors"
            onClick={() => navigate('/flows')}
          >
            ← Back
          </button>
          <input
            className="flex-1 max-w-xs bg-gray-800 border border-gray-700 rounded px-3 py-1 text-sm font-medium text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
            value={flowName}
            onChange={(e) => setFlowName(e.target.value)}
            placeholder="Flow name"
          />

          {/* Flow type */}
          <select
            value={flowType}
            onChange={(e) => {
              setFlowType(e.target.value as 'crm' | 'telephony')
              setFlowDirection('')
              setFlowSubType('')
            }}
            className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-gray-300 focus:outline-none focus:border-sky-500"
          >
            <option value="crm">CRM</option>
            <option value="telephony">Telephony</option>
          </select>

          {/* Direction — telephony only */}
          {flowType === 'telephony' && (
            <select
              value={flowDirection}
              onChange={(e) => {
                setFlowDirection(e.target.value as 'inbound' | 'outbound' | '')
                setFlowSubType('')
              }}
              className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-gray-300 focus:outline-none focus:border-sky-500"
            >
              <option value="">Direction…</option>
              <option value="inbound">Inbound</option>
              <option value="outbound">Outbound</option>
            </select>
          )}

          {/* Sub-type — outbound telephony only */}
          {flowType === 'telephony' && flowDirection === 'outbound' && (
            <select
              value={flowSubType}
              onChange={(e) => setFlowSubType(e.target.value as 'manual' | 'progressive' | 'predictive' | '')}
              className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-gray-300 focus:outline-none focus:border-sky-500"
            >
              <option value="">Sub-type…</option>
              <option value="manual">Manual</option>
              <option value="progressive">Progressive</option>
              <option value="predictive">Predictive</option>
            </select>
          )}

          {statusMsg && (
            <span className="text-sm text-gray-400 italic">{statusMsg}</span>
          )}
          <div className="ml-auto flex items-center gap-2">
            {flowId && (
              <button
                className="px-4 py-1.5 text-sm border border-gray-700 rounded text-gray-300 hover:bg-gray-800 transition-colors"
                onClick={() => setShowHistory(true)}
              >
                History
              </button>
            )}
            <button
              className="px-4 py-1.5 text-sm border border-gray-700 rounded text-gray-300 hover:bg-gray-800 disabled:opacity-50 transition-colors"
              onClick={onSave}
              disabled={saving}
            >
              {saving ? 'Saving…' : 'Save'}
            </button>
            <button
              className="px-4 py-1.5 text-sm bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50 transition-colors"
              onClick={onPublish}
              disabled={publishing}
            >
              {publishing ? 'Publishing…' : 'Publish'}
            </button>
          </div>
        </div>
      </div>

      {/* 3-panel layout */}
      <div className="flex flex-1 overflow-hidden">
        <NodePalette />

        {/* Canvas */}
        <div ref={wrapperRef} className="flex-1">
          <ReactFlow
            nodes={nodes}
            edges={edges}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            onNodeClick={onNodeClick}
            onPaneClick={onPaneClick}
            onDragOver={onDragOver}
            onDrop={onDrop}
            fitView
            deleteKeyCode="Delete"
            multiSelectionKeyCode={['Shift', 'Meta', 'Control']}
            colorMode="dark"
          >
            <Background />
            <Controls />
            <MiniMap nodeColor={(n) => {
              const meta: Record<string, string> = {
                script: '#3b82f6',
                input: '#10b981',
                email: '#0891b2',
                phone: '#0d9488',
                address: '#f97316',
                section: '#d1d5db',
                execute_flow: '#0369a1',
                transition_to_flow: '#7e22ce',
                branch: '#f59e0b',
                set_variable: '#8b5cf6',
                api_call: '#6366f1',
                scheduled_callback: '#0891b2',
                end: '#ef4444',
              }
              return meta[n.type ?? ''] ?? '#9ca3af'
            }} />
          </ReactFlow>
        </div>

        {/* Properties panel */}
        {selectedNode && (
          <NodePropertiesPanel
            node={selectedNode}
            isEntry={entryNodeId === selectedNode.id}
            onUpdate={updateNodeData}
            onSetEntry={setEntryNode}
            onDelete={deleteNode}
            onClose={() => setSelectedNodeId(null)}
            nodes={nodes as Node<NodeData>[]}
            edges={edges}
            entryNodeId={entryNodeId}
          />
        )}
      </div>

      {/* Option picker modal — shown when connecting from a select-type input node */}
      {pendingConn && (() => {
        const srcNode = (nodes as Node<NodeData>[]).find((n) => n.id === pendingConn.source)
        const options = pickerOptions(srcNode) ?? []
        const isFixedOptions = srcNode ? FIXED_EXIT_OPTIONS[srcNode.type as ContactConnectionNodeType] !== undefined : false
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
          <div
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm cursor-default"
            style={{ pointerEvents: 'all' }}
          >
            <div className="bg-gray-900 border border-gray-700 rounded-xl shadow-2xl w-80 p-5 flex flex-col gap-4">
              <div>
                <p className="text-sm font-semibold text-white">Which option leads here?</p>
                <p className="text-xs text-gray-500 mt-0.5">
                  Picking an already-wired option replaces its existing connection.
                </p>
              </div>
              <div className="flex flex-col gap-1.5">
                {options.map((opt) => {
                  const isWired = wiredMap.has(opt)
                  return (
                    <button
                      key={opt}
                      type="button"
                      onClick={() => onOptionPicked(opt)}
                      className={`w-full text-left px-3 py-2 rounded-lg text-sm border transition-colors cursor-pointer
                        ${isWired
                          ? 'text-amber-300 bg-amber-950/30 border-amber-800/50 hover:bg-amber-900/40'
                          : 'text-white bg-gray-800 border-gray-700 hover:bg-emerald-900/50 hover:border-emerald-600'
                        }`}
                    >
                      <span>{isFixedOptions ? opt.charAt(0).toUpperCase() + opt.slice(1) : opt}</span>
                      {isWired && (
                        <span className="ml-2 text-[10px] text-amber-500 font-medium">↺ re-wire</span>
                      )}
                    </button>
                  )
                })}
              </div>
              <button
                type="button"
                onClick={() => setPendingConn(null)}
                className="text-xs text-gray-500 hover:text-gray-300 self-center transition-colors cursor-pointer"
              >
                Cancel
              </button>
            </div>
          </div>
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

// ── Page wrapper (provides ReactFlow context) ───────────────────────────────

export default function FlowDesignerPage() {
  const { id } = useParams<{ id?: string }>()
  return (
    <ReactFlowProvider>
      <DesignerCanvas initialFlowId={id ?? null} initialFlowName="New Flow" />
    </ReactFlowProvider>
  )
}
