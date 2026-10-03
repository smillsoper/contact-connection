import { useState } from 'react'
import { api } from '../../api/client'

interface AiContext {
  text: string
  redactions: Record<string, number>
  scriptSteps: number
  characters: number
  estimatedTokens: number
}

/**
 * AI call summary (AI learning track). Step 1: "What the AI would see" — the exact, redacted text an AI
 * summary would send to the model, with what was withheld and the approximate size in tokens. Nothing is
 * sent to any AI yet. Later steps add the summary itself and the agent confirming it.
 */
export default function AiSummaryPanel({ callId }: { callId: string }) {
  const [ctx, setCtx] = useState<AiContext | null>(null)
  const [open, setOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function load() {
    setError(null)
    try { setCtx(await api.get<AiContext>(`/api/v1/call-review/calls/${callId}/ai/context`)); setOpen(true) }
    catch (e) { setError(e instanceof Error ? e.message : 'Failed to load.') }
  }

  const redacted = ctx ? Object.entries(ctx.redactions) : []

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl px-4 py-3">
      <div className="flex items-center justify-between">
        <p className="text-xs font-semibold uppercase tracking-wide text-gray-500">
          AI summary <span className="normal-case font-normal">— preview</span>
        </p>
        <button onClick={() => (open ? setOpen(false) : load())} className="text-xs text-indigo-400 hover:text-indigo-300">
          {open ? 'Hide' : 'Show what the AI would see'}
        </button>
      </div>
      {error && <p className="text-red-400 text-xs mt-2">{error}</p>}
      {open && ctx && (
        <div className="mt-3 space-y-2">
          <div className="flex flex-wrap gap-x-5 gap-y-1 text-xs text-gray-400">
            <span>Script steps: <span className="text-gray-200">{ctx.scriptSteps}</span></span>
            <span>Size: <span className="text-gray-200">{ctx.characters.toLocaleString()} characters ≈ {ctx.estimatedTokens.toLocaleString()} tokens</span></span>
            <span>
              Withheld / redacted:{' '}
              <span className="text-gray-200">
                {redacted.length === 0 ? 'nothing needed' : redacted.map(([k, n]) => `${n} ${k}`).join(', ')}
              </span>
            </span>
          </div>
          <pre className="text-xs text-gray-300 bg-gray-950 border border-gray-800 rounded-lg p-3 max-h-96 overflow-auto whitespace-pre-wrap">{ctx.text}</pre>
          <p className="text-[11px] text-gray-500">
            This is everything the model would receive about this call — it has no other access to the platform. Nothing has been sent.
          </p>
        </div>
      )}
    </div>
  )
}
