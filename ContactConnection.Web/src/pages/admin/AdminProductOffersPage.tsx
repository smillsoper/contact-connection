import { useEffect, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import { offersApi, type OfferFlag, type OfferSummary } from '../../api/offers'
import { productsApi, type ProductSearchResult } from '../../api/products'
import { listClients, type Client } from '../../api/telephony'
import ScopePicker, { scopeLabel } from '../../components/admin/ScopePicker'
import { DeleteIcon } from '../../components/icons/Icons'

interface EditorState {
  name: string
  sku: string
  fullPrice: number
  shipping: number
  taxExempt: boolean
  shippingExempt: boolean
  clientId: string
  campaignIds: string[]
  taxCode: string
  autoShip: boolean
  autoShipOptional: boolean
  autoShipDays: number[]
  isUpsell: boolean
  upsellQty: number
  upsellQtyOfEntry: number
  upsellCommission: number
  upsellClientAmount: number
  flags: OfferFlag[]
}

const inputCls = 'w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm disabled:opacity-50'
const labelCls = 'block text-sm font-medium text-gray-300 mb-1'

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="border-t border-gray-800 pt-4 space-y-3">
      <p className="text-xs font-semibold uppercase tracking-wide text-gray-500">{title}</p>
      {children}
    </div>
  )
}

function NumberField({ label, value, onChange, step = '1', help, disabled }: {
  label: string; value: number; onChange: (n: number) => void; step?: string; help?: string; disabled?: boolean
}) {
  return (
    <div>
      <label className={labelCls}>{label}</label>
      <input type="number" step={step} value={value} disabled={disabled}
        onChange={(e) => onChange(Number(e.target.value) || 0)} className={inputCls} />
      {help && <p className="text-xs text-gray-500 mt-1">{help}</p>}
    </div>
  )
}

