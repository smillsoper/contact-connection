import { useEffect, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import {
  listAdminWebhooks,
  getAdminWebhook,
  enableAdminWebhook,
  updateAdminWebhook,
  regenerateAdminWebhookSecret,
  regenerateAdminWebhookToken,
  disableAdminWebhook,
  listAdminWebhookEvents,
  type AdminWebhookSummary,
} from '../../api/adminApiDefinitions'
import WebhookConfigPanel from '../../components/versioning/WebhookConfigPanel'

const STATUS_COLOR: Record<string, string> = {
  received: 'bg-gray-800 text-gray-300',
  processed: 'bg-emerald-900/50 text-emerald-400',
  duplicate: 'bg-amber-900/50 text-amber-400',
  rejected: 'bg-red-900/50 text-red-400',
  failed: 'bg-red-900/50 text-red-400',
}

// Tenant-wide list of every configured webhook, across every API Definition/Endpoint — the
// dashboard-linked counterpart to the per-endpoint "Webhook" button in
// ApiDefinitionDetailContent.tsx. A webhook's config (URL/secret/signature settings/events log)
// still lives entirely on the endpoint it's a 1:1 sidecar of; this page only solves "where do I
// find my webhooks" by aggregating them, then reuses the exact same WebhookConfigPanel to manage
// whichever one is selected. See API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook support".
export default function AdminWebhooksPage() {
  const [items, setItems] = useState<AdminWebhookSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selected, setSelected] = useState<AdminWebhookSummary | null>(null)

  async function load() {
    setLoading(true)
    setError(null)
    try {
      setItems(await listAdminWebhooks())
    } catch (e) {
      setError(String(e))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  return (
    <AdminShell>
      <div className="p-6 max-w-5xl mx-auto">
        <div className="mb-6">
          <h1 className="text-xl font-semibold text-white">Webhooks</h1>
          <p className="text-sm text-gray-400 mt-1">
            Every inbound webhook configured across your API Definitions. Each one is tied to a
            specific API Endpoint — payload mapping is configured there, in that endpoint's own
            Response Mapping panel; this page is for finding, monitoring, and managing them.
          </p>
        </div>

        {error && (
          <div className="mb-4 p-3 bg-red-900/40 border border-red-700 text-red-300 rounded text-sm">
            {error}
          </div>
        )}

        {loading ? (
          <div className="text-gray-400 text-sm py-8 text-center">Loading…</div>
        ) : items.length === 0 ? (
          <div className="text-gray-500 text-sm py-12 text-center border border-dashed border-gray-700 rounded-lg">
            No webhooks configured yet. Enable one from an API Endpoint's "Webhook" button in{' '}
            <span className="text-gray-400">API Definitions</span>.
          </div>
        ) : (
          <div className="border border-gray-800 rounded-lg overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-gray-900 text-gray-400 text-left">
                  <th className="px-4 py-3 font-medium">Definition / Endpoint</th>
                  <th className="px-4 py-3 font-medium">URL</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="px-4 py-3 font-medium">Last Event</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {items.map((item, i) => (
                  <tr
                    key={item.webhookEndpointId}
                    className={`border-t border-gray-800 ${i % 2 === 0 ? 'bg-gray-950' : 'bg-gray-900/30'}`}
                  >
                    <td className="px-4 py-3">
                      <div className="text-white">{item.definitionName}</div>
                      <div className="text-gray-500 text-xs font-mono">{item.endpointName} · {item.endpointPath}</div>
                    </td>
                    <td className="px-4 py-3 text-gray-400 font-mono text-xs">{item.url}</td>
                    <td className="px-4 py-3">
                      <span className={`text-xs px-1.5 py-0.5 rounded font-medium ${
                        item.isActive ? 'bg-emerald-900/50 text-emerald-400' : 'bg-gray-800 text-gray-400'
                      }`}>
                        {item.isActive ? 'Active' : 'Disabled'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      {item.lastEventAt ? (
                        <div>
                          <span className={`text-xs px-1.5 py-0.5 rounded font-medium ${STATUS_COLOR[item.lastEventStatus ?? ''] ?? 'bg-gray-800 text-gray-300'}`}>
                            {item.lastEventStatus}
                          </span>
                          <div className="text-gray-500 text-xs mt-1">{new Date(item.lastEventAt).toLocaleString()}</div>
                        </div>
                      ) : (
                        <span className="text-gray-600 text-xs">No events yet</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-right">
                      <button
                        onClick={() => setSelected(item)}
                        className="text-indigo-400 hover:text-indigo-300 text-xs transition-colors"
                      >
                        Configure
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {selected && (
        <WebhookConfigPanel
          endpointName={`${selected.definitionName} — ${selected.endpointName}`}
          getWebhook={() => getAdminWebhook(selected.definitionId, selected.endpointId)}
          enableWebhook={() => enableAdminWebhook(selected.definitionId, selected.endpointId)}
          updateWebhook={(data) => updateAdminWebhook(selected.definitionId, selected.endpointId, data)}
          regenerateSecret={() => regenerateAdminWebhookSecret(selected.definitionId, selected.endpointId)}
          regenerateToken={() => regenerateAdminWebhookToken(selected.definitionId, selected.endpointId)}
          disableWebhook={() => disableAdminWebhook(selected.definitionId, selected.endpointId)}
          listEvents={() => listAdminWebhookEvents(selected.definitionId, selected.endpointId)}
          onClose={() => { setSelected(null); load() }}
        />
      )}
    </AdminShell>
  )
}
