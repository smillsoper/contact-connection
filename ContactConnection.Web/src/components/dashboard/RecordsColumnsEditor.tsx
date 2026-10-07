import type { RecordColumn } from '../../api/dashboardWidgets'
import { ChevronDownIcon, ChevronUpIcon, DeleteIcon } from '../icons/Icons'

/** Pick and order the records widget's columns (S181). `available` = the server's catalog plus custom fields. */
function ColumnPicker({ available, value, onChange }: { available: RecordColumn[]; value: string[]; onChange: (v: string[]) => void }) {
  const byKey = new Map(available.map((c) => [c.key, c]))
  const groups = [...new Set(available.map((c) => c.group))]
  const move = (i: number, d: number) => {
    const next = [...value]
    const j = i + d
    if (j < 0 || j >= next.length) return
    ;[next[i], next[j]] = [next[j], next[i]]
    onChange(next)
  }
  return (
    <div className="grid sm:grid-cols-2 gap-3">
      <div className="border border-gray-800 rounded p-2 max-h-72 overflow-y-auto">
        {groups.map((g) => (
          <div key={g} className="mb-2">
            <div className="text-[10px] uppercase tracking-wide text-gray-600 mb-1">{g}</div>
            {available.filter((c) => c.group === g).map((c) => (
              <label key={c.key} className="flex items-center gap-2 text-xs text-gray-300">
                <input type="checkbox" checked={value.includes(c.key)}
                  onChange={(e) => onChange(e.target.checked ? [...value, c.key] : value.filter((k) => k !== c.key))} />
                {c.label}
              </label>
            ))}
          </div>
        ))}
      </div>
      <div className="border border-gray-800 rounded p-2 max-h-72 overflow-y-auto">
        <div className="text-[10px] uppercase tracking-wide text-gray-600 mb-1">Order</div>
        {value.length === 0 && <p className="text-xs text-gray-600">Nothing picked — the default columns are used.</p>}
        {value.map((k, i) => (
          <div key={k} className="flex items-center gap-1 text-xs text-gray-200 py-0.5">
            <span className="flex-1 truncate">{byKey.get(k)?.label ?? k}</span>
            <button className="px-1 text-gray-500 hover:text-white disabled:opacity-30" disabled={i === 0} onClick={() => move(i, -1)} title="Move up"><ChevronUpIcon size={13} /></button>
            <button className="px-1 text-gray-500 hover:text-white disabled:opacity-30" disabled={i === value.length - 1} onClick={() => move(i, 1)} title="Move down"><ChevronDownIcon size={13} /></button>
            <button className="px-1 text-gray-500 hover:text-red-300" onClick={() => onChange(value.filter((x) => x !== k))} title="Remove"><DeleteIcon size={13} /></button>
          </div>
        ))}
      </div>
    </div>
  )
}

export default function RecordsColumnsEditor({ available, columns, pageSize, allowRecordings, onChange }: {
  available: RecordColumn[]
  columns: string[]
  pageSize: number
  allowRecordings: boolean
  onChange: (p: { columns?: string[]; pageSize?: number; allowRecordings?: boolean }) => void
}) {
  return (
    <div className="space-y-4">
      <div>
        <label className="block text-xs text-gray-400 mb-1">Table columns</label>
        <ColumnPicker available={available} value={columns} onChange={(v) => onChange({ columns: v })} />
      </div>
      <div className="flex flex-wrap items-center gap-6">
        <label className="flex items-center gap-2 text-xs text-gray-300">
          Rows per page
          <select value={pageSize} onChange={(e) => onChange({ pageSize: Number(e.target.value) })}
            className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-white">
            {[10, 25, 50, 100].map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </label>
        <label className="flex items-center gap-2 text-xs text-gray-300">
          <input type="checkbox" checked={allowRecordings} onChange={(e) => onChange({ allowRecordings: e.target.checked })} />
          Recording playback in call details
        </label>
      </div>
      <p className="text-[11px] text-gray-500">
        Clicking a call opens its full details — call, customer, addresses, each interaction's order, cart, payments and summary,
        and the values the script captured. On a client dashboard, client users only hear recordings if you've also allowed it for
        them under Admin → Client users. Card data is never shown.
      </p>
    </div>
  )
}
