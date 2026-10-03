import { useEffect, useState } from 'react'
import { api } from '../../api/client'

interface AiContext {
  text: string
  redactions: Record<string, number>
  scriptSteps: number
  characters: number
  estimatedTokens: number
}

interface AiSuggestion {
  summaryId: string
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
  recordedDisposition: string | null
}

interface Confirmed {
  id: string
  summary: string
  reasonForCall: string
  outcome: string
  disposition: string | null
  followUp: string | null
  edited: boolean
  reviewedByName: string | null
  reviewedAt: string
  ai: { aiSummary: string; aiDisposition: string | null; aiConfidence: number; model: string }
}

const OUTCOMES: [string, string][] = [
  ['order_placed', 'Order placed'], ['order_failed', 'Order failed'], ['no_sale', 'No sale'],
  ['customer_service', 'Customer service'], ['test_or_junk', 'Test / junk'], ['other', 'Other'],
]
const outcomeLabel = (o: string) => OUTCOMES.find(([k]) => k === o)?.[1] ?? o
const inputCls = 'w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-1.5 text-white text-sm'

/**
 * AI call summary (AI learning track).
 * - "What the AI would see": the exact redacted context.
 * - Generate: the model's suggestion, saved server-side as a suggestion (with model / tokens / cost).
 * - Review (calls.manage): editable fields; when the AI's disposition differs from the recorded one the
 *   reviewer must choose explicitly; Confirm & save or Discard. Nothing the AI writes reaches the call until
 *   a person confirms it. The confirmed summary then shows with who confirmed it and whether it was edited.
 */
