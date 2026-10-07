import { useEffect, useMemo, useState } from 'react'
import { api } from '../../api/client'

interface DnisOption { number: string; label: string | null; campaign: string | null; isActive: boolean }

const fmt = (n: string) => {
  const d = n.replace(/\D/g, '').slice(-10)
  return d.length === 10 ? `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}` : n
}

/**
 * Pick the dialed numbers (DNIS) a widget shows (S181) — none ticked = every number. Lists the tenant's numbers, narrowed to
 * the widget's client / campaign when one is chosen; a number picked earlier but no longer listed stays shown so it can be
 * unticked.
 */
export default function DnisPicker({ clientId, campaignId, value, onChange }: {
  clientId?: string; campaignId?: string; value: string[]; onChange: (v: string[]) => void
}) {
  const [options, setOptions] = useState<DnisOption[]>([])
  const [search, setSearch] = useState('')

  useEffect(() => {
    const q = new URLSearchParams()
    if (campaignId) q.set('campaignId', campaignId)
    else if (clientId) q.set('clientId', clientId)
    api.get<DnisOption[]>(`/api/v1/dashboard-widgets/dnis-options?${q}`).then(setOptions).catch(() => setOptions([]))
  }, [clientId, campaignId])

  const all = useMemo(() => {
    const known = new Set(options.map((o) => o.number))
    return [...options, ...value.filter((v) => !known.has(v)).map((v) => ({ number: v, label: 'not in this scope', campaign: null, isActive: false }))]
  }, [options, value])
  const term = search.trim().toLowerCase()
  const shown = term
    ? all.filter((o) => o.number.includes(term.replace(/\D/g, '') || term) || (o.label ?? '').toLowerCase().includes(term) || (o.campaign ?? '').toLowerCase().includes(term))
    : all

  return (
    <div>
      <div className="flex items-center justify-between mb-1">
        <label className="text-xs text-gray-400">Numbers dialed (DNIS)</label>
        <span className="text-[11px] text-gray-500">
          {value.length === 0 ? 'All numbers' : `${value.length} selected`}
          {value.length > 0 && <button className="ml-2 text-sky-300 hover:text-sky-200" onClick={() => onChange([])}>Clear</button>}
        </span>
      </div>
      {all.length > 6 && (
        <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search number, label or campaign…"
          className="w-full mb-1 bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-white placeholder-gray-500 focus:outline-none focus:border-sky-500" />
      )}
      <div className="border border-gray-800 rounded p-2 max-h-40 overflow-y-auto space-y-0.5">
        {all.length === 0 && <p className="text-xs text-gray-600">No numbers in this scope.</p>}
        {shown.map((o) => (
          <label key={o.number} className="flex items-center gap-2 text-xs text-gray-300">
            <input type="checkbox" checked={value.includes(o.number)}
              onChange={(e) => onChange(e.target.checked ? [...value, o.number] : value.filter((v) => v !== o.number))} />
            <span className="font-mono">{fmt(o.number)}</span>
            {o.label && <span className="text-gray-400 truncate">{o.label}</span>}
            {o.campaign && <span className="text-gray-600 truncate">· {o.campaign}</span>}
            {!o.isActive && <span className="text-gray-600">(inactive)</span>}
          </label>
        ))}
      </div>
    </div>
  )
}
