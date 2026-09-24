import { useCallback } from 'react'
import type { Node, Edge, NodeChange, OnNodesChange } from '@xyflow/react'

interface Pt { x: number; y: number }

const EPS = 0.001

// Wraps a React Flow `onNodesChange` handler so that when a group of connected nodes is dragged
// together, any edge whose BOTH endpoints are in that group has its waypoints (EditableEdge's
// `data.waypoints`, stored as absolute canvas coordinates) translated by the same delta — without
// this, a curve's pivot points stay put while the nodes move out from under them, and the user has
// to manually drag every point back into place after every group move.
//
// Deliberately scoped to edges where BOTH endpoints moved by the same delta in this change batch
// (a rigid group translation) — if only one endpoint of an edge is being dragged, there's no
// single correct way to guess how a free-floating waypoint should follow, so those are left alone.
export function useNodesChangeWithWaypoints<TData extends Record<string, unknown>>(
  nodes: Node<TData>[],
  onNodesChange: OnNodesChange<Node<TData>>,
  setEdges: React.Dispatch<React.SetStateAction<Edge[]>>,
): OnNodesChange<Node<TData>> {
  return useCallback(
    (changes: NodeChange<Node<TData>>[]) => {
      const deltas = new Map<string, Pt>()
      for (const ch of changes) {
        if (ch.type === 'position' && ch.position) {
          const prev = nodes.find((n) => n.id === ch.id)
          if (prev) {
            deltas.set(ch.id, { x: ch.position.x - prev.position.x, y: ch.position.y - prev.position.y })
          }
        }
      }

      if (deltas.size > 0) {
        setEdges((eds) =>
          eds.map((e) => {
            const sd = deltas.get(e.source)
            const td = deltas.get(e.target)
            if (!sd || !td || Math.abs(sd.x - td.x) > EPS || Math.abs(sd.y - td.y) > EPS) return e
            const waypoints = (e.data?.waypoints ?? []) as Pt[]
            if (waypoints.length === 0) return e
            return {
              ...e,
              data: { ...e.data, waypoints: waypoints.map((wp) => ({ x: wp.x + sd.x, y: wp.y + sd.y })) },
            }
          }),
        )
      }

      onNodesChange(changes)
    },
    [nodes, onNodesChange, setEdges],
  )
}
