import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import { productsApi, type ProductSearchResult, type ProductInventoryStatus } from '../../api/products'

const INVENTORY_STATUSES: ProductInventoryStatus[] = ['Available', 'CanBackorder', 'NoBackorder', 'OutOfStock', 'Discontinued']

const STATUS_BADGE: Record<ProductInventoryStatus, string> = {
  Available: 'bg-emerald-900/40 text-emerald-300',
  CanBackorder: 'bg-cyan-900/40 text-cyan-300',
  NoBackorder: 'bg-amber-900/40 text-amber-300',
  OutOfStock: 'bg-amber-900/40 text-amber-300',
  Discontinued: 'bg-gray-700 text-gray-400',
}

interface EditorState {
  weight: number
  inventoryStatus: ProductInventoryStatus
  qtyAvailable: number
  decrementOnOrder: boolean
  minimumQty: number
  searchable: boolean
}

function ProductEditorModal({
  product,
  onSave,
  onClose,
}: {
  product: ProductSearchResult | null // null = new
  onSave: (sku: string, description: string, data: EditorState) => Promise<void>
  onClose: () => void
}) {
  const isEdit = product !== null

  const [sku, setSku] = useState(product?.sku ?? '')
  const [description, setDescription] = useState(product?.description ?? '')
  const [weight, setWeight] = useState(product?.weight ?? 0)
  const [inventoryStatus, setInventoryStatus] = useState<ProductInventoryStatus>(product?.inventory.inventoryStatus ?? 'Available')
  const [qtyAvailable, setQtyAvailable] = useState(product?.inventory.qtyAvailable ?? 0)
  const [decrementOnOrder, setDecrementOnOrder] = useState(product?.inventory.decrementOnOrder ?? false)
  const [minimumQty, setMinimumQty] = useState(product?.inventory.minimumQty ?? 0)
  const [searchable, setSearchable] = useState(product?.searchable ?? true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSave() {
    setError(null)
    if (!isEdit) {
      if (!sku.trim()) { setError('SKU is required.'); return }
      if (!description.trim()) { setError('Description is required.'); return }
    }

    setSaving(true)
    try {
      await onSave(sku.trim(), description.trim(), {
        weight, inventoryStatus, qtyAvailable, decrementOnOrder, minimumQty, searchable,
      })
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
          <h2 className="text-lg font-semibold text-white">{isEdit ? 'Edit Product' : 'New Product'}</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-white text-xl leading-none">&times;</button>
        </div>

        <div className="overflow-y-auto flex-1 px-6 py-4 space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">SKU</label>
            <input
              value={sku}
              onChange={(e) => setSku(e.target.value)}
              disabled={isEdit}
              className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm font-mono disabled:opacity-50"
              placeholder="e.g. WIDGET-001"
            />
            {isEdit && <p className="text-xs text-gray-500 mt-1">SKU can't be changed after creation.</p>}
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Description</label>
            <input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              disabled={isEdit}
              className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm disabled:opacity-50"
              placeholder="e.g. Widget Mobile — 1 Month Supply"
            />
            {isEdit && <p className="text-xs text-gray-500 mt-1">Description can't be changed after creation.</p>}
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-1">Inventory Status</label>
              <select
                value={inventoryStatus}
                onChange={(e) => setInventoryStatus(e.target.value as ProductInventoryStatus)}
                className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
              >
                {INVENTORY_STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-1">Weight</label>
              <input
                type="number" step="0.01"
                value={weight}
                onChange={(e) => setWeight(Number(e.target.value) || 0)}
                className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
              />
            </div>
          </div>

          <label className="flex items-center gap-3 cursor-pointer">
            <button
              type="button"
              onClick={() => setDecrementOnOrder((v) => !v)}
              className={`inline-flex h-5 w-10 flex-shrink-0 cursor-pointer rounded-full p-0.5 transition-colors duration-200 ${decrementOnOrder ? 'bg-indigo-600' : 'bg-gray-600'}`}
            >
              <span className={`block h-4 w-4 rounded-full bg-white shadow-sm transition-transform duration-200 ${decrementOnOrder ? 'translate-x-5' : 'translate-x-0'}`} />
            </button>
            <span className="text-sm text-gray-300">Track real inventory quantity (off = status flag only, matches Life Seasons)</span>
          </label>

          {decrementOnOrder && (
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-1">Qty Available</label>
                <input
                  type="number"
                  value={qtyAvailable}
                  onChange={(e) => setQtyAvailable(Number(e.target.value) || 0)}
                  className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-1">Minimum Qty</label>
                <input
                  type="number"
                  value={minimumQty}
                  onChange={(e) => setMinimumQty(Number(e.target.value) || 0)}
                  className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm"
                />
              </div>
            </div>
          )}

          <label className="flex items-center gap-3 cursor-pointer">
            <button
              type="button"
              onClick={() => setSearchable((v) => !v)}
              className={`inline-flex h-5 w-10 flex-shrink-0 cursor-pointer rounded-full p-0.5 transition-colors duration-200 ${searchable ? 'bg-indigo-600' : 'bg-gray-600'}`}
            >
              <span className={`block h-4 w-4 rounded-full bg-white shadow-sm transition-transform duration-200 ${searchable ? 'translate-x-5' : 'translate-x-0'}`} />
            </button>
            <span className="text-sm text-gray-300">Searchable (visible to agents in the cart's product search)</span>
          </label>

          {error && <p className="text-red-400 text-sm">{error}</p>}
        </div>

        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-700">
          <button onClick={onClose} className="px-4 py-2 text-sm text-gray-300 hover:text-white">Cancel</button>
          <button
            onClick={handleSave}
            disabled={saving}
            className="px-5 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg disabled:opacity-50"
          >
            {saving ? 'Saving…' : 'Save Product'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function AdminProductsPage() {
  const [products, setProducts] = useState<ProductSearchResult[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<ProductSearchResult | null | 'new'>(null)

  async function load() {
    try {
      setProducts(await productsApi.search('', 1, 100, true))
    } catch {
      setError('Failed to load products.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  async function handleSave(sku: string, description: string, data: EditorState) {
    if (editing === 'new') {
      await productsApi.create({
        sku, description, weight: data.weight,
        inventoryStatus: data.inventoryStatus,
        qtyAvailable: data.qtyAvailable,
        decrementOnOrder: data.decrementOnOrder,
      })
    } else if (editing) {
      await productsApi.update(editing.id, {
        weight: data.weight,
        inventoryStatus: data.inventoryStatus,
        qtyAvailable: data.qtyAvailable,
        decrementOnOrder: data.decrementOnOrder,
        minimumQty: data.minimumQty,
        searchable: data.searchable,
      })
    }
    await load()
  }

  const sorted = [...products].sort((a, b) => a.description.localeCompare(b.description))

  return (
    <AdminShell>
      <div className="max-w-4xl mx-auto">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h1 className="text-2xl font-bold text-white">Products</h1>
            <p className="text-sm text-gray-400 mt-1">
              The physical catalog — pricing and campaign assignment live on each product's Offers.
            </p>
          </div>
          <button
            onClick={() => setEditing('new')}
            className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg"
          >
            + New Product
          </button>
        </div>

        {error && <p className="text-red-400 text-sm mb-4">{error}</p>}

        {loading ? (
          <p className="text-gray-400">Loading…</p>
        ) : sorted.length === 0 ? (
          <p className="text-gray-500 italic">No products yet.</p>
        ) : (
          <div className="space-y-3">
            {sorted.map((p) => (
              <div key={p.id} className="bg-gray-800 border border-gray-700 rounded-xl p-4 flex items-center justify-between gap-4">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-white font-medium truncate">{p.description}</span>
                    <span className="text-xs text-gray-500 font-mono">{p.sku}</span>
                    <span className={`text-xs px-2 py-0.5 rounded-full ${STATUS_BADGE[p.inventory.inventoryStatus]}`}>
                      {p.inventory.inventoryStatus}
                    </span>
                    {!p.searchable && (
                      <span className="text-xs bg-gray-700 text-gray-400 px-2 py-0.5 rounded-full">Not searchable</span>
                    )}
                  </div>
                </div>
                <Link
                  to={`/admin/products/${p.id}/offers`}
                  className="px-3 py-1.5 text-sm bg-gray-700 hover:bg-gray-600 text-white rounded-lg flex-shrink-0"
                >
                  Manage Offers →
                </Link>
                <button
                  onClick={() => setEditing(p)}
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
        <ProductEditorModal
          product={editing === 'new' ? null : editing}
          onSave={handleSave}
          onClose={() => setEditing(null)}
        />
      )}
    </AdminShell>
  )
}
