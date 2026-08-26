import { useEffect, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import { listAdminWebhooks, type AdminWebhookSummary } from '../../api/adminApiDefinitions'
import { CANONICAL_WEBHOOK_TYPE_LABELS } from '../../constants/canonicalWebhookTypes'
import WebhookMappingEditor from '../../components/webhooks/WebhookMappingEditor'

const STATUS_COLOR: Record<string, string> = {
  received: 'bg-gray-800 text-gray-300',
  processed: 'bg-emerald-900/50 text-emerald-400',
  duplicate: 'bg-amber-900/50 text-amber-400',
  rejected: 'bg-red-900/50 text-red-400',
  failed: 'bg-red-900/50 text-red-400',
}

// Tenant-wide list of every configured webhook. Each webhook is a standalone resource — not tied
// to any API Definition/Endpoint — that maps an arbitrary inbound payload onto one of a curated
// set of canonical domain objects (Order/OrderLine/CallRecord). See
// API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook support", and
// CanonicalWebhookMappingEvaluator.cs for the mapping/dispatch shape.
export default function AdminWebhooksPage() {
  const [items, setItems] = useState<AdminWebhookSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editorTarget, setEditorTarget] = useState<'new' | string | null>(null)

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
        <div className="mb-6 flex items-start justify-between gap-4">
          <div>
            <h1 className="text-xl font-semibold text-white">Webhooks</h1>
            <p className="text-sm text-gray-400 mt-1">
              Standalone inbound webhooks — each maps an external payload onto a canonical object
              (Order, Order Line, or Call Record) via a visual field mapping, independent of any
              outbound API Definition.
            </p>
          </div>
          <button
            onClick={() => setEditorTarget('new')}
            className="px-3 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg transition-colors shrink-0"
          >
            New Webhook
          </button>
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
            No webhooks configured yet. Click <span className="text-gray-400">New Webhook</span> to
            map an inbound payload onto a canonical object.
          </div>
        ) : (
          <div className="border border-gray-800 rounded-lg overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-gray-900 text-gray-400 text-left">
                  <th className="px-4 py-3 font-medium">Name</th>
                  <th className="px-4 py-3 font-medium">Maps To</th>
                  <th className="px-4 py-3 font-medium">URL</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="px-4 py-3 font-medium">Last Event</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {items.map((item, i) => (
                  <tr
                    key={item.id}
                    className={`border-t border-gray-800 ${i % 2 === 0 ? 'bg-gray-950' : 'bg-gray-900/30'}`}
                  >
                    <td className="px-4 py-3">
                      <div className="text-white">{item.name}</div>
                      {item.description && <div className="text-gray-500 text-xs">{item.description}</div>}
                    </td>
                    <td className="px-4 py-3 text-gray-400 text-xs">
                      {CANONICAL_WEBHOOK_TYPE_LABELS[item.canonicalType] ?? item.canonicalType}
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
                        onClick={() => setEditorTarget(item.id)}
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

      {editorTarget && (
        <WebhookMappingEditor
          webhookId={editorTarget === 'new' ? null : editorTarget}
          onClose={() => { setEditorTarget(null); load() }}
          // Refreshes the list in the background without dismissing the modal — the editor
          // decides for itself when to close (immediately after an edit-save, but only once the
          // admin dismisses it after a create, so the reveal-once secret stays visible).
          onSaved={load}
        />
      )}
    </AdminShell>
  )
}
