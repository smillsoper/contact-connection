import { useEffect, useState } from 'react'
import type { WebhookConfig, WebhookEventRecord } from '../../api/adminApiDefinitions'

interface WebhookConfigPanelProps {
  endpointName: string
  getWebhook: () => Promise<WebhookConfig>
  enableWebhook: () => Promise<WebhookConfig>
  updateWebhook: (data: {
    signatureHeaderName?: string; signatureAlgorithm?: string
    includeTimestamp?: boolean; timestampToleranceSeconds?: number; isActive?: boolean
  }) => Promise<WebhookConfig>
  regenerateSecret: () => Promise<WebhookConfig>
  regenerateToken: () => Promise<WebhookConfig>
  disableWebhook: () => Promise<void>
  listEvents: () => Promise<WebhookEventRecord[]>
  onClose: () => void
}

const STATUS_COLOR: Record<string, string> = {
  received: 'bg-gray-800 text-gray-300',
  processed: 'bg-emerald-900/50 text-emerald-400',
  duplicate: 'bg-amber-900/50 text-amber-400',
  rejected: 'bg-red-900/50 text-red-400',
  failed: 'bg-red-900/50 text-red-400',
}

const ALGORITHMS = ['SHA256', 'SHA512', 'SHA1', 'MD5']

// Enable/configure an inbound webhook for a single API endpoint, and browse its recent receipt
// log. Payload-mapping (which fields to pull out of the vendor's body) is configured via the
// endpoint's existing Response Mapping panel, reused as-is — this panel only owns the parts that
// are specific to being a *receiver*: the URL, the shared secret, signature settings, and the
// events log. See API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook support".
export default function WebhookConfigPanel({
  endpointName, getWebhook, enableWebhook, updateWebhook, regenerateSecret, regenerateToken,
  disableWebhook, listEvents, onClose,
}: WebhookConfigPanelProps) {
  const [webhook, setWebhook] = useState<WebhookConfig | null | undefined>(undefined) // undefined = loading, null = not configured
  const [revealedSecret, setRevealedSecret] = useState<string | null>(null)
  const [events, setEvents] = useState<WebhookEventRecord[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [copied, setCopied] = useState(false)

  const load = () => {
    getWebhook()
      .then((w) => { setWebhook(w); return listEvents() })
      .then(setEvents)
      .catch((e: unknown) => {
        const msg = (e as Error).message
        if (msg.startsWith('404')) setWebhook(null)
        else setError(msg)
      })
  }

  useEffect(load, []) // eslint-disable-line react-hooks/exhaustive-deps

  const run = async (action: () => Promise<WebhookConfig | void>, captureSecret = false) => {
    setBusy(true)
    setError(null)
    try {
      const result = await action()
      if (result) {
        setWebhook(result)
        if (captureSecret && result.secret) setRevealedSecret(result.secret)
      } else {
        setWebhook(null)
        setRevealedSecret(null)
      }
      listEvents().then(setEvents).catch(() => {})
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const fullUrl = webhook ? `https://${webhook.tenantSubdomain}.contactconnection.cc${webhook.path}` : ''

  const copyUrl = () => {
    navigator.clipboard.writeText(fullUrl).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    })
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4" onClick={onClose}>
      <div
        className="bg-gray-900 border border-gray-700 rounded-2xl w-full max-w-xl shadow-2xl flex flex-col max-h-[85vh]"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="px-5 py-4 border-b border-gray-800 flex items-start justify-between shrink-0">
          <div className="min-w-0">
            <h2 className="text-white text-sm font-semibold">Inbound Webhook</h2>
            <p className="text-gray-500 text-xs mt-0.5 truncate">{endpointName}</p>
          </div>
          <button onClick={onClose} className="text-gray-500 hover:text-white text-lg leading-none px-1 shrink-0">×</button>
        </div>

        <div className="flex-1 overflow-y-auto px-5 py-4 space-y-4">
          {error && <p className="text-red-400 text-xs">{error}</p>}

          {webhook === undefined && <p className="text-gray-500 text-sm">Loading…</p>}

          {webhook === null && (
            <div>
              <p className="text-gray-500 text-xs mb-3">
                No webhook configured for this endpoint yet. Enabling one generates a URL and
                secret a vendor can push events to instead of us only calling out.
              </p>
              <button
                disabled={busy}
                onClick={() => run(() => enableWebhook(), true)}
                className="px-3 py-1.5 text-xs font-medium rounded-lg bg-emerald-700 hover:bg-emerald-600 text-white disabled:opacity-50"
              >
                Enable Webhook
              </button>
            </div>
          )}

          {webhook && (
            <>
              {revealedSecret && (
                <div className="bg-amber-950/40 border border-amber-800/60 rounded-lg px-3 py-2.5">
                  <p className="text-amber-400 text-xs font-medium mb-1">
                    Secret — copy this now, it won't be shown again
                  </p>
                  <p className="text-gray-200 text-xs font-mono break-all">{revealedSecret}</p>
                </div>
              )}

              <div>
                <label className="text-gray-500 text-xs">Webhook URL</label>
                <div className="flex items-center gap-2 mt-1">
                  <input readOnly value={fullUrl} className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-300 font-mono" />
                  <button onClick={copyUrl} className="px-2.5 py-1.5 text-xs rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 shrink-0">
                    {copied ? 'Copied' : 'Copy'}
                  </button>
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-gray-500 text-xs">Signature Header</label>
                  <input
                    defaultValue={webhook.signatureHeaderName}
                    onBlur={(e) => run(() => updateWebhook({ signatureHeaderName: e.target.value }))}
                    className="w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200"
                  />
                </div>
                <div>
                  <label className="text-gray-500 text-xs">Algorithm</label>
                  <select
                    value={webhook.signatureAlgorithm}
                    onChange={(e) => run(() => updateWebhook({ signatureAlgorithm: e.target.value }))}
                    className="w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200"
                  >
                    {ALGORITHMS.map((a) => <option key={a} value={a}>{a}</option>)}
                  </select>
                </div>
              </div>

              <label className="flex items-center gap-2 text-xs text-gray-300">
                <input
                  type="checkbox"
                  checked={webhook.includeTimestamp}
                  onChange={(e) => run(() => updateWebhook({ includeTimestamp: e.target.checked }))}
                />
                Timestamped signature (t=…,v1=…) — enables replay-window rejection
              </label>

              {webhook.includeTimestamp && (
                <div>
                  <label className="text-gray-500 text-xs">Tolerance (seconds)</label>
                  <input
                    type="number"
                    defaultValue={webhook.timestampToleranceSeconds}
                    onBlur={(e) => run(() => updateWebhook({ timestampToleranceSeconds: Number(e.target.value) }))}
                    className="w-full mt-1 bg-gray-800 border border-gray-700 rounded-lg px-2.5 py-1.5 text-xs text-gray-200"
                  />
                </div>
              )}

              <label className="flex items-center gap-2 text-xs text-gray-300">
                <input
                  type="checkbox"
                  checked={webhook.isActive}
                  onChange={(e) => run(() => updateWebhook({ isActive: e.target.checked }))}
                />
                Active
              </label>

              <p className="text-gray-600 text-[11px]">
                Payload mapping (which fields drive fulfillment updates) is configured in this
                endpoint's Response Mapping panel — name outcomes "shipped" or "delivered" mapping
                to "orderLineId"/"trackingNumber" to update order fulfillment automatically.
              </p>

              <div className="flex flex-wrap gap-2 pt-1">
                <button disabled={busy} onClick={() => run(() => regenerateSecret(), true)}
                  className="px-2.5 py-1.5 text-xs rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 disabled:opacity-50">
                  Regenerate Secret
                </button>
                <button disabled={busy} onClick={() => run(() => regenerateToken())}
                  className="px-2.5 py-1.5 text-xs rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 disabled:opacity-50">
                  Regenerate URL
                </button>
                <button disabled={busy} onClick={() => run(() => disableWebhook())}
                  className="px-2.5 py-1.5 text-xs rounded-lg bg-red-900/50 hover:bg-red-900 text-red-300 disabled:opacity-50">
                  Disable
                </button>
              </div>

              <div className="pt-2 border-t border-gray-800">
                <h3 className="text-gray-400 text-xs font-medium mb-2">Recent Events</h3>
                {events === null && <p className="text-gray-500 text-xs">Loading…</p>}
                {events !== null && events.length === 0 && <p className="text-gray-500 text-xs">No events received yet.</p>}
                {events !== null && events.length > 0 && (
                  <div className="space-y-1.5">
                    {events.map((e) => (
                      <div key={e.id} className="bg-gray-800/40 border border-gray-700/50 rounded-lg px-3 py-2 flex items-center justify-between gap-2">
                        <div className="min-w-0">
                          <div className="flex items-center gap-2">
                            <span className={`text-[11px] px-1.5 py-0.5 rounded font-medium shrink-0 ${STATUS_COLOR[e.processingStatus] ?? 'bg-gray-800 text-gray-300'}`}>
                              {e.processingStatus}
                            </span>
                            {e.outcomeKey && <span className="text-gray-400 text-[11px] truncate">{e.outcomeKey}</span>}
                            {!e.signatureValid && <span className="text-red-400 text-[11px]">bad signature</span>}
                          </div>
                          {e.processingError && <p className="text-red-400/80 text-[11px] mt-0.5 truncate">{e.processingError}</p>}
                        </div>
                        <span className="text-gray-600 text-[11px] shrink-0">{new Date(e.receivedAt).toLocaleString()}</span>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
