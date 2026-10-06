import { useEffect, useMemo, useRef, useState } from 'react'
import { customKpisApi, type CustomKpi, type KpiFormatName, type KpiVariable } from '../../api/dashboardWidgets'
import type { DispositionCategory } from '../../api/dispositions'

// Custom KPIs (S181): a category ratio, or an NCalc formula over the KPI variables (calls, orders, revenue, time, every
// category and disposition as a count) — click a variable to insert it, validated as you type. Shown in the KPI widget.

const input = 'w-full bg-gray-900 border border-gray-700 rounded px-2 py-1.5 text-sm text-gray-100 focus:outline-none focus:border-indigo-500'
const label = 'block text-xs text-gray-400 mb-1'
const btn = 'px-3 py-1.5 rounded text-sm disabled:opacity-50'

const FORMATS: { value: KpiFormatName; label: string }[] = [
  { value: 'percent', label: 'Percent (formula returns a fraction, shown ×100)' },
  { value: 'currency', label: 'Currency' },
  { value: 'number', label: 'Number (2 decimals)' },
  { value: 'integer', label: 'Whole number' },
  { value: 'duration', label: 'Duration (seconds → m:ss)' },
]

interface Draft {
  id: string | null; name: string; description: string; kind: 'ratio' | 'formula'
  numerator: string[]; denominator: string[]; formula: string; format: KpiFormatName
}

