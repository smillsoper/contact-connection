import { useState } from 'react'
import { api } from '../../api/client'

interface AiContext {
  text: string
  redactions: Record<string, number>
  scriptSteps: number
  characters: number
  estimatedTokens: number
}

interface AiSummaryResult {
  summary: {
    text: string
    reasonForCall: string
    outcome: string
    suggestedDisposition: string | null
    dispositionValid: boolean
    confidence: number
    followUp: string | null
    isTestCall: boolean
  }
  usage: { model: string; inputTokens: number; outputTokens: number; estimatedCostUsd: number; elapsedMs: number; attempts: number }
  allowedDispositions: string[]
  possibleTestCall: boolean
}

const OUTCOME_LABELS: Record<string, string> = {
  order_placed: 'Order placed', order_failed: 'Order failed', no_sale: 'No sale',
  customer_service: 'Customer service', test_or_junk: 'Test / junk', other: 'Other',
}

/**
 * AI call summary (AI learning track).
 * Step 1 — "What the AI would see": the exact, redacted context, with what was withheld and its size.
 * Step 2 — "Generate summary": the model's suggestion (summary, reason, outcome, disposition, confidence,
 * follow-up) plus the real token usage and cost. Suggest-only: nothing is saved yet (step 3 adds the
 * agent confirming it).
 */
export default function AiSummaryPanel({ callId }: { callId: string }) {
  const [ctx, setCtx] = useState<AiContext | null>(null)
  const [open, setOpen] = useState(false)
  const [result, setResult] = useState<AiSummaryResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function load() {
    setError(null)
    try { setCtx(await api.get<AiContext>(`/api/v1/call-review/calls/${callId}/ai/context`)); setOpen(true) }
    catch (e) { setError(e instanceof Error ? e.message : 'Failed to load.') }
  }

  async function generate() {
    setBusy(true); setError(null)
    try { setResult(await api.post<AiSummaryResult>(`/api/v1/call-review/calls/${callId}/ai/summary`)) }
    catch (e) { setError(e instanceof Error ? e.message : 'Summary unavailable.') }
    finally { setBusy(false) }
  }

  const redacted = ctx ? Object.entries(ctx.redactions) : []
  const s = result?.summary
  const u = result?.usage

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl px-4 py-3">
      <div className="flex items-center justify-between gap-3">
        <p className="text-xs font-semibold uppercase tracking-wide text-gray-500">
          AI summary <span className="normal-case font-normal">— suggestion only, nothing is saved</span>
        </p>
        <div className="flex items-center gap-4">
          <button onClick={() => (open ? setOpen(false) : load())} className="text-xs text-gray-400 hover:text-gray-200">
            {open ? 'Hide what the AI sees' : 'Show what the AI would see'}
          </button>
          <button onClick={generate} disabled={busy}
            className="px-3 py-1 text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-500 rounded-md disabled:opacity-50">
            {busy ? 'Summarizing…' : result ? 'Regenerate' : 'Generate summary'}
          </button>
        </div>
      </div>
      {error && <p className="text-red-400 text-xs mt-2">{error}</p>}

      {s && u && (
        <div className="mt-3 space-y-2">
          {(result.possibleTestCall || s.isTestCall) && (
            <p className="inline-block text-[11px] font-medium text-amber-200 bg-amber-950/50 border border-amber-800 rounded px-2 py-0.5">
              Possible test call
              <span className="font-normal text-amber-300/80">
                {' — '}{result.possibleTestCall ? 'placeholder answers found in the script' : 'flagged by the AI'}
              </span>
            </p>
          )}
          <p className="text-sm text-gray-100 leading-relaxed">{s.text}</p>
          <div className="flex flex-wrap gap-x-5 gap-y-1 text-xs text-gray-400">
            <span>Reason: <span className="text-gray-200">{s.reasonForCall || '—'}</span></span>
            <span>Outcome: <span className="text-gray-200">{OUTCOME_LABELS[s.outcome] ?? s.outcome}</span></span>
            <span>
              Suggested disposition:{' '}
              <span className={s.dispositionValid ? 'text-emerald-300' : 'text-amber-300'}>{s.suggestedDisposition ?? '—'}</span>
              {!s.dispositionValid && s.suggestedDisposition && <span className="text-amber-400"> (not an allowed value — ignored)</span>}
            </span>
            <span>Confidence: <span className={s.confidence < 0.6 ? 'text-amber-300' : 'text-gray-200'}>{Math.round(s.confidence * 100)}%</span></span>
          </div>
          {s.followUp && <p className="text-xs text-gray-300">Follow-up: {s.followUp}</p>}
          <p className="text-[11px] text-gray-500 border-t border-gray-800 pt-2">
            {u.model} · {u.inputTokens.toLocaleString()} tokens in, {u.outputTokens.toLocaleString()} out ·
            ≈ ${u.estimatedCostUsd.toFixed(4)} · {(u.elapsedMs / 1000).toFixed(1)}s
            {u.attempts > 1 ? ` · ${u.attempts} attempts` : ''} · chose from {result.allowedDispositions.length} allowed dispositions
          </p>
        </div>
      )}

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
            This is everything the model receives about this call (plus the standing instructions) — it has no other access to the platform.
          </p>
        </div>
      )}
    </div>
  )
}
