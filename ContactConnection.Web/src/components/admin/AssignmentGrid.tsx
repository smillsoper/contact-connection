import { useEffect, useMemo, useState } from 'react'
import { getAssignmentMatrix, bulkAssign, type AssignmentMatrix } from '../../api/telephony'
import { CheckIcon, CloseIcon, DeleteIcon } from '../icons/Icons'

/**
 * Bulk campaign assignment (S182): agents and agent groups down the side, campaigns across the top. Click a cell to assign,
 * change the proficiency or remove; tick rows and campaign columns to apply one change to all of them at once.
 */

type RowKind = 'agent' | 'group'
const key = (kind: RowKind, id: string) => `${kind}:${id}`

export default function AssignmentGrid() {
  const [clientId, setClientId] = useState('')
  const [matrix, setMatrix] = useState<AssignmentMatrix | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [show, setShow] = useState<'both' | 'agents' | 'groups'>('both')
  const [rows, setRows] = useState<Set<string>>(new Set())
  const [cols, setCols] = useState<Set<string>>(new Set())
  const [proficiency, setProficiency] = useState(50)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<{ ok: boolean; text: string } | null>(null)
  const [editing, setEditing] = useState<{ row: string; campaignId: string; value: string } | null>(null)

  function load(c = clientId) {
    return getAssignmentMatrix(c || undefined).then((m) => { setMatrix(m); setError(null) }).catch((e: Error) => setError(e.message))
  }
  useEffect(() => { void load(clientId); setCols(new Set()) }, [clientId]) // eslint-disable-line react-hooks/exhaustive-deps

  const q = search.trim().toLowerCase()
  const agents = useMemo(() => (matrix?.agents ?? []).filter((a) => show !== 'groups'
    && (!q || a.name.toLowerCase().includes(q) || a.email.toLowerCase().includes(q))), [matrix, show, q])
  const groups = useMemo(() => (matrix?.groups ?? []).filter((g) => show !== 'agents'
    && (!q || g.name.toLowerCase().includes(q))), [matrix, show, q])
  const campaigns = matrix?.campaigns ?? []
  const visibleRowKeys = [...groups.map((g) => key('group', g.id)), ...agents.map((a) => key('agent', a.id))]
  const allRows = visibleRowKeys.length > 0 && visibleRowKeys.every((k) => rows.has(k))
  const allCols = campaigns.length > 0 && campaigns.every((c) => cols.has(c.id))

  function flip(set: Set<string>, setter: (s: Set<string>) => void, k: string) {
    const next = new Set(set); if (next.has(k)) next.delete(k); else next.add(k); setter(next)
  }

  async function apply(action: 'assign' | 'set' | 'remove', rowKeys: string[], campaignIds: string[], prof: number | null) {
    setBusy(true); setNotice(null)
    try {
      const agentIds = rowKeys.filter((k) => k.startsWith('agent:')).map((k) => k.slice(6))
      const groupIds = rowKeys.filter((k) => k.startsWith('group:')).map((k) => k.slice(6))
      const r = await bulkAssign({ campaignIds, agentIds, groupIds, action, proficiency: prof })
      const parts = [r.added && `${r.added} assigned`, r.updated && `${r.updated} updated`, r.removed && `${r.removed} removed`].filter(Boolean)
      setNotice({ ok: true, text: parts.length ? parts.join(', ') + '.' : 'Nothing to change.' })
      await load()
    } catch (e) { setNotice({ ok: false, text: e instanceof Error ? e.message : 'Update failed.' }) }
    finally { setBusy(false) }
  }

  async function saveCell() {
    if (!editing) return
    const v = Math.round(Number(editing.value))
    if (!(v >= 1 && v <= 100)) { setNotice({ ok: false, text: 'Proficiency must be 1–100.' }); return }
    await apply('assign', [editing.row], [editing.campaignId], v)
    setEditing(null)
  }

  // A render function, not a component — an inner component would remount (and drop the edit box's focus) every keystroke.
  function renderCell(rowKey: string, campaignId: string, value: number | undefined, sub?: string | null) {
    const isEditing = editing?.row === rowKey && editing.campaignId === campaignId
    const highlighted = rows.has(rowKey) && cols.has(campaignId)
    if (isEditing) {
      return (
        <td key={campaignId} className="px-1 py-1 text-center bg-indigo-950/60">
          <div className="flex items-center justify-center gap-1">
            <input autoFocus type="number" min={1} max={100} value={editing.value}
              onChange={(e) => setEditing({ ...editing, value: e.target.value })}
              onKeyDown={(e) => { if (e.key === 'Enter') void saveCell(); if (e.key === 'Escape') setEditing(null) }}
              className="w-14 bg-gray-800 text-white rounded px-1 py-0.5 text-xs text-center outline-none focus:ring-1 focus:ring-indigo-500" />
            <button disabled={busy} onClick={() => void saveCell()} className="text-emerald-400 hover:text-emerald-300 text-xs" title="Save"><CheckIcon size={13} /></button>
            {value !== undefined && (
              <button disabled={busy} onClick={() => { void apply('remove', [rowKey], [campaignId], null); setEditing(null) }}
                className="text-red-400 hover:text-red-300 text-xs" title="Remove from campaign"><DeleteIcon size={13} /></button>
            )}
            <button onClick={() => setEditing(null)} className="text-gray-500 hover:text-white text-xs" title="Cancel"><CloseIcon size={13} /></button>
          </div>
        </td>
      )
    }
    return (
      <td key={campaignId} className={`px-1 py-1 text-center ${highlighted ? 'bg-indigo-950/40' : ''}`}>
        <button type="button" onClick={() => setEditing({ row: rowKey, campaignId, value: String(value ?? proficiency) })}
          title={value === undefined ? 'Not assigned — click to assign' : `Proficiency ${value} — click to change or remove`}
          className={`w-full rounded px-1 py-1 text-xs ${value === undefined ? 'text-gray-700 hover:text-gray-400 hover:bg-gray-800' : 'bg-emerald-900/40 text-emerald-300 hover:bg-emerald-800/50 font-medium'}`}>
          {value ?? '·'}
          {sub && <span className="block text-[10px] text-amber-300/80 font-normal">{sub}</span>}
        </button>
      </td>
    )
  }

  const btn = 'rounded-lg px-3 py-1.5 text-xs font-medium disabled:opacity-40'
  const canBulk = rows.size > 0 && cols.size > 0

  return (
    <div>
      <p className="text-gray-500 text-sm mb-4">
        Who takes which campaign's calls, and at what proficiency (1–100). Click a cell to assign or change it; tick rows and
        campaign columns to apply one change to all of them.
      </p>

      <div className="flex flex-wrap items-center gap-3 mb-3">
        <select value={clientId} onChange={(e) => setClientId(e.target.value)}
          className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500">
          <option value="">All clients</option>
          {(matrix?.clients ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        <select value={show} onChange={(e) => setShow(e.target.value as typeof show)}
          className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500">
          <option value="both">Groups and agents</option>
          <option value="groups">Groups only</option>
          <option value="agents">Agents only</option>
        </select>
        <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search agents / groups…"
          className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 w-56" />
      </div>

      <div className="sticky top-0 z-20 mb-3 rounded-lg border border-gray-800 bg-gray-950/95 px-4 py-2 flex flex-wrap items-center gap-2">
        <span className="text-sm text-gray-300 mr-1">
          {rows.size} row{rows.size === 1 ? '' : 's'} × {cols.size} campaign{cols.size === 1 ? '' : 's'}
        </span>
        <label className="text-xs text-gray-400 flex items-center gap-1">Proficiency
          <input type="number" min={1} max={100} value={proficiency}
            onChange={(e) => setProficiency(Math.min(100, Math.max(1, Number(e.target.value) || 1)))}
            className="w-16 bg-gray-800 text-white rounded px-2 py-1 text-xs outline-none focus:ring-1 focus:ring-indigo-500" />
        </label>
        <button disabled={busy || !canBulk} onClick={() => void apply('assign', [...rows], [...cols], proficiency)}
          className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white`} title="Assign at this proficiency (updates existing ones too)">Assign</button>
        <button disabled={busy || !canBulk} onClick={() => void apply('set', [...rows], [...cols], proficiency)}
          className={`${btn} bg-gray-800 hover:bg-gray-700 text-gray-200`} title="Change proficiency only where already assigned">Set proficiency</button>
        <button disabled={busy || !canBulk} onClick={() => void apply('remove', [...rows], [...cols], null)}
          className={`${btn} bg-red-900/60 hover:bg-red-800/60 text-red-200`}>Remove</button>
        {(rows.size > 0 || cols.size > 0) && (
          <button onClick={() => { setRows(new Set()); setCols(new Set()) }} className="text-gray-500 hover:text-white text-xs ml-auto">Clear selection</button>
        )}
        {notice && <span className={`basis-full text-xs ${notice.ok ? 'text-emerald-400' : 'text-red-400'}`}>{notice.text}</span>}
      </div>

      {error && <p className="text-red-400 text-sm">{error}</p>}
      {!matrix && !error && <p className="text-gray-400 text-sm">Loading…</p>}
      {matrix && campaigns.length === 0 && <p className="text-gray-500 text-sm">No campaigns for this client.</p>}

      {matrix && campaigns.length > 0 && (
        <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-auto max-h-[70vh]">
          <table className="text-sm border-separate border-spacing-0">
            <thead>
              <tr className="text-gray-400">
                <th className="sticky left-0 top-0 z-10 bg-gray-900 px-3 py-2 text-left border-b border-gray-800 min-w-56">
                  <label className="flex items-center gap-2 text-xs font-medium">
                    <input type="checkbox" checked={allRows}
                      onChange={() => setRows(allRows ? new Set() : new Set(visibleRowKeys))} />
                    All rows
                    <span className="ml-auto flex items-center gap-1 font-normal">
                      <input type="checkbox" checked={allCols}
                        onChange={() => setCols(allCols ? new Set() : new Set(campaigns.map((c) => c.id)))} />
                      All campaigns
                    </span>
                  </label>
                </th>
                {campaigns.map((c) => (
                  <th key={c.id} className={`sticky top-0 bg-gray-900 px-2 py-2 border-b border-gray-800 align-bottom min-w-24 max-w-36 ${cols.has(c.id) ? 'text-indigo-300' : ''}`}>
                    <label className="flex flex-col items-center gap-1 cursor-pointer text-xs font-medium" title={c.clientName ? `${c.clientName} — ${c.name}` : c.name}>
                      {!clientId && c.clientName && <span className="text-[10px] text-gray-500 font-normal truncate max-w-32">{c.clientName}</span>}
                      <span className="truncate max-w-32">{c.name}</span>
                      {c.status !== 'active' && <span className="text-[10px] text-amber-400/80 font-normal">{c.status}</span>}
                      <input type="checkbox" checked={cols.has(c.id)} onChange={() => flip(cols, setCols, c.id)} />
                    </label>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {groups.length > 0 && (
                <tr><td colSpan={campaigns.length + 1} className="sticky left-0 bg-gray-950/60 px-3 py-1 text-[11px] uppercase tracking-wide text-gray-500">Agent groups</td></tr>
              )}
              {groups.map((g) => {
                const k = key('group', g.id)
                return (
                  <tr key={k} className="hover:bg-gray-800/20">
                    <td className={`sticky left-0 z-10 px-3 py-1.5 border-b border-gray-800/60 ${rows.has(k) ? 'bg-indigo-950' : 'bg-gray-900'}`}>
                      <label className="flex items-center gap-2 cursor-pointer">
                        <input type="checkbox" checked={rows.has(k)} onChange={() => flip(rows, setRows, k)} />
                        <span className="text-white">{g.name}</span>
                        <span className="text-[10px] text-violet-300 bg-violet-900/40 rounded px-1">group</span>
                      </label>
                    </td>
                    {campaigns.map((c) => {
                      const cell = g.cells[c.id]
                      return renderCell(k, c.id, cell?.proficiency,
                        cell ? (cell.tierLabel ?? (cell.routingTier > 0 ? `tier ${cell.routingTier}` : null)) : null)
                    })}
                  </tr>
                )
              })}
              {agents.length > 0 && (
                <tr><td colSpan={campaigns.length + 1} className="sticky left-0 bg-gray-950/60 px-3 py-1 text-[11px] uppercase tracking-wide text-gray-500">Agents</td></tr>
              )}
              {agents.map((a) => {
                const k = key('agent', a.id)
                return (
                  <tr key={k} className="hover:bg-gray-800/20">
                    <td className={`sticky left-0 z-10 px-3 py-1.5 border-b border-gray-800/60 ${rows.has(k) ? 'bg-indigo-950' : 'bg-gray-900'}`}>
                      <label className="flex items-center gap-2 cursor-pointer" title={a.email}>
                        <input type="checkbox" checked={rows.has(k)} onChange={() => flip(rows, setRows, k)} />
                        <span className="text-white">{a.name || a.email}</span>
                      </label>
                    </td>
                    {campaigns.map((c) => renderCell(k, c.id, a.cells[c.id]))}
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
