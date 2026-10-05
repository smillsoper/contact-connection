import { useEffect, useState } from 'react'
import { api } from '../api/client'
import AiSummaryPanel from './admin/AiSummaryPanel'

interface PendingCall {
  /** The interaction the summary is for (S178); null on older rows. */
  interactionId: string | null
  callRecordId: string
  createdAt: string
  callStartedAt: string | null
  campaign: string | null
  callerName: string | null
  callerNumber: string | null
  orderNumber: string | null
  handleTimeSeconds: number | null
}

function fmtPhone(n: string | null) {
  const d = (n ?? '').replace(/\D/g, '').replace(/^1(?=\d{10}$)/, '')
  return d.length === 10 ? `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}` : n
}

/** "10:42 AM · NeuroQ - LF TV · Margaret Smith · (541) 670-4541 · Order #LIFSEA-10000003 · 2m 8s" */
function callLabel(p: PendingCall) {
  const time = new Date(p.callStartedAt ?? p.createdAt).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
  const length = p.handleTimeSeconds != null ? `${Math.floor(p.handleTimeSeconds / 60)}m ${p.handleTimeSeconds % 60}s` : null
  return [time, p.campaign, p.callerName, fmtPhone(p.callerNumber), p.orderNumber ? `Order #${p.orderNumber}` : null, length]
    .filter(Boolean).join(' · ')
}

/**
 * Agent wrap-up (AI step c, S171): AI call summaries waiting for this agent's review. Generated automatically
 * when a call's script finishes (campaigns that opted in); the server pushes "summary ready" and the card
 * appears here. Loaded on start too, so a refresh doesn't lose it. The agent confirms (edits allowed) or
 * discards — nothing reaches the call record until they do. Never blocks taking the next call.
 */
export default function WrapUpSummaries() {
  const [pending, setPending] = useState<PendingCall[]>([])

  const load = () => api.get<PendingCall[]>('/api/v1/ai/summaries/mine/pending').then(setPending).catch(() => {})

  useEffect(() => {
    load()
    const onReady = () => load()
    window.addEventListener('cc:ai-summary-ready', onReady)
    return () => window.removeEventListener('cc:ai-summary-ready', onReady)
  }, [])

  if (pending.length === 0) return null

  return (
    <div className="border-b border-gray-800 bg-gray-950 px-4 py-3 space-y-3 max-h-[60vh] overflow-y-auto">
      {pending.map((p) => (
        <AiSummaryPanel
          key={p.interactionId ?? p.callRecordId}
          callId={p.callRecordId}
          interactionId={p.interactionId}
          canManage
          agentMode
          title={`Wrap-up — ${callLabel(p)}`}
          onChanged={() => setPending((list) => list.filter((x) => x.callRecordId !== p.callRecordId))}
        />
      ))}
    </div>
  )
}