export default function AiSummaryPanel({ callId, canManage, onChanged }: { callId: string; canManage: boolean; onChanged?: () => void }) {
  const [ctx, setCtx] = useState<AiContext | null>(null)
  const [open, setOpen] = useState(false)
  const [suggestion, setSuggestion] = useState<AiSuggestion | null>(null)
  const [confirmed, setConfirmed] = useState<Confirmed | null>(null)
  const [stats, setStats] = useState<{ generated: number; totalCostUsd: number } | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Review form
  const [text, setText] = useState('')
  const [reason, setReason] = useState('')
  const [outcome, setOutcome] = useState('other')
  const [disposition, setDisposition] = useState('')
  const [followUp, setFollowUp] = useState('')

  const loadConfirmed = () =>
    api.get<{ confirmed: Confirmed | null; generated: number; totalCostUsd: number }>(`/api/v1/call-review/calls/${callId}/ai/summaries`)
      .then((r) => { setConfirmed(r.confirmed); setStats({ generated: r.generated, totalCostUsd: r.totalCostUsd }) })
      .catch(() => {})
  useEffect(() => { loadConfirmed() }, [callId]) // eslint-disable-line react-hooks/exhaustive-deps

  async function loadContext() {
    setError(null)
    try { setCtx(await api.get<AiContext>(`/api/v1/call-review/calls/${callId}/ai/context`)); setOpen(true) }
    catch (e) { setError(e instanceof Error ? e.message : 'Failed to load.') }
  }

  async function generate() {
    setBusy(true); setError(null)
    try {
      const r = await api.post<AiSuggestion>(`/api/v1/call-review/calls/${callId}/ai/summary`)
      setSuggestion(r)
      setText(r.summary.text)
      setReason(r.summary.reasonForCall)
      setOutcome(r.summary.outcome)
      // When the AI and the recorded disposition disagree, start with NO choice — the reviewer must pick.
      const ai = r.summary.dispositionValid ? r.summary.suggestedDisposition : null
      setDisposition(ai && r.recordedDisposition && ai !== r.recordedDisposition ? '' : (ai ?? r.recordedDisposition ?? ''))
      setFollowUp(r.summary.followUp ?? '')
      loadConfirmed()
    }
    catch (e) { setError(e instanceof Error ? e.message : 'Summary unavailable.') }
    finally { setBusy(false) }
  }

  async function review(action: 'confirm' | 'discard') {
    if (!suggestion) return
    setBusy(true); setError(null)
    try {
      if (action === 'confirm')
        await api.post(`/api/v1/call-review/calls/${callId}/ai/summaries/${suggestion.summaryId}/confirm`,
          { summary: text, reasonForCall: reason, outcome, disposition: disposition || null, followUp: followUp || null })
      else
        await api.post(`/api/v1/call-review/calls/${callId}/ai/summaries/${suggestion.summaryId}/discard`)
      setSuggestion(null)
      await loadConfirmed()
      onChanged?.()
    }
    catch (e) { setError(e instanceof Error ? e.message : 'Failed.') }
    finally { setBusy(false) }
  }

  const s = suggestion?.summary
  const u = suggestion?.usage
  const aiDisp = s?.dispositionValid ? s.suggestedDisposition : null
  const disagree = !!(aiDisp && suggestion?.recordedDisposition && aiDisp !== suggestion.recordedDisposition)
  const redacted = ctx ? Object.entries(ctx.redactions) : []

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl px-4 py-3">
      <div className="flex items-center justify-between gap-3">
        <p className="text-xs font-semibold uppercase tracking-wide text-gray-500">
          AI summary
          {stats && stats.generated > 0 && (
            <span className="normal-case font-normal"> — {stats.generated} generated · ${stats.totalCostUsd.toFixed(4)} total</span>
          )}
        </p>
        <div className="flex items-center gap-4">
          <button onClick={() => (open ? setOpen(false) : loadContext())} className="text-xs text-gray-400 hover:text-gray-200">
            {open ? 'Hide what the AI sees' : 'Show what the AI would see'}
          </button>
          <button onClick={generate} disabled={busy}
            className="px-3 py-1 text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-500 rounded-md disabled:opacity-50">
            {busy && !suggestion ? 'Summarizing…' : suggestion ? 'Regenerate' : confirmed ? 'Generate a new summary' : 'Generate summary'}
          </button>
        </div>
      </div>
      {error && <p className="text-red-400 text-xs mt-2">{error}</p>}

      {/* Confirmed summary on the call */}
      {confirmed && !suggestion && (
        <div className="mt-3 space-y-1.5">
          <p className="text-sm text-gray-100 leading-relaxed">{confirmed.summary}</p>
          <div className="flex flex-wrap gap-x-5 gap-y-1 text-xs text-gray-400">
            <span>Reason: <span className="text-gray-200">{confirmed.reasonForCall || '—'}</span></span>
            <span>Outcome: <span className="text-gray-200">{outcomeLabel(confirmed.outcome)}</span></span>
            <span>Disposition: <span className="text-gray-200">{confirmed.disposition ?? '—'}</span></span>
          </div>
          {confirmed.followUp && <p className="text-xs text-gray-300">Follow-up: {confirmed.followUp}</p>}
          <p className="text-[11px] text-gray-500">
            AI-assisted · confirmed by {confirmed.reviewedByName ?? 'unknown'} {new Date(confirmed.reviewedAt).toLocaleString()} ·{' '}
            {confirmed.edited ? <span className="text-amber-300">edited before saving</span> : 'accepted as suggested'}
            {confirmed.edited && (
              <details className="inline">
                <summary className="inline cursor-pointer text-indigo-400 ml-1">show the AI's original</summary>
                <span className="block mt-1 text-gray-400">{confirmed.ai.aiSummary} (disposition: {confirmed.ai.aiDisposition ?? '—'})</span>
              </details>
            )}
          </p>
        </div>
      )}

      {/* Suggestion awaiting review */}
      {s && u && suggestion && (
        <div className="mt-3 space-y-3">
          {(suggestion.possibleTestCall || s.isTestCall) && (
            <p className="inline-block text-[11px] font-medium text-amber-200 bg-amber-950/50 border border-amber-800 rounded px-2 py-0.5">
              Possible test call
              <span className="font-normal text-amber-300/80">
                {' — '}{suggestion.possibleTestCall ? 'placeholder answers found in the script' : 'flagged by the AI'}
              </span>
            </p>
          )}
          {!canManage && (
            <>
              <p className="text-sm text-gray-100 leading-relaxed">{s.text}</p>
              <p className="text-xs text-gray-500">Suggestion only — someone with Call Records edit access can confirm it.</p>
            </>
          )}
          {canManage && (
            <div className="space-y-2">
              <label className="block">
                <span className="block text-xs text-gray-400 mb-1">Summary</span>
                <textarea value={text} onChange={(e) => setText(e.target.value)} rows={3} className={inputCls} />
              </label>
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-2">
                <label className="block">
                  <span className="block text-xs text-gray-400 mb-1">Reason for call</span>
                  <input value={reason} onChange={(e) => setReason(e.target.value)} className={inputCls} />
                </label>
                <label className="block">
                  <span className="block text-xs text-gray-400 mb-1">Outcome</span>
                  <select value={outcome} onChange={(e) => setOutcome(e.target.value)} className={inputCls}>
                    {OUTCOMES.map(([k, l]) => <option key={k} value={k}>{l}</option>)}
                  </select>
                </label>
                <label className="block">
                  <span className="block text-xs text-gray-400 mb-1">
                    Disposition <span className="text-gray-500">(AI {Math.round(s.confidence * 100)}% confident)</span>
                  </span>
                  <select value={disposition} onChange={(e) => setDisposition(e.target.value)}
                    className={`${inputCls} ${disagree && !disposition ? 'border-amber-500' : ''}`}>
                    <option value="">{disagree ? 'Choose…' : '— none —'}</option>
                    {suggestion.allowedDispositions.map((d) => (
                      <option key={d} value={d}>
                        {d}{d === aiDisp ? '  ← AI suggests' : ''}{d === suggestion.recordedDisposition ? '  ← recorded' : ''}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
              {disagree && (
                <p className="text-xs text-amber-300">
                  The AI suggests <b>{aiDisp}</b> but the call is recorded as <b>{suggestion.recordedDisposition}</b> — choose which is right.
                  Changing it updates the call's disposition (and recalculates commission).
                </p>
              )}
              {s.confidence < 0.6 && <p className="text-xs text-amber-300">Low confidence — check the call details before confirming.</p>}
              <label className="block">
                <span className="block text-xs text-gray-400 mb-1">Follow-up (optional)</span>
                <input value={followUp} onChange={(e) => setFollowUp(e.target.value)} className={inputCls} />
              </label>
              <div className="flex gap-2">
                <button disabled={busy || !text.trim() || (disagree && !disposition)} onClick={() => review('confirm')}
                  className="px-4 py-1.5 text-sm font-medium text-white bg-emerald-600 hover:bg-emerald-500 rounded-lg disabled:opacity-50">
                  Confirm &amp; save
                </button>
                <button disabled={busy} onClick={() => review('discard')}
                  className="px-4 py-1.5 text-sm text-gray-300 bg-gray-800 hover:bg-gray-700 rounded-lg disabled:opacity-50">
                  Discard
                </button>
              </div>
            </div>
          )}
          <p className="text-[11px] text-gray-500 border-t border-gray-800 pt-2">
            {u.model} · {u.inputTokens.toLocaleString()} tokens in, {u.outputTokens.toLocaleString()} out ·
            ≈ ${u.estimatedCostUsd.toFixed(4)} · {(u.elapsedMs / 1000).toFixed(1)}s
            {u.attempts > 1 ? ` · ${u.attempts} attempts` : ''} · chose from {suggestion.allowedDispositions.length} allowed dispositions
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