export default function CustomKpisTab({ categories }: { categories: DispositionCategory[] }) {
  const [kpis, setKpis] = useState<CustomKpi[]>([])
  const [variables, setVariables] = useState<KpiVariable[]>([])
  const [edit, setEdit] = useState<Draft | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [formulaError, setFormulaError] = useState<string | null>(null)
  const [openGroup, setOpenGroup] = useState<string>('Calls')
  const formulaRef = useRef<HTMLTextAreaElement>(null)

  const load = () => customKpisApi.list().then(setKpis).catch((e: Error) => setError(e.message))
  useEffect(() => {
    load()
    customKpisApi.variables().then(setVariables).catch(() => { })
  }, [])

  // Live validation, debounced.
  useEffect(() => {
    if (!edit || edit.kind !== 'formula') { setFormulaError(null); return }
    const t = setTimeout(() => {
      customKpisApi.validate(edit.formula).then((r) => setFormulaError(r.error)).catch(() => { })
    }, 400)
    return () => clearTimeout(t)
  }, [edit?.formula, edit?.kind]) // eslint-disable-line react-hooks/exhaustive-deps

  const groups = useMemo(() => {
    const m = new Map<string, KpiVariable[]>()
    for (const v of variables) m.set(v.group, [...(m.get(v.group) ?? []), v])
    return [...m.entries()]
  }, [variables])

  const name = (id: string) => categories.find((c) => c.id === id)?.name ?? '?'
  const pick = (list: string[], id: string, on: boolean) => on ? [...list, id] : list.filter((x) => x !== id)

  function insert(token: string) {
    if (!edit) return
    const el = formulaRef.current
    const start = el?.selectionStart ?? edit.formula.length
    const end = el?.selectionEnd ?? edit.formula.length
    const next = edit.formula.slice(0, start) + token + edit.formula.slice(end)
    setEdit({ ...edit, formula: next })
    requestAnimationFrame(() => { el?.focus(); el?.setSelectionRange(start + token.length, start + token.length) })
  }

  async function save() {
    if (!edit) return
    setError(null)
    const body = {
      name: edit.name, description: edit.description || null, kind: edit.kind,
      numeratorCategoryIds: edit.kind === 'ratio' ? edit.numerator : [],
      denominatorCategoryIds: edit.kind === 'ratio' ? edit.denominator : [],
      formula: edit.kind === 'formula' ? edit.formula : null, format: edit.format,
    }
    try {
      if (edit.id) await customKpisApi.update(edit.id, body); else await customKpisApi.create(body)
      setEdit(null); load()
    } catch (e) { setError(e instanceof Error ? e.message : 'Save failed.') }
  }

  const describe = (k: CustomKpi) => k.kind === 'formula'
    ? <code className="text-gray-300">{k.formula}</code>
    : <>{k.numeratorCategoryIds.map(name).join(' + ')} ÷ {k.denominatorCategoryIds.length === 0 ? 'all interactions' : k.denominatorCategoryIds.map(name).join(' + ')}</>

  return (
    <>
      <p className="text-xs text-gray-400 mb-3">
        Your own KPIs — a <b className="text-gray-300">category ratio</b> (e.g. Lead captured ÷ all interactions) or a <b className="text-gray-300">formula</b>
        {' '}(e.g. Save rate = <code>Disp_Saved / (Disp_Saved + Disp_Cancelled)</code>). They appear in the dashboard KPI widget's KPI list.
      </p>
      {error && <p className="text-red-400 text-sm mb-3">{error}</p>}
      {edit ? (
        <div className="bg-gray-800/60 border border-gray-700 rounded-lg p-4 mb-4">
          <div className="grid sm:grid-cols-2 gap-3 mb-3">
            <div><label className={label}>Name</label><input className={input} value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} placeholder="e.g. Save rate" /></div>
            <div><label className={label}>Description</label><input className={input} value={edit.description} onChange={(e) => setEdit({ ...edit, description: e.target.value })} /></div>
          </div>
          <div className="flex gap-4 mb-3 text-sm text-gray-300">
            <label className="flex items-center gap-2"><input type="radio" checked={edit.kind === 'ratio'} onChange={() => setEdit({ ...edit, kind: 'ratio' })} /> Category ratio</label>
            <label className="flex items-center gap-2"><input type="radio" checked={edit.kind === 'formula'} onChange={() => setEdit({ ...edit, kind: 'formula' })} /> Formula</label>
          </div>

          {edit.kind === 'ratio' ? (
            <div className="grid sm:grid-cols-2 gap-4">
              <div>
                <label className={label}>Count interactions in…</label>
                {categories.filter((c) => c.isActive).map((c) => (
                  <label key={c.id} className="flex items-center gap-2 text-sm text-gray-300">
                    <input type="checkbox" checked={edit.numerator.includes(c.id)} onChange={(e) => setEdit({ ...edit, numerator: pick(edit.numerator, c.id, e.target.checked) })} /> {c.name}
                  </label>
                ))}
              </div>
              <div>
                <label className={label}>…out of interactions in (none checked = all interactions)</label>
                {categories.filter((c) => c.isActive).map((c) => (
                  <label key={c.id} className="flex items-center gap-2 text-sm text-gray-300">
                    <input type="checkbox" checked={edit.denominator.includes(c.id)} onChange={(e) => setEdit({ ...edit, denominator: pick(edit.denominator, c.id, e.target.checked) })} /> {c.name}
                  </label>
                ))}
              </div>
            </div>
          ) : (
            <div className="grid md:grid-cols-3 gap-4">
              <div className="md:col-span-2">
                <label className={label}>Formula</label>
                <textarea ref={formulaRef} rows={4} className={`${input} font-mono`} value={edit.formula} spellCheck={false}
                  placeholder="Disp_Saved / (Disp_Saved + Disp_Cancelled)" onChange={(e) => setEdit({ ...edit, formula: e.target.value })} />
                {edit.formula.trim() && (formulaError
                  ? <p className="text-xs text-red-400 mt-1">{formulaError}</p>
                  : <p className="text-xs text-emerald-400 mt-1">✓ Valid</p>)}
                <p className="text-[11px] text-gray-500 mt-1">
                  + − × ÷ (<code>* /</code>) and parentheses; functions like <code>Round(x, 2)</code>, <code>Max(a, b)</code>, <code>if(cond, a, b)</code>.
                  Dividing by zero shows "—".
                </p>
                <label className={`${label} mt-3`}>Show as</label>
                <select className={input} value={edit.format} onChange={(e) => setEdit({ ...edit, format: e.target.value as KpiFormatName })}>
                  {FORMATS.map((f) => <option key={f.value} value={f.value}>{f.label}</option>)}
                </select>
              </div>
              <div>
                <label className={label}>Variables (click to insert)</label>
                <div className="border border-gray-700 rounded max-h-72 overflow-y-auto">
                  {groups.map(([group, vars]) => (
                    <div key={group}>
                      <button type="button" className="w-full text-left px-2 py-1 text-xs font-medium text-gray-300 bg-gray-900 border-b border-gray-800"
                        onClick={() => setOpenGroup(openGroup === group ? '' : group)}>
                        {openGroup === group ? '▾' : '▸'} {group} <span className="text-gray-600">({vars.length})</span>
                      </button>
                      {openGroup === group && vars.map((v) => (
                        <button key={v.name} type="button" title={v.label} onClick={() => insert(v.name)}
                          className="block w-full text-left px-2 py-0.5 text-[11px] hover:bg-gray-800">
                          <code className="text-sky-300">{v.name}</code>
                          <span className="block text-gray-500 truncate">{v.label}</span>
                        </button>
                      ))}
                    </div>
                  ))}
                </div>
              </div>
            </div>
          )}

          <div className="flex gap-2 mt-3">
            <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white`}
              disabled={!edit.name.trim() || (edit.kind === 'ratio' ? edit.numerator.length === 0 : !edit.formula.trim() || !!formulaError)}
              onClick={save}>Save</button>
            <button className={`${btn} bg-gray-700 hover:bg-gray-600 text-white`} onClick={() => setEdit(null)}>Cancel</button>
          </div>
        </div>
      ) : (
        <button className={`${btn} bg-indigo-600 hover:bg-indigo-500 text-white mb-4`}
          onClick={() => setEdit({ id: null, name: '', description: '', kind: 'ratio', numerator: [], denominator: [], formula: '', format: 'percent' })}>New KPI</button>
      )}
      {kpis.length === 0 ? <p className="text-gray-500 italic text-sm">No custom KPIs yet.</p> : (
        <table className="w-full text-sm">
          <tbody>
            {kpis.map((k) => (
              <tr key={k.id} className={`border-b border-gray-800 ${k.isActive ? '' : 'opacity-50'}`}>
                <td className="py-2 pr-3 text-gray-100">{k.name}{k.description && <span className="block text-xs text-gray-500">{k.description}</span>}</td>
                <td className="py-2 pr-3 text-xs text-gray-400">{describe(k)}<span className="ml-2 text-gray-600">{k.kind === 'formula' ? k.format : 'percent'}</span></td>
                <td className="py-2 text-right whitespace-nowrap text-sm">
                  <button className="text-indigo-400 hover:text-indigo-300 mr-3"
                    onClick={() => setEdit({ id: k.id, name: k.name, description: k.description ?? '', kind: k.kind, numerator: k.numeratorCategoryIds,
                      denominator: k.denominatorCategoryIds, formula: k.formula ?? '', format: k.format })}>Edit</button>
                  <button className="text-gray-400 hover:text-white mr-3" onClick={() => customKpisApi.update(k.id, {
                    name: k.name, description: k.description, kind: k.kind, numeratorCategoryIds: k.numeratorCategoryIds,
                    denominatorCategoryIds: k.denominatorCategoryIds, formula: k.formula, format: k.format, isActive: !k.isActive,
                  }).then(load).catch((e: Error) => setError(e.message))}>{k.isActive ? 'Hide' : 'Show'}</button>
                  <button className="text-red-400 hover:text-red-300" onClick={() => customKpisApi.remove(k.id).then(load).catch((e: Error) => setError(e.message))}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  )
}
