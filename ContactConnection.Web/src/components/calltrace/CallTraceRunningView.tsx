import { useEffect, useRef, useState } from 'react'
import type { CallTraceStep, CaptureMode } from '../../api/callTraces'

export interface CallTab {
  callRecordId: string
  steps: CallTraceStep[]
  ended: boolean
}

interface Props {
  calls: CallTab[]
  activeCallRecordId?: string
  onSelectCall: (callRecordId: string) => void
  status: 'running' | 'stopped'
  stopReason?: string
  effectiveCaptureMode?: CaptureMode
  effectiveCaptureValue?: number
  onStop: () => void
  onNewTrace: () => void
}

export default function CallTraceRunningView({
  calls, activeCallRecordId, onSelectCall, status, stopReason,
  effectiveCaptureMode, effectiveCaptureValue, onStop, onNewTrace,
}: Props) {
  const scrollRef = useRef<HTMLDivElement>(null)
  const activeCall = calls.find((c) => c.callRecordId === activeCallRecordId) ?? calls[0]
  const [expandedKeys, setExpandedKeys] = useState<Set<string>>(new Set())

  function toggleExpanded(key: string) {
    setExpandedKeys((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight })
  }, [activeCall?.steps.length])

  const progressLabel = effectiveCaptureMode === 'duration'
    ? `up to ${effectiveCaptureValue} min`
    : `${calls.length}/${effectiveCaptureValue ?? '?'} calls captured`

  return (
    <div className="flex flex-col h-full">
      {/* Status banner — "stopped" only means no NEW calls will be matched; calls
          already captured keep streaming steps until they end. */}
      <div className="px-4 py-2 border-b border-gray-800 flex items-center justify-between shrink-0">
        <span className="text-xs text-gray-400">
          {status === 'running'
            ? `Tracing — ${progressLabel}`
            : `Not accepting new calls — ${describeStopReason(stopReason)}`}
        </span>
        <div className="flex items-center gap-3 shrink-0">
          {status === 'running' && (
            <button onClick={onStop} className="text-xs text-red-400 hover:text-red-300">
              Stop Trace
            </button>
          )}
          <button onClick={onNewTrace} className="text-xs text-indigo-400 hover:text-indigo-300">
            New Trace
          </button>
        </div>
      </div>

      {calls.length === 0 ? (
        <div className="flex-1 flex items-center justify-center text-gray-500 text-sm p-6 text-center">
          Waiting for a matching call to start…
        </div>
      ) : (
        <>
          {/* Tab strip — one tab per matched call */}
          <div className="flex items-center gap-1 px-2 pt-2 border-b border-gray-800 overflow-x-auto shrink-0">
            {calls.map((c, i) => (
              <button
                key={c.callRecordId}
                onClick={() => onSelectCall(c.callRecordId)}
                className={`px-3 py-1.5 text-xs font-medium rounded-t-lg border-b-2 -mb-px whitespace-nowrap transition-colors ${
                  activeCall?.callRecordId === c.callRecordId
                    ? 'border-indigo-500 text-white bg-gray-800/60'
                    : 'border-transparent text-gray-500 hover:text-gray-300'
                }`}
              >
                Call {i + 1}
                {c.ended && <span className="ml-1.5 text-gray-600">●</span>}
              </button>
            ))}
          </div>

          {/* Step timeline for the active call */}
          <div ref={scrollRef} className="flex-1 overflow-y-auto p-3 space-y-2">
            {activeCall?.steps.length === 0 && (
              <p className="text-gray-500 text-xs">No steps yet…</p>
            )}
            {activeCall?.steps.map((step, i) => {
              const key = `${activeCall.callRecordId}:${i}`
              const expanded = expandedKeys.has(key)
              const canExpand = !!step.stateSnapshot
              return (
                <div key={i} className="bg-gray-800/50 border border-gray-800 rounded-lg px-3 py-2">
                  <button
                    type="button"
                    onClick={() => canExpand && toggleExpanded(key)}
                    className={`w-full text-left ${canExpand ? 'cursor-pointer' : 'cursor-default'}`}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="flex items-center gap-1.5">
                        {canExpand && (
                          <svg className={`w-3 h-3 text-gray-500 shrink-0 transition-transform ${expanded ? 'rotate-90' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
                          </svg>
                        )}
                        <span className={`inline-flex items-center text-xs font-medium px-1.5 py-0.5 rounded ${
                          step.engine === 'telephony'
                            ? 'text-violet-300 bg-violet-900/30'
                            : 'text-sky-300 bg-sky-900/30'
                        }`}>
                          {step.nodeType}
                        </span>
                      </span>
                      <span className="text-gray-600 text-xs font-mono">
                        {new Date(step.enteredAt).toLocaleTimeString()}
                      </span>
                    </div>
                    {step.label && <p className="text-gray-200 text-sm mt-1">{step.label}</p>}
                    {step.detail && <p className="text-gray-400 text-xs mt-0.5">{step.detail}</p>}
                    {(step.transitionTaken || step.exitReason || step.nextNodeId) && (
                      <p className="text-gray-500 text-xs mt-1">
                        exit: {step.transitionTaken ?? '—'}
                        {step.nextNodeId && <> → {step.nextNodeId}</>}
                        {step.exitReason && <> ({step.exitReason})</>}
                      </p>
                    )}
                  </button>
                  {expanded && step.stateSnapshot && (
                    <StateSnapshotView json={step.stateSnapshot} />
                  )}
                </div>
              )
            })}
            {activeCall?.ended && (
              <p className="text-gray-600 text-xs italic pt-1">Call ended.</p>
            )}
          </div>
        </>
      )}
    </div>
  )
}

// Renders the per-step state snapshot (flow variables, SIP headers, section state, etc.).
// Several variable values are themselves JSON strings (e.g. an address or phone captured by
// a composite input node) — those get parsed and rendered as nested objects too, rather than
// showing an escaped JSON blob.
function StateSnapshotView({ json }: { json: string }) {
  let parsed: unknown
  try {
    parsed = JSON.parse(json)
  } catch {
    return <pre className="mt-2 pt-2 border-t border-gray-700/50 text-xs text-gray-400 whitespace-pre-wrap break-all">{json}</pre>
  }
  return (
    <div className="mt-2 pt-2 border-t border-gray-700/50">
      <JsonNode value={parsed} />
    </div>
  )
}

function tryParseNestedJson(value: string): unknown {
  const trimmed = value.trim()
  if (!trimmed.startsWith('{') && !trimmed.startsWith('[')) return undefined
  try { return JSON.parse(trimmed) } catch { return undefined }
}

// Flow variables that hold a whole API-call response (e.g. {{flow.myVar}}) are stored twice in
// the same flat vars object: once as a bare key holding the whole thing as a JSON string (which
// renders below as a nested tree), and again as dot-path siblings like "myVar.success",
// "myVar.response.field" (so {{flow.myVar.response.field}} template lookups work). Once the
// bare key's tree is shown, those dot-path siblings are pure noise — drop them.
function filterFlattenedDuplicates(obj: Record<string, unknown>): Record<string, unknown> {
  const jsonObjectKeys = new Set<string>()
  for (const [key, value] of Object.entries(obj)) {
    if (typeof value === 'string' && tryParseNestedJson(value) !== undefined) jsonObjectKeys.add(key)
  }
  if (jsonObjectKeys.size === 0) return obj

  const filtered: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(obj)) {
    const dotIndex = key.indexOf('.')
    if (dotIndex > 0 && jsonObjectKeys.has(key.slice(0, dotIndex))) continue
    filtered[key] = value
  }
  return filtered
}

function JsonNode({ value, depth = 0 }: { value: unknown; depth?: number }) {
  if (value === null || value === undefined) {
    return <span className="text-gray-600 italic">null</span>
  }

  if (typeof value === 'string') {
    const nested = tryParseNestedJson(value)
    if (nested !== undefined) return <JsonNode value={nested} depth={depth} />
    return value === ''
      ? <span className="text-gray-600 italic">(empty)</span>
      : <span className="text-gray-300 break-all">{value}</span>
  }

  if (typeof value === 'boolean' || typeof value === 'number') {
    return <span className="text-amber-300">{String(value)}</span>
  }

  if (Array.isArray(value)) {
    if (value.length === 0) return <span className="text-gray-600 italic">[]</span>
    return (
      <div className="space-y-0.5">
        {value.map((item, i) => (
          <div key={i} className="flex gap-1.5">
            <span className="text-gray-600">–</span>
            <JsonNode value={item} depth={depth + 1} />
          </div>
        ))}
      </div>
    )
  }

  const entries = Object.entries(filterFlattenedDuplicates(value as Record<string, unknown>))
  if (entries.length === 0) return <span className="text-gray-600 italic">{'{}'}</span>
  return (
    <div className={depth > 0 ? 'pl-3 border-l border-gray-800 space-y-1' : 'space-y-1'}>
      {entries.map(([k, v]) => (
        <div key={k} className="text-xs">
          <span className="text-gray-500 font-mono">{k}:</span>{' '}
          {typeof v === 'object' && v !== null
            ? <div className="mt-0.5"><JsonNode value={v} depth={depth + 1} /></div>
            : <JsonNode value={v} depth={depth + 1} />}
        </div>
      ))}
    </div>
  )
}

function describeStopReason(reason?: string): string {
  switch (reason) {
    case 'call-limit-reached': return 'call limit reached'
    case 'duration-elapsed': return 'time limit reached'
    case 'closed by user': return 'stopped manually'
    default: return reason ?? 'unknown reason'
  }
}
