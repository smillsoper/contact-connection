import { useState } from 'react'
import { cartApi, type CartDocument } from '../../api/cart'
import { productsApi, type ProductSearchResult } from '../../api/products'
import { offersApi, type OfferSummary } from '../../api/offers'

interface CartModalProps {
  callRecordId: string
  cart: CartDocument | null
  onChanged: (cart: CartDocument) => void
  onClose: () => void
}

type View = 'cart' | 'search'

export default function CartModal({ callRecordId, cart, onChanged, onClose }: CartModalProps) {
  const [view, setView] = useState<View>('cart')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // Search sub-view state
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<ProductSearchResult[]>([])
  const [selectedProduct, setSelectedProduct] = useState<ProductSearchResult | null>(null)
  const [offers, setOffers] = useState<OfferSummary[]>([])
  const [addQty, setAddQty] = useState(1)

  async function runSearch(e: React.FormEvent) {
    e.preventDefault()
    if (!query.trim()) return
    setError(null)
    setBusy(true)
    try {
      const found = await productsApi.search(query.trim())
      setResults(found)
      setSelectedProduct(null)
      setOffers([])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Search failed.')
    } finally {
      setBusy(false)
    }
  }

  async function pickProduct(product: ProductSearchResult) {
    setError(null)
    setSelectedProduct(product)
    setAddQty(1)
    try {
      setOffers(await offersApi.listByProduct(product.id))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load offers for this product.')
    }
  }

  async function addOffer(offer: OfferSummary) {
    setError(null)
    setBusy(true)
    try {
      const updated = await cartApi.addItem(callRecordId, offer.id, addQty)
      onChanged(updated)
      setView('cart')
      setSelectedProduct(null)
      setOffers([])
      setResults([])
      setQuery('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not add item.')
    } finally {
      setBusy(false)
    }
  }

  async function changeQty(itemIndex: number, newQty: number) {
    if (newQty < 1) return
    setError(null)
    setBusy(true)
    try {
      onChanged(await cartApi.updateQuantity(callRecordId, itemIndex, newQty))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not update quantity.')
    } finally {
      setBusy(false)
    }
  }

  async function removeItem(itemIndex: number) {
    setError(null)
    setBusy(true)
    try {
      onChanged(await cartApi.removeItem(callRecordId, itemIndex))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not remove item.')
    } finally {
      setBusy(false)
    }
  }

  const items = cart?.items ?? []
  const isOutOfStock = (p: ProductSearchResult) =>
    p.inventory.inventoryStatus === 'OutOfStock' || p.inventory.inventoryStatus === 'Discontinued'

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm"
      onClick={onClose}
    >
      <div
        className="bg-gray-900 border border-gray-700 rounded-xl shadow-2xl w-[520px] max-h-[80vh] flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-3 border-b border-gray-800 shrink-0">
          <div className="flex items-center gap-3">
            <h2 className="text-sm font-semibold text-white">Cart</h2>
            <div className="flex rounded-lg overflow-hidden border border-gray-700 text-xs">
              <button
                type="button"
                onClick={() => setView('cart')}
                className={`px-3 py-1 transition-colors ${view === 'cart' ? 'bg-indigo-600 text-white' : 'bg-gray-800 text-gray-400 hover:text-gray-200'}`}
              >
                Items ({items.length})
              </button>
              <button
                type="button"
                onClick={() => setView('search')}
                className={`px-3 py-1 transition-colors ${view === 'search' ? 'bg-indigo-600 text-white' : 'bg-gray-800 text-gray-400 hover:text-gray-200'}`}
              >
                Add Product
              </button>
            </div>
          </div>
          <button type="button" onClick={onClose} className="text-gray-500 hover:text-gray-300 text-lg leading-none">
            ×
          </button>
        </div>

        {error && (
          <div className="mx-5 mt-3 px-3 py-2 rounded-lg bg-red-950/40 border border-red-800/50 text-xs text-red-300">
            {error}
          </div>
        )}

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-5 py-3">
          {view === 'cart' && (
            items.length === 0 ? (
              <p className="text-sm text-gray-500 italic py-6 text-center">Cart is empty.</p>
            ) : (
              <div className="flex flex-col gap-2">
                {items.map((item, i) => (
                  <div key={i} className="flex items-center gap-3 bg-gray-800 border border-gray-700 rounded-lg px-3 py-2">
                    <div className="flex-1 min-w-0">
                      <p className="text-sm text-white truncate">{item.description}</p>
                      <p className="text-[11px] text-gray-500">{item.sku} · ${item.fullPrice.toFixed(2)} ea</p>
                    </div>
                    <div className="flex items-center gap-1.5 shrink-0">
                      <button
                        type="button"
                        disabled={busy || item.quantity <= 1}
                        onClick={() => changeQty(i, item.quantity - 1)}
                        className="w-6 h-6 rounded bg-gray-700 hover:bg-gray-600 disabled:opacity-30 text-white text-sm"
                      >
                        −
                      </button>
                      <span className="w-6 text-center text-sm text-white">{item.quantity}</span>
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => changeQty(i, item.quantity + 1)}
                        className="w-6 h-6 rounded bg-gray-700 hover:bg-gray-600 disabled:opacity-30 text-white text-sm"
                      >
                        +
                      </button>
                    </div>
                    <p className="w-16 text-right text-sm text-white shrink-0">${item.extendedPrice.toFixed(2)}</p>
                    <button
                      type="button"
                      disabled={busy}
                      onClick={() => removeItem(i)}
                      className="text-red-400 hover:text-red-300 text-xs shrink-0 disabled:opacity-30"
                    >
                      Remove
                    </button>
                  </div>
                ))}
              </div>
            )
          )}

          {view === 'search' && (
            <div className="flex flex-col gap-3">
              <form onSubmit={runSearch} className="flex gap-2">
                <input
                  autoFocus
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="Search by name or SKU…"
                  className="flex-1 bg-gray-800 border border-gray-700 rounded px-3 py-1.5 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-indigo-500"
                />
                <button
                  type="submit"
                  disabled={busy}
                  className="px-3 py-1.5 text-xs font-medium rounded bg-indigo-600 hover:bg-indigo-500 text-white disabled:opacity-50"
                >
                  Search
                </button>
              </form>

              {selectedProduct ? (
                <div className="flex flex-col gap-2">
                  <button
                    type="button"
                    onClick={() => { setSelectedProduct(null); setOffers([]) }}
                    className="text-xs text-gray-500 hover:text-gray-300 self-start"
                  >
                    ← Back to results
                  </button>
                  <p className="text-sm text-white">{selectedProduct.description}</p>
                  <div className="flex items-center gap-2 mb-1">
                    <label className="text-xs text-gray-400">Qty</label>
                    <input
                      type="number"
                      min={1}
                      value={addQty}
                      onChange={(e) => setAddQty(Math.max(1, Number(e.target.value) || 1))}
                      className="w-16 bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white"
                    />
                  </div>
                  {offers.length === 0 ? (
                    <p className="text-xs text-gray-500 italic">No active offers for this product.</p>
                  ) : (
                    offers.filter((o) => o.isActive).map((offer) => (
                      <button
                        key={offer.id}
                        type="button"
                        disabled={busy}
                        onClick={() => addOffer(offer)}
                        className="flex items-center justify-between bg-gray-800 hover:bg-emerald-900/40 border border-gray-700 hover:border-emerald-600 rounded-lg px-3 py-2 text-left transition-colors disabled:opacity-50"
                      >
                        <span className="text-sm text-white">{offer.name}</span>
                        <span className="text-sm text-emerald-400 font-medium">${offer.fullPrice.toFixed(2)}</span>
                      </button>
                    ))
                  )}
                </div>
              ) : (
                <div className="flex flex-col gap-1.5">
                  {results.map((p) => (
                    <button
                      key={p.id}
                      type="button"
                      onClick={() => pickProduct(p)}
                      className="flex items-center justify-between bg-gray-800 hover:bg-gray-750 border border-gray-700 rounded-lg px-3 py-2 text-left transition-colors"
                    >
                      <div className="min-w-0">
                        <p className="text-sm text-white truncate">{p.description}</p>
                        <p className="text-[11px] text-gray-500">{p.sku}</p>
                      </div>
                      {isOutOfStock(p) && (
                        <span className="text-[10px] font-medium text-amber-400 shrink-0 ml-2">Out of stock</span>
                      )}
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer totals */}
        {view === 'cart' && cart && (
          <div className="px-5 py-3 border-t border-gray-800 shrink-0 flex flex-col gap-0.5 text-xs text-gray-400">
            <div className="flex justify-between"><span>Subtotal</span><span>${cart.cartSubtotal.toFixed(2)}</span></div>
            <div className="flex justify-between"><span>Shipping</span><span>${cart.shipping.toFixed(2)}</span></div>
            <div className="flex justify-between"><span>Tax</span><span>${cart.salesTax.toFixed(2)}</span></div>
            <div className="flex justify-between text-sm text-white font-semibold mt-1">
              <span>Total</span><span>${cart.cartTotal.toFixed(2)}</span>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