function OfferEditorModal({
  offer,
  product,
  clients,
  onSave,
  onClose,
}: {
  offer: OfferSummary | null // null = new
  product: ProductSearchResult | null
  clients: Client[]
  onSave: (data: EditorState) => Promise<void>
  onClose: () => void
}) {
  const isEdit = offer !== null
  const [s, setS] = useState<EditorState>({
    name: offer?.name ?? '',
    sku: offer?.sku ?? '',
    fullPrice: offer?.fullPrice ?? 0,
    shipping: offer?.shipping ?? 0,
    taxExempt: offer?.taxExempt ?? false,
    shippingExempt: offer?.shippingExempt ?? false,
    clientId: offer?.clientId ?? product?.clientId ?? '',
    campaignIds: offer?.campaignIds ?? product?.campaignIds ?? [],
    taxCode: offer?.taxCode ?? '',
    autoShip: offer?.autoShip?.autoShip ?? false,
    autoShipOptional: offer?.autoShip?.autoShipOptional ?? false,
    autoShipDays: offer?.autoShip?.autoShipIntervals.map((i) => i.intervalDays) ?? [],
    isUpsell: offer?.upsell?.isUpsell ?? false,
    upsellQty: offer?.upsell?.upsellQty ?? 0,
    upsellQtyOfEntry: offer?.upsell?.upsellQtyOfEntry ?? 0,
    upsellCommission: offer?.upsell?.upsellCommission ?? 0,
    upsellClientAmount: offer?.upsell?.upsellClientAmount ?? 0,
    flags: offer?.flags ?? [],
  })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const set = (patch: Partial<EditorState>) => setS((prev) => ({ ...prev, ...patch }))

  async function handleSave() {
    setError(null)
    if (!s.name.trim()) { setError('Name is required.'); return }
    if (s.fullPrice <= 0) { setError('Full price must be greater than zero.'); return }
    if (s.autoShip && s.autoShipDays.filter((d) => d > 0).length === 0) {
      setError('AutoShip needs at least one interval (days between shipments).'); return
    }
    setSaving(true)
    try {
      await onSave({
        ...s,
        name: s.name.trim(), sku: s.sku.trim(), taxCode: s.taxCode.trim(),
        autoShipDays: s.autoShipDays.filter((d) => d > 0),
        flags: s.flags.filter((f) => f.name.trim()),
      })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
      <div className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-2xl max-h-[90vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-700">
          <h2 className="text-lg font-semibold text-white">{isEdit ? 'Edit Offer' : 'New Offer'}</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-white text-xl leading-none">&times;</button>
        </div>

        <div className="overflow-y-auto flex-1 px-6 py-4 space-y-4">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div>
              <label className={labelCls}>Name</label>
              <input value={s.name} onChange={(e) => set({ name: e.target.value })} className={inputCls}
                placeholder="e.g. Buy 2 Get 1 Free Every 3 Months" />
            </div>
            <div>
              <label className={labelCls}>SKU override</label>
              <input value={s.sku} onChange={(e) => set({ sku: e.target.value })} className={`${inputCls} font-mono`}
                placeholder={product ? `Blank = product SKU (${product.sku})` : 'Blank = product SKU'} />
              <p className="text-xs text-gray-500 mt-1">The fulfillment SKU for this variant — carried on the cart, order and order API.</p>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <NumberField label="Full Price" step="0.01" value={s.fullPrice} onChange={(n) => set({ fullPrice: n })}
              help="Priced as a single full payment. Multi-payment plans stay API-only for now." />
            <NumberField label="Shipping" step="0.01" value={s.shipping} onChange={(n) => set({ shipping: n })} />
          </div>

          <div className="flex gap-6">
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={s.taxExempt} onChange={(e) => set({ taxExempt: e.target.checked })} className="accent-indigo-600" />
              <span className="text-sm text-gray-300">Tax exempt</span>
            </label>
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={s.shippingExempt} onChange={(e) => set({ shippingExempt: e.target.checked })} className="accent-indigo-600" />
              <span className="text-sm text-gray-300">Shipping exempt</span>
            </label>
          </div>

          <div>
            <label className={labelCls}>Tax code override</label>
            <input value={s.taxCode} onChange={(e) => set({ taxCode: e.target.value })} disabled={s.taxExempt} className={inputCls}
              placeholder={s.taxExempt ? 'Not used — offer is tax exempt' : "Blank = the product's tax code"} />
          </div>

          <ScopePicker
            clients={clients} clientId={s.clientId} campaignIds={s.campaignIds}
            onChange={(cl, ca) => set({ clientId: cl, campaignIds: ca })}
            help="Tenant-wide offers are visible everywhere. Check campaigns to limit this offer to just those scripts."
          />

          <Section title="AutoShip">
            <div className="flex flex-wrap gap-6">
              <label className="flex items-center gap-2 cursor-pointer">
                <input type="checkbox" checked={s.autoShip}
                  onChange={(e) => set({ autoShip: e.target.checked, autoShipDays: e.target.checked && s.autoShipDays.length === 0 ? [30] : s.autoShipDays })}
                  className="accent-indigo-600" />
                <span className="text-sm text-gray-300">AutoShip (recurring shipments)</span>
              </label>
              <label className={`flex items-center gap-2 ${s.autoShip ? 'cursor-pointer' : 'opacity-50'}`}>
                <input type="checkbox" checked={s.autoShipOptional} disabled={!s.autoShip}
                  onChange={(e) => set({ autoShipOptional: e.target.checked })} className="accent-indigo-600" />
                <span className="text-sm text-gray-300">Customer may decline AutoShip</span>
              </label>
            </div>
            {s.autoShip && (
              <div>
                <label className={labelCls}>Intervals (days between shipments)</label>
                <div className="flex flex-wrap items-center gap-2">
                  {s.autoShipDays.map((d, i) => (
                    <div key={i} className="flex items-center gap-1">
                      <input type="number" min={1} value={d}
                        onChange={(e) => set({ autoShipDays: s.autoShipDays.map((x, j) => (j === i ? Number(e.target.value) || 0 : x)) })}
                        className="w-20 bg-gray-800 border border-gray-600 rounded-lg px-2 py-1.5 text-white text-sm" />
                      <button type="button" onClick={() => set({ autoShipDays: s.autoShipDays.filter((_, j) => j !== i) })}
                        className="text-gray-500 hover:text-red-400 text-xs px-1" title="Remove interval"><DeleteIcon size={13} /></button>
                    </div>
                  ))}
                  <button type="button" onClick={() => set({ autoShipDays: [...s.autoShipDays, 30] })}
                    className="text-indigo-400 hover:text-indigo-300 text-xs">+ Interval</button>
                </div>
                <p className="text-xs text-gray-500 mt-1">One interval is applied automatically; with several, the agent picks one.</p>
              </div>
            )}
          </Section>

          <Section title="Upsell">
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={s.isUpsell} onChange={(e) => set({ isUpsell: e.target.checked })} className="accent-indigo-600" />
              <span className="text-sm text-gray-300">This offer is an upsell</span>
            </label>
            {s.isUpsell && (
              <div className="grid grid-cols-2 gap-3">
                <NumberField label="Upsell quantity" value={s.upsellQty} onChange={(n) => set({ upsellQty: n })} />
                <NumberField label="Entry quantity that triggers it" value={s.upsellQtyOfEntry} onChange={(n) => set({ upsellQtyOfEntry: n })} />
                <NumberField label="Agent commission" step="0.01" value={s.upsellCommission} onChange={(n) => set({ upsellCommission: n })} />
                <NumberField label="Client amount" step="0.01" value={s.upsellClientAmount} onChange={(n) => set({ upsellClientAmount: n })} />
              </div>
            )}
          </Section>

          <Section title="Flags">
            <p className="text-xs text-gray-500 -mt-1">Name/value pairs for reporting and exports — e.g. "Cannella SKU".</p>
            {s.flags.map((f, i) => (
              <div key={i} className="flex gap-2">
                <input value={f.name} placeholder="Name"
                  onChange={(e) => set({ flags: s.flags.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)) })}
                  className="w-1/3 bg-gray-800 border border-gray-600 rounded-lg px-3 py-1.5 text-white text-sm" />
                <input value={f.value} placeholder="Value"
                  onChange={(e) => set({ flags: s.flags.map((x, j) => (j === i ? { ...x, value: e.target.value } : x)) })}
                  className="flex-1 bg-gray-800 border border-gray-600 rounded-lg px-3 py-1.5 text-white text-sm" />
                <button type="button" onClick={() => set({ flags: s.flags.filter((_, j) => j !== i) })}
                  className="text-gray-500 hover:text-red-400 text-xs px-2">Remove</button>
              </div>
            ))}
            <button type="button" onClick={() => set({ flags: [...s.flags, { name: '', value: '' }] })}
              className="text-indigo-400 hover:text-indigo-300 text-xs">+ Flag</button>
          </Section>

          {error && <p className="text-red-400 text-sm">{error}</p>}
        </div>

        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-700">
          <button onClick={onClose} className="px-4 py-2 text-sm text-gray-300 hover:text-white">Cancel</button>
          <button onClick={handleSave} disabled={saving}
            className="px-5 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg disabled:opacity-50">
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
      const [p, offerList, clientList] = await Promise.all([
        productsApi.get(productId),
        offersApi.listByProduct(productId),
        listClients(),
      ])
      setProduct(p)
      setOffers(offerList)
      setClients(clientList)
    } catch {
      setError('Failed to load offers.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [productId]) // eslint-disable-line react-hooks/exhaustive-deps

  async function handleSave(d: EditorState) {
    if (!productId) return
    const intervals = d.autoShipDays.map((days) => ({ intervalDays: days }))
    if (editing === 'new') {
      await offersApi.create({
        productId, name: d.name, fullPrice: d.fullPrice, shipping: d.shipping,
        taxExempt: d.taxExempt, shippingExempt: d.shippingExempt,
        clientId: d.clientId || null, campaignIds: d.clientId ? d.campaignIds : [],
        taxCode: d.taxCode || null, sku: d.sku || null,
        autoShip: d.autoShip, autoShipOptional: d.autoShipOptional, autoShipIntervals: intervals,
        isUpsell: d.isUpsell, upsellQty: d.upsellQty, upsellQtyOfEntry: d.upsellQtyOfEntry,
        upsellCommission: d.upsellCommission, upsellClientAmount: d.upsellClientAmount,
        flags: d.flags,
      })
    } else if (editing) {
      await offersApi.update(editing.id, {
        name: d.name, fullPrice: d.fullPrice, shipping: d.shipping,
        taxExempt: d.taxExempt, shippingExempt: d.shippingExempt,
        clientId: d.clientId || null, campaignIds: d.clientId ? d.campaignIds : [],
        taxCode: d.taxCode || null, sku: d.sku,
        autoShip: d.autoShip, autoShipOptional: d.autoShipOptional, autoShipIntervals: intervals,
        upsell: {
          isUpsell: d.isUpsell, upsellQty: d.upsellQty, upsellQtyOfEntry: d.upsellQtyOfEntry,
          upsellCommission: d.upsellCommission, upsellClientAmount: d.upsellClientAmount,
        },
        flags: d.flags,
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
              Each offer is a sales configuration for this product — its own SKU, price, shipping,
              AutoShip and client/campaign scope.
            </p>
          </div>
          <button onClick={() => setEditing('new')}
            className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg">
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
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-white font-medium">{o.name}</span>
                    <span className="text-xs text-gray-500 font-mono">{o.effectiveSku ?? product?.sku}</span>
                    <span className="text-emerald-400 text-sm font-medium">${o.fullPrice.toFixed(2)}</span>
                    {o.autoShip?.autoShip && (
                      <span className="text-xs bg-sky-900/40 text-sky-300 px-2 py-0.5 rounded-full">
                        AutoShip {o.autoShip.autoShipIntervals.map((i) => `${i.intervalDays}d`).join(' / ')}
                      </span>
                    )}
                    {o.upsell?.isUpsell && <span className="text-xs bg-violet-900/40 text-violet-300 px-2 py-0.5 rounded-full">Upsell</span>}
                    {!o.isActive && <span className="text-xs bg-gray-700 text-gray-400 px-2 py-0.5 rounded-full">Inactive</span>}
                  </div>
                  <div className="flex flex-wrap items-center gap-4 mt-1">
                    <span className="text-xs text-gray-400">
                      {o.shippingExempt ? 'Shipping exempt' : `Shipping: $${o.shipping.toFixed(2)}`}
                    </span>
                    <span className="text-xs text-gray-400">{scopeLabel(o.clientId, o.campaignIds, clients)}</span>
                    {o.flags?.map((f) => (
                      <span key={f.name} className="text-xs text-gray-500">{f.name}: <span className="text-gray-300">{f.value}</span></span>
                    ))}
                  </div>
                </div>
                <button onClick={() => toggleActive(o)}
                  className="px-3 py-1.5 text-sm bg-gray-700 hover:bg-gray-600 text-white rounded-lg flex-shrink-0">
                  {o.isActive ? 'Deactivate' : 'Activate'}
                </button>
                <button onClick={() => setEditing(o)}
                  className="px-3 py-1.5 text-sm bg-gray-700 hover:bg-gray-600 text-white rounded-lg flex-shrink-0">
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
          product={product}
          clients={clients}
          onSave={handleSave}
          onClose={() => setEditing(null)}
        />
      )}
    </AdminShell>
  )
}
