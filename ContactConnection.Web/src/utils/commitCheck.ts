import type { Edge } from '@xyflow/react'

/**
 * Commit Point sanity check: after a commit node (the point of no return), no path may lead back
 * to a node that comes before it — the engine blocks section *jumps* after a commit, but a wired
 * edge back (e.g. a "Change Call Type" option placed after the order was submitted) is part of the
 * script itself, so the designer flags it instead.
 *
 * Returns the offending edges: source is the commit node or anything reachable from it, target is
 * anything that can reach the commit node (its ancestors).
 */
export function findPathsBackBeforeCommit(commitId: string, edges: Edge[]): Edge[] {
  const out = new Map<string, string[]>()
  const into = new Map<string, string[]>()
  for (const e of edges) {
    out.set(e.source, [...(out.get(e.source) ?? []), e.target])
    into.set(e.target, [...(into.get(e.target) ?? []), e.source])
  }
  const walk = (start: string, next: Map<string, string[]>) => {
    const seen = new Set<string>()
    const stack = [...(next.get(start) ?? [])]
    while (stack.length) {
      const n = stack.pop()!
      if (seen.has(n) || n === start) continue
      seen.add(n)
      stack.push(...(next.get(n) ?? []))
    }
    return seen
  }
  const after = walk(commitId, out)
  after.add(commitId)
  const before = walk(commitId, into)
  // A node that is both before and after means a cycle through the commit point itself.
  return edges.filter((e) => after.has(e.source) && before.has(e.target))
}
