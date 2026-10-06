import { useEffect, useState } from 'react'
import type { Format } from './widgets/KpiWidget'

/** Targets are stored in the KPI's display units (percent points, dollars, hours, seconds for times). */
function toText(v: number | null | undefined, f: Format) {
  if (v == null) return ''
  if (f === 'secs') {
    const s = Math.round(v)
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
  }
  return String(v)
}

/** Times accept "m:ss" (2:30) or a plain number of minutes (2.5 = 2:30). Returns undefined when unreadable. */
function parse(text: string, f: Format): number | null | undefined {
  const t = text.trim().replace(/[$%,h\s]/gi, '')
  if (t === '') return null
  if (f === 'secs') {
    const m = /^(\d+):([0-5]?\d)$/.exec(t)
    if (m) return Number(m[1]) * 60 + Number(m[2])
    const n = Number(t)
    return Number.isFinite(n) && n >= 0 ? Math.round(n * 60) : undefined
  }
  const n = Number(t)
  return Number.isFinite(n) ? n : undefined
}

const PREFIX: Partial<Record<Format, string>> = { money: '$' }
const SUFFIX: Partial<Record<Format, string>> = { pct: '%', hours: 'h', secs: 'min' }
export const TARGET_PLACEHOLDER: Record<Format, string> = { pct: '25', int: '10', money: '4.50', num: '1.5', hours: '8', secs: '3:00' }

export default function KpiTargetInput({ value, format, placeholder, disabled, onChange }: {
  value: number | null | undefined
  format: Format
  placeholder: string
  disabled?: boolean
  onChange: (v: number | null) => void
}) {
  const [text, setText] = useState(toText(value, format))
  const [bad, setBad] = useState(false)
  useEffect(() => { setText(toText(value, format)); setBad(false) }, [value, format])

  function commit() {
    const v = parse(text, format)
    if (v === undefined) { setBad(true); return }
    setBad(false)
    onChange(v)
    setText(toText(v, format))
  }

  return (
    <div className={`flex items-center bg-gray-800 border rounded px-1.5 ${bad ? 'border-red-500' : 'border-gray-700'} ${disabled ? 'opacity-40' : ''}`}
      title={format === 'secs' ? 'Minutes:seconds, e.g. 2:30 — or plain minutes, e.g. 2.5' : undefined}>
      {PREFIX[format] && <span className="text-gray-500 text-[11px]">{PREFIX[format]}</span>}
      <input type="text" inputMode="decimal" value={text} disabled={disabled}
        placeholder={`${placeholder} ${TARGET_PLACEHOLDER[format]}`}
        onChange={(e) => setText(e.target.value)} onBlur={commit} onKeyDown={(e) => { if (e.key === 'Enter') commit() }}
        className="w-full min-w-0 bg-transparent py-0.5 px-0.5 text-[11px] text-white placeholder-gray-600 focus:outline-none" />
      {SUFFIX[format] && <span className="text-gray-500 text-[11px]">{SUFFIX[format]}</span>}
    </div>
  )
}
