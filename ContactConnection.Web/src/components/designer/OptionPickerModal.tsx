import { useState } from 'react'

interface OptionPickerModalProps {
  options: string[]
  // option -> id of the node it's currently wired to (for the "re-wire" badge)
  wiredMap: Map<string, string>
  formatLabel?: (option: string) => string
  onConfirm: (selected: string[]) => void
  onCancel: () => void
}

// "Which option(s) lead here?" modal shown when the user drags a connection off a node whose
// exit branches all funnel through one physical handle. Lets the user check several branches at
// once — each gets wired to the same target in a single confirm, and any branch that's already
// wired elsewhere gets moved (re-wired) rather than duplicated. Shared by both the CRM and
// Telephony designers so every multi-exit node type gets identical picker behavior.
export default function OptionPickerModal({ options, wiredMap, formatLabel, onConfirm, onCancel }: OptionPickerModalProps) {
  const [selected, setSelected] = useState<Set<string>>(new Set())

  function toggle(opt: string) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(opt)) next.delete(opt)
      else next.add(opt)
      return next
    })
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm cursor-default"
      style={{ pointerEvents: 'all' }}
    >
      <div className="bg-gray-900 border border-gray-700 rounded-xl shadow-2xl w-80 p-5 flex flex-col gap-4">
        <div>
          <p className="text-sm font-semibold text-white">Which option(s) lead here?</p>
          <p className="text-xs text-gray-500 mt-0.5">
            Pick one or more. An already-wired option moves its existing connection here.
          </p>
        </div>
        <div className="flex flex-col gap-1.5 max-h-80 overflow-y-auto">
          {options.map((opt) => {
            const isWired = wiredMap.has(opt)
            const isChecked = selected.has(opt)
            return (
              <button
                key={opt}
                type="button"
                onClick={() => toggle(opt)}
                className={`w-full flex items-center gap-2.5 text-left px-3 py-2 rounded-lg text-sm border transition-colors cursor-pointer
                  ${isChecked
                    ? 'text-white bg-blue-950/50 border-blue-600'
                    : isWired
                      ? 'text-amber-300 bg-amber-950/30 border-amber-800/50 hover:bg-amber-900/40'
                      : 'text-white bg-gray-800 border-gray-700 hover:bg-emerald-900/50 hover:border-emerald-600'
                  }`}
              >
                <span
                  className={`shrink-0 w-4 h-4 rounded border flex items-center justify-center
                    ${isChecked ? 'bg-blue-600 border-blue-500' : 'border-gray-600'}`}
                >
                  {isChecked && (
                    <svg viewBox="0 0 16 16" width="11" height="11" fill="none" stroke="white" strokeWidth="2">
                      <path d="M3 8.5l3 3 7-7" />
                    </svg>
                  )}
                </span>
                <span className="flex-1">{formatLabel ? formatLabel(opt) : opt}</span>
                {isWired && (
                  <span className="text-[10px] text-amber-500 font-medium">↺ re-wire</span>
                )}
              </button>
            )
          })}
        </div>
        <div className="flex items-center justify-between pt-1">
          <button
            type="button"
            onClick={onCancel}
            className="text-xs text-gray-500 hover:text-gray-300 transition-colors cursor-pointer"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={selected.size === 0}
            onClick={() => onConfirm([...selected])}
            className="px-3 py-1.5 text-xs font-medium rounded-lg bg-blue-600 text-white hover:bg-blue-500 disabled:opacity-40 disabled:cursor-not-allowed transition-colors cursor-pointer"
          >
            {selected.size === 0 ? 'Connect' : `Connect ${selected.size} branch${selected.size === 1 ? '' : 'es'}`}
          </button>
        </div>
      </div>
    </div>
  )
}
