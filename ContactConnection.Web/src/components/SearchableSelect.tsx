import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'

interface Option {
  value: string
  label: string
  sublabel?: string
}

interface Props {
  options: Option[]
  value: string
  onChange: (value: string) => void
  placeholder?: string
  allLabel?: string
  className?: string
}

export default function SearchableSelect({ options, value, onChange, placeholder = 'Select…', allLabel, className = '' }: Props) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [dropdownStyle, setDropdownStyle] = useState<React.CSSProperties>({})
  const buttonRef = useRef<HTMLButtonElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const selected = options.find((o) => o.value === value)

  const filtered = query
    ? options.filter((o) => {
        const q = query.toLowerCase()
        return o.label.toLowerCase().includes(q) || (o.sublabel ?? '').toLowerCase().includes(q)
      })
    : options

  useEffect(() => {
    if (open) {
      setQuery('')
      // Position dropdown under the button using fixed coords
      if (buttonRef.current) {
        const rect = buttonRef.current.getBoundingClientRect()
        setDropdownStyle({
          position: 'fixed',
          top: rect.bottom + 4,
          left: rect.left,
          width: Math.max(rect.width, 220),
          zIndex: 9999,
        })
      }
      setTimeout(() => inputRef.current?.focus(), 0)
    }
  }, [open])

  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (
        buttonRef.current && !buttonRef.current.contains(e.target as Node) &&
        !(e.target as Element).closest('[data-searchable-dropdown]')
      ) {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [])

  function select(val: string) {
    onChange(val)
    setOpen(false)
  }

  const dropdown = open ? (
    <div
      data-searchable-dropdown
      style={dropdownStyle}
      className="bg-gray-800 border border-gray-700 rounded-lg shadow-xl overflow-hidden"
    >
      <div className="p-2 border-b border-gray-700">
        <input
          ref={inputRef}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Escape') setOpen(false)
            if (e.key === 'Enter' && filtered.length === 1) select(filtered[0].value)
          }}
          placeholder="Search…"
          className="w-full bg-gray-900 text-white rounded px-2.5 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
        />
      </div>
      <ul className="max-h-52 overflow-y-auto py-1">
        {allLabel && (
          <li>
            <button
              type="button"
              onClick={() => select('')}
              className={`w-full text-left px-3 py-2 text-sm transition-colors ${!value ? 'bg-indigo-600/30 text-indigo-300' : 'text-gray-300 hover:bg-gray-700'}`}
            >
              {allLabel}
            </button>
          </li>
        )}
        {filtered.length === 0 && (
          <li className="px-3 py-2 text-sm text-gray-500">No results</li>
        )}
        {filtered.map((o) => (
          <li key={o.value}>
            <button
              type="button"
              onClick={() => select(o.value)}
              className={`w-full text-left px-3 py-2 text-sm transition-colors ${o.value === value ? 'bg-indigo-600/30 text-indigo-300' : 'text-gray-300 hover:bg-gray-700'}`}
            >
              {o.label}
              {o.sublabel && (
                <span className="block text-xs text-gray-500 mt-0.5">{o.sublabel}</span>
              )}
            </button>
          </li>
        ))}
      </ul>
    </div>
  ) : null

  return (
    <div className={`relative ${className}`}>
      <button
        ref={buttonRef}
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="w-full flex items-center justify-between gap-2 bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 text-left"
      >
        <span className={selected ? 'text-white' : 'text-gray-500'}>
          {selected ? selected.label : (allLabel ?? placeholder)}
        </span>
        <svg className={`w-3.5 h-3.5 text-gray-500 shrink-0 transition-transform ${open ? 'rotate-180' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {createPortal(dropdown, document.body)}
    </div>
  )
}
