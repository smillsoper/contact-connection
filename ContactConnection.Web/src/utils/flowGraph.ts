import type { Node, Edge } from '@xyflow/react'
import type { NodeData } from '../types/designer'

export interface FlowVarToken {
  key: string
  label: string
  /** True when the variable holds a JSON object — show sub-properties, not the raw value. */
  isObject?: boolean
  properties?: { key: string; label: string }[]
}

export interface FlowAncestorVars {
  /** Input nodes that precede this node — each yields {{input.id}} */
  inputs: { id: string; label: string }[]
  /** API call nodes that precede this node — each yields {{api.id.field}} */
  apis: { id: string; label: string }[]
  /** Flow variables visible at this node — from set_variable, email, phone, and input outputVariable */
  flowVars: FlowVarToken[]
}

/**
 * Return ancestors keyed by their minimum distance from nodeId (1 = direct parent).
 * Closest ancestors appear first when sorted ascending — that means the most recently
 * set value of a variable wins when we use first-writer-wins registration.
 */
function ancestorDistances(nodeId: string, edges: Edge[]): Map<string, number> {
  const parents = new Map<string, string[]>()
  for (const e of edges) {
    const list = parents.get(e.target) ?? []
    list.push(e.source)
    parents.set(e.target, list)
  }

  const dist = new Map<string, number>()
  const queue: [string, number][] = [[nodeId, 0]]
  while (queue.length) {
    const [cur, d] = queue.shift()!
    if (dist.has(cur)) continue
    dist.set(cur, d)
    for (const p of parents.get(cur) ?? []) queue.push([p, d + 1])
  }
  dist.delete(nodeId)
  return dist
}

/** Compute the variables that are reachable from ancestor nodes of nodeId. */
export function computeAncestorVars(
  nodeId: string,
  nodes: Node<NodeData>[],
  edges: Edge[],
): FlowAncestorVars {
  const distMap = ancestorDistances(nodeId, edges)
  // Sort closest-first so first-writer-wins === most-recent-value-wins
  const ancestors = nodes
    .filter((n) => distMap.has(n.id))
    .sort((a, b) => distMap.get(a.id)! - distMap.get(b.id)!)

  const inputs: FlowAncestorVars['inputs'] = []
  const apis: FlowAncestorVars['apis'] = []
  const flowVars: FlowVarToken[] = []
  const seen = new Set<string>()

  function addFlat(key: string, label: string) {
    if (!key || seen.has(key)) return
    seen.add(key)
    flowVars.push({ key, label })
  }

  function addObject(key: string, label: string, properties: { key: string; label: string }[]) {
    if (!key || seen.has(key)) return
    seen.add(key)
    flowVars.push({ key, label, isObject: true, properties })
  }

  // Pass 1 — register object-typed node outputs first so set_variable flat entries
  // don't claim the key before the object shape is known (seen set is first-writer-wins).
  for (const n of ancestors) {
    const type = n.type as string
    const data = n.data
    const nodeLabel = (data.label as string) || n.id

    switch (type) {
      case 'email': {
        const outVar = (data.outputVariable as string | undefined)?.trim()
        if (outVar) {
          addObject(outVar, `Email: ${nodeLabel}`, [
            { key: 'value',         label: 'Email address' },
            { key: 'isFormatValid', label: 'Format valid' },
            { key: 'domainExists',  label: 'Domain exists' },
            { key: 'mxExists',      label: 'MX record exists' },
            { key: 'isDisposable',  label: 'Disposable address' },
            { key: 'isDeliverable', label: 'Deliverable' },
          ])
        }
        break
      }
      case 'phone': {
        const outVar = (data.outputVariable as string | undefined)?.trim()
        if (outVar) {
          addObject(outVar, `Phone: ${nodeLabel}`, [
            { key: 'value',         label: 'Digits (unmasked)' },
            { key: 'display_value', label: 'Formatted number' },
            { key: 'isMobile',      label: 'Is mobile' },
            { key: 'isTollFree',    label: 'Is toll-free' },
            { key: 'isInternal',    label: 'Is internal' },
            { key: 'doNotCall',     label: 'Do not call' },
          ])
        }
        break
      }
      case 'address': {
        const outVar = (data.outputVariable as string | undefined)?.trim()
        if (outVar) {
          addObject(outVar, `Address: ${nodeLabel}`, [
            { key: 'firstName',         label: 'First name' },
            { key: 'middleInitial',     label: 'Middle initial' },
            { key: 'lastName',          label: 'Last name' },
            { key: 'company',           label: 'Company' },
            { key: 'address1Prefix',    label: 'Address 1 prefix' },
            { key: 'address1',          label: 'Address line 1' },
            { key: 'address2Prefix',    label: 'Address 2 prefix' },
            { key: 'address2',          label: 'Address line 2' },
            { key: 'formattedAddress1', label: 'Formatted address 1' },
            { key: 'formattedAddress2', label: 'Formatted address 2' },
            { key: 'fullAddress',       label: 'Full address (single line)' },
            { key: 'city',              label: 'City' },
            { key: 'state',             label: 'State' },
            { key: 'zip',               label: 'ZIP code' },
            { key: 'zip4',              label: 'ZIP+4' },
            { key: 'country',           label: 'Country' },
            { key: 'isPOBox',           label: 'Is PO Box' },
            { key: 'isCanada',          label: 'Is Canada' },
            { key: 'isMilitary',        label: 'Is military (APO/FPO)' },
            { key: 'isOutlyingUS',      label: 'Is outlying US territory' },
            { key: 'isForeign',         label: 'Is foreign' },
            { key: 'isAKHI',            label: 'Is Alaska/Hawaii' },
            { key: 'isVerified',        label: 'Is verified' },
          ])
        }
        break
      }
    }
  }

  // Pass 2 — inputs, flat flow vars, set_variable, api_call
  // Object keys already in `seen` from pass 1 will be skipped by addFlat.
  for (const n of ancestors) {
    const type = n.type as string
    const data = n.data
    const nodeLabel = (data.label as string) || n.id

    switch (type) {
      case 'input': {
        inputs.push({ id: n.id, label: nodeLabel })
        const outVar = (data.outputVariable as string | undefined)?.trim()
        if (outVar) addFlat(outVar, `${nodeLabel} → output`)
        break
      }
      case 'set_variable': {
        const assignments = data.assignments as { variable: string; value: string }[] | undefined
        for (const a of assignments ?? []) {
          let key = a.variable.trim()
          if (key.startsWith('{{') && key.endsWith('}}')) key = key.slice(2, -2).trim()
          if (key.startsWith('flow.')) key = key.slice(5)
          // Nested write (flow.obj.prop) — skip; the object is registered by its source node
          if (!key || key.includes('.')) continue
          addFlat(key, `${nodeLabel} → set`)
        }
        break
      }
      case 'api_call': {
        apis.push({ id: n.id, label: nodeLabel })
        break
      }
    }
  }

  return { inputs, apis, flowVars }
}
