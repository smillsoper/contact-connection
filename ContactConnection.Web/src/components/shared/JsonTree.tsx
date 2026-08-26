import { useState } from 'react'

// Recursive, click-a-leaf-to-copy-a-path JSON tree. Path convention matches the backend
// ResolvePath helpers used throughout this codebase (AddressResponseMappingEvaluator,
// CanonicalWebhookMappingEvaluator, etc.): objects append ".key", arrays append "[i]" — a copied
// path is directly pasteable into any "from"/sourcePath field that uses that same convention.
//
// Originally built inline in ApiDefinitionDetailContent.tsx for the endpoint response-mapping UI
// (paste/capture a sample response, click fields to build a mapping); extracted here so the
// webhook-mapping UI (WebhookMappingEditor) can reuse the exact same tree/picker instead of a
// second copy-pasted implementation.

export interface JsonTreeProps {
  name: string
  value: unknown
  path: string
  depth: number
  copiedPath: string | null
  onCopy: (path: string) => void
}

export default function JsonTree({ name, value, path, depth, copiedPath, onCopy }: JsonTreeProps) {
  const [expanded, setExpanded] = useState(true)
  const isObj = value !== null && typeof value === 'object'

  if (isObj) {
    const isArr = Array.isArray(value)
    const entries: [string, unknown, string][] = isArr
      ? (value as unknown[]).map((v, i) => [`[${i}]`, v, `${path}[${i}]`])
      : Object.entries(value as Record<string, unknown>).map(([k, v]) => [k, v, path ? `${path}.${k}` : k])

    if (depth === 0) {
      return (
        <div className="w-full overflow-hidden">
          {entries.map(([k, v, childPath]) => (
            <JsonTree key={k} name={k} value={v} path={childPath} depth={1} copiedPath={copiedPath} onCopy={onCopy} />
          ))}
        </div>
      )
    }

    return (
      <div className="pl-3 border-l border-gray-800/50 overflow-hidden">
        <div className="flex items-center overflow-hidden group/arr">
          <button
            onClick={() => setExpanded(e => !e)}
            className="flex items-center gap-1 py-0.5 flex-1 min-w-0 text-left hover:bg-gray-800/30 rounded-l px-0.5 overflow-hidden"
          >
            <span className="text-gray-600 text-[10px] w-3 shrink-0">{expanded ? '▾' : '▸'}</span>
            <span className="font-mono text-gray-300 text-xs truncate">{name}</span>
            <span className="text-gray-600 text-[10px] ml-0.5 shrink-0">{isArr ? `[${(value as unknown[]).length}]` : `{}`}</span>
          </button>
          {isArr && path && (
            <button
              onClick={() => onCopy(path)}
              title={`Copy array path: ${path}`}
              className={`shrink-0 px-1 py-0.5 text-[10px] transition-colors ${copiedPath === path ? 'text-emerald-400' : 'text-gray-700 group-hover/arr:text-gray-500 hover:text-gray-300'}`}
            >
              {copiedPath === path ? '✓' : '⎘'}
            </button>
          )}
        </div>
        {expanded && entries.map(([k, v, childPath]) => (
          <JsonTree key={k} name={k} value={v} path={childPath} depth={depth + 1} copiedPath={copiedPath} onCopy={onCopy} />
        ))}
      </div>
    )
  }

  const displayVal = value === null ? 'null' : String(value)
  const valColor = value === null ? 'text-gray-600'
    : typeof value === 'number' ? 'text-sky-400'
    : typeof value === 'boolean' ? 'text-emerald-400'
    : 'text-amber-300'

  return (
    <div className="pl-3 border-l border-gray-800/50 overflow-hidden">
      <button
        onClick={() => onCopy(path)}
        title={`Copy path: ${path}`}
        className="flex items-center gap-1 py-0.5 w-full text-left hover:bg-indigo-900/20 rounded px-0.5 group overflow-hidden"
      >
        <span className="text-gray-600 text-[10px] w-3 shrink-0" />
        <span className="font-mono text-gray-400 text-xs shrink-0 max-w-[45%] truncate">{name}:</span>
        <span className={`font-mono text-xs ml-1 flex-1 min-w-0 truncate ${valColor}`} title={displayVal}>
          {displayVal.length > 22 ? displayVal.slice(0, 22) + '…' : displayVal}
        </span>
        <span className={`text-[10px] shrink-0 ${copiedPath === path ? 'text-emerald-400' : 'text-gray-700 group-hover:text-gray-500'}`}>
          {copiedPath === path ? '✓' : '⎘'}
        </span>
      </button>
    </div>
  )
}
