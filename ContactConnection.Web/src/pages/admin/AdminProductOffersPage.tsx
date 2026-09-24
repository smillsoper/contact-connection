import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import { offersApi, type OfferSummary } from '../../api/offers'
import { productsApi, type ProductSearchResult } from '../../api/products'
import { listClients, type Client } from '../../api/telephony'

function scopeLabel(offer: OfferSummary, clients: Client[]): string {
  if (!offer.clientId) return 'Tenant-wide'
  const client = clients.find((c) => c.id === offer.clientId)
  if (!offer.campaignId) return `Client: ${client?.name ?? '(unknown)'}`
  const campaign = client?.campaigns.find((c) => c.id === offer.campaignId)
  return `Campaign: ${campaign?.name ?? '(unknown)'} (${client?.name ?? '(unknown)'})`
}

interface EditorState {
  name: string
  fullPrice: number
  shipping: number
  taxExempt: boolean
  shippingExempt: boolean
  clientId: string
  campaignId: string
}

function OfferEditorModal({
  offer,
  clients,
  onSave,
  onClose,
}: {
  offer: OfferSummary | null // null = new
  clients: Client[]
  onSave: (data: EditorState) => Promise<void>
  onClose: () => void
}) {
  const isEdit = offer !== null

  const [name, setName] = useState(offer?.name ?? '')
  const [fullPrice, setFullPrice] = useState(offer?.fullPrice ?? 0)
  const [shipping, setShipping] = useState(offer?.shipping ?? 0)
  const [taxExempt, setTaxExempt] = useState(offer?.taxExempt ?? false)
  const [shippingExempt, setShippingExempt] = useState(offer?.shippingExempt ?? false)
  const [clientId, setClientId] = useState(offer?.clientId ?? '')
  const [campaignId, setCampaignId] = useState(offer?.campaignId ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const selectedClient = clients.find((c) => c.id === clientId)

  function handleClientChange(next: string) {
    setClientId(next)
    setCampaignId('') // campaign choice is only meaningful under the client that owns it
  }

  async function handleSave() {
    setError(null)
    if (!name.trim()) { setError('Name is required.'); return }
    if (fullPrice <= 0) { setError('Full price must be greater than zero.'); return }

    setSaving(true)
    try {
      await onSave({ name: name.trim(), fullPrice, shipping, taxExempt, shippingExempt, clientId, campaignId })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
      <div className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-lg max-h-[90vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-700">
          <h2 className="text-lg font-semibold text-white">{isEdit ? 'Edit Offer' : 'New Offer'}</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-white text-xl leading-none">&times;</button>
        </div>

        <div className="overflow-y-auto flex-1 px-6 py-4 space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Name</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
              placeholder="e.g. TV Special, Web Offer, Retention Offer"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-1">Full Price</label>
              <input
                type="number" step="0.01"
                value={fullPrice}
                onChange={(e) => setFullPrice(Number(e.target.value) || 0)}
                className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
              />
              <p className="text-xs text-gray-500 mt-1">Priced as a single full payment. Multi-payment plans stay API-only for now.</p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-1">Shipping</label>
              <input
                type="number" step="0.01"
                value={shipping}
                onChange={(e) => setShipping(Number(e.target.value) || 0)}
                className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
              />
            </div>
          </div>

          <div className="flex gap-6">
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={taxExempt} onChange={(e) => setTaxExempt(e.target.checked)} className="accent-indigo-600" />
              <span className="text-sm text-gray-300">Tax exempt</span>
            </label>
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={shippingExempt} onChange={(e) => setShippingExempt(e.target.checked)} className="accent-indigo-600" />
              <span className="text-sm text-gray-300">Shipping exempt</span>
            </label>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Scope</label>
            <div className="grid grid-cols-2 gap-2">
              <select
                value={clientId}
                onChange={(e) => handleClientChange(e.target.value)}
                className="bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
              >
                <option value="">Tenant-wide</option>
                {clients.map((c) => (
                  <option key={c.id} value={c.id}>{c.name}</option>
                ))}
              </select>
              <select
                value={campaignId}
                onChange={(e) => setCampaignId(e.target.value)}
                disabled={!selectedClient}
                className="bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm disabled:opacity-50"
              >
                <option value="">All campaigns</option>
                {selectedClient?.campaigns.map((c) => (
                  <option key={c.id} value={c.id}>{c.name}</option>
                ))}
              </select>
            </div>
            <p className="text-xs text-gray-500 mt-1">Tenant-wide offers are visible everywhere. A campaign-scoped offer needs its client set too.</p>
          </div>

          {error && <p className="text-red-400 text-sm">{error}</p>}
        </div>

        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-700">
          <button onClick={onClose} className="px-4 py-2 text-sm text-gray-300 hover:text-white">Cancel</button>
          <button
            onClick={handleSave}
            disabled={saving}
            className="px-5 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg disabled:opacity-50"
          >
            {saving ? 'Saving…' : 'Save Offer'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function AdminProductOffersPage() {
  const { id: productId } = useParams<{ id: string }>()
  const [product, setProduct] = useState<ProductSearchResult | null>(null)
  const [offers, setOffers] = useState<OfferSummary[]>([])
  const [clients, setClients] = useState<Client[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<OfferSummary | null | 'new'>(null)

  async function load() {
    if (!productId) return
    try {
      const [productList, offerList, clientList] = await Promise.all([
        productsApi.search('', 1, 100, true),
        offersApi.listByProduct(productId),
        listClients(),
      ])
      setProduct(productList.find((p) => p.id === productId) ?? null)
      setOffers(offerList)
      setClients(clientList)
    } catch {
      setError('Failed to load offers.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [productId]) // eslint-disable-line react-hooks/exhaustive-deps

  async function handleSave(data: EditorState) {
    if (!productId) return
    if (editing === 'new') {
      await offersApi.create({
        productId, name: data.name, fullPrice: data.fullPrice, shipping: data.shipping,
        taxExempt: data.taxExempt, shippingExempt: data.shippingExempt,
        clientId: data.clientId || null, campaignId: data.campaignId || null,
      })
    } else if (editing) {
      await offersApi.update(editing.id, {
        name: data.name, fullPrice: data.fullPrice, shipping: data.shipping,
        taxExempt: data.taxExempt, shippingExempt: data.shippingExempt,
        clientId: data.clientId || null, campaignId: data.campaignId || null,
      })
    }
    await load()
  }

  async function toggleActive(offer: OfferSummary) {
    if (offer.isActive) await offersApi.deactivate(offer.id)
    else await offersApi.activate(offer.id)
    await load()
  }

  const sorted = [...offers].sort((a, b) => a.name.localeCompare(b.name))

  return (
    <AdminShell>
      <div className="max-w-4xl mx-auto">
        <Link to="/admin/products" className="text-sm text-gray-400 hover:text-gray-200">← Products</Link>

        <div className="flex items-center justify-between mb-6 mt-2">
          <div>
            <h1 className="text-2xl font-bold text-white">
              Offers {product && <span className="text-gray-400 font-normal">— {product.description}</span>}
            </h1>
            <p className="text-sm text-gray-400 mt-1">
              Each offer is a sales configuration for this product — its own price, shipping, and
              client/campaign scope.
            </p>
          </div>
          <button
            onClick={() => setEditing('new')}
            className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg"
          >
            + New Offer
          </button>
        </div>

        {error && <p className="text-red-400 text-sm mb-4">{error}</p>}

        {loading ? (
          <p className="text-gray-400">Loading…</p>
        ) : sorted.length === 0 ? (
          <p className="text-gray-500 italic">No offers for this product yet.</p>
        ) : (
          <div className="space-y-3">
            {sorted.map((o) => (
              <div key={o.id} className="bg-gray-800 border border-gray-700 rounded-xl p-4 flex items-center justify-between gap-4">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-white font-medium">{o.name}</span>
                    <span className="text-emerald-400 text-sm font-medium">${o.fullPrice.toFixed(2)}</span>
                    {!o.isActive && (
                      <span className="text-xs bg-gray-700 text-gray-400 px-2 py-0.5 rounded-full">Inactive</span>
                    )}
                  </div>
                  <div className="flex items-center gap-4 mt-1">
                    <span className="text-xs text-gray-400">Shipping: ${o.shipping.toFixed(2)}</span>
                    <span className="text-xs text-gray-400">{scopeLabel(o, clients)}</span>
                  </div>
                </div>
                <button
                  onClick={() => toggleActive(o)}
                  className="px-3 py-1.5 text-sm bg-gray-700 hover:bg-gray-600 text-white rounded-lg flex-shrink-0"
                >
                  {o.isActive ? 'Deactivate' : 'Activate'}
                </button>
                <button
                  onClick={() => setEditing(o)}
                  className="px-3 py-1.5 text-sm bg-gray-700 hover:bg-gray-600 text-white rounded-lg flex-shrink-0"
                >
                  Edit
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      {editing !== null && (
        <OfferEditorModal
          offer={editing === 'new' ? null : editing}
          clients={clients}
          onSave={handleSave}
          onClose={() => setEditing(null)}
        />
      )}
    </AdminShell>
  )
}
