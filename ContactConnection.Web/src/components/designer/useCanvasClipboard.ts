import { useEffect, useRef } from 'react'
import type { Node, Edge } from '@xyflow/react'

interface UseCanvasClipboardOptions<TData extends Record<string, unknown> & { isEntry?: boolean }> {
  nodes: Node<TData>[]
  edges: Edge[]
  setNodes: React.Dispatch<React.SetStateAction<Node<TData>[]>>
  setEdges: React.Dispatch<React.SetStateAction<Edge[]>>
  // Formats a pasted edge's new id from its remapped endpoints + its transition key — the two
  // designers use different id schemes (`${source}-${key}-${target}` vs `e-${source}-${target}-${key}`).
  makeEdgeId: (newSource: string, newTarget: string, transitionKey: string) => string
  // Called after a cut removes nodes, so the page can react (e.g. clear a stale entryNodeId,
  // close a properties panel pointed at a now-deleted node).
  onNodesRemoved?: (removedIds: string[]) => void
}

// Ctrl/Cmd+C / X / V for node/edge selections on a React Flow canvas — copy or cut the selected
// nodes (plus any edges wholly inside the selection) into a ref-backed clipboard, and paste them
// back as a new, offset, selected group. Shared by both the CRM and Telephony flow designers so
// cut/copy/paste behaves identically in each (previously CRM-only).
export function useCanvasClipboard<TData extends Record<string, unknown> & { isEntry?: boolean }>({
  nodes,
  edges,
  setNodes,
  setEdges,
  makeEdgeId,
  onNodesRemoved,
}: UseCanvasClipboardOptions<TData>) {
  // Refs so the keydown handler always sees current state without re-registering on every change.
  const nodesRef = useRef(nodes)
  const edgesRef = useRef(edges)
  const clipboardRef = useRef<{ nodes: Node<TData>[]; edges: Edge[] } | null>(null)
  useEffect(() => { nodesRef.current = nodes }, [nodes])
  useEffect(() => { edgesRef.current = edges }, [edges])

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      // Skip when typing in any input/textarea/contenteditable
      const tag = (e.target as HTMLElement).tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || (e.target as HTMLElement).isContentEditable) return

      const meta = e.metaKey || e.ctrlKey
      if (!meta) return

      if (e.key === 'c' || e.key === 'x') {
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

        if (e.key === 'x') {
          setNodes((nds) => nds.filter((n) => !selectedIds.has(n.id)))
          setEdges((eds) => eds.filter((ed) => !selectedIds.has(ed.source) && !selectedIds.has(ed.target)))
          onNodesRemoved?.([...selectedIds])
        }
      }

      if (e.key === 'v') {
        const cb = clipboardRef.current
        if (!cb) return
        e.preventDefault()
        const ts = Date.now()
        const idMap = new Map<string, string>()
        cb.nodes.forEach((n, i) => idMap.set(n.id, `node_${ts}_${i}`))

        const newNodes: Node<TData>[] = cb.nodes.map((n) => ({
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
          return { ...ed, id: makeEdgeId(newSrc, newTgt, key), source: newSrc, target: newTgt }
        })

        setNodes((nds) => [...nds.map((n) => ({ ...n, selected: false })), ...newNodes])
        setEdges((eds) => [...eds, ...newEdges])
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [setNodes, setEdges, makeEdgeId, onNodesRemoved])  // stable setters — refs handle current values
}
