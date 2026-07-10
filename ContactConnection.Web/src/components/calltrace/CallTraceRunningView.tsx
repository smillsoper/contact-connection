import { useEffect, useRef } from 'react'
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
}

export default function CallTraceRunningView({
  calls, activeCallRecordId, onSelectCall, status, stopReason,
  effectiveCaptureMode, effectiveCaptureValue, onStop,
}: Props) {
  const scrollRef = useRef<HTMLDivElement>(null)
  const activeCall = calls.find((c) => c.callRecordId === activeCallRecordId) ?? calls[0]

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
        {status === 'running' && (
          <button onClick={onStop} className="text-xs text-red-400 hover:text-red-300">
            Stop Trace
          </button>
        )}
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
            {activeCall?.steps.map((step, i) => (
              <div key={i} className="bg-gray-800/50 border border-gray-800 rounded-lg px-3 py-2">
                <div className="flex items-center justify-between gap-2">
                  <span className={`inline-flex items-center text-xs font-medium px-1.5 py-0.5 rounded ${
                    step.engine === 'telephony'
                      ? 'text-violet-300 bg-violet-900/30'
                      : 'text-sky-300 bg-sky-900/30'
                  }`}>
                    {step.nodeType}
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
              </div>
            ))}
            {activeCall?.ended && (
              <p className="text-gray-600 text-xs italic pt-1">Call ended.</p>
            )}
          </div>
        </>
      )}
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
