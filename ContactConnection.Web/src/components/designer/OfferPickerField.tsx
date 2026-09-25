import { useState } from 'react'
import { productsApi, type ProductSearchResult } from '../../api/products'
import { offersApi, type OfferSummary } from '../../api/offers'

interface OfferPickerFieldProps {
  onPick: (offerId: string, displayName: string) => void
  onCancel: () => void
}

// Inline (not modal) product-search-then-offer-pick control for the flow designer's properties
// panel — search a product (reusing the agent cart's own productsApi.search), pick one, then pick
// one of its offers (offersApi.listByProduct). Kept separate from CartModal's own inline
// search-and-pick logic (which serves a different, already-verified add-to-cart flow) rather than
// extracting a shared component, to avoid touching that working code for this.
export default function OfferPickerField({ onPick, onCancel }: OfferPickerFieldProps) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<ProductSearchResult[]>([])
  const [selectedProduct, setSelectedProduct] = useState<ProductSearchResult | null>(null)
  const [offers, setOffers] = useState<OfferSummary[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function runSearch(e: React.FormEvent) {
    e.preventDefault()
    if (!query.trim()) return
    setBusy(true)
    setError(null)
    try {
      setResults(await productsApi.search(query.trim(), 1, 20, true))
      setSelectedProduct(null)
      setOffers([])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Search failed.')
    } finally {
      setBusy(false)
    }
  }

  async function pickProduct(product: ProductSearchResult) {
    setSelectedProduct(product)
    setError(null)
    try {
      setOffers(await offersApi.listByProduct(product.id))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load offers for this product.')
    }
  }

  return (
    <div className="border border-gray-700 rounded-lg p-2 bg-gray-800/50 flex flex-col gap-2">
      <form onSubmit={runSearch} className="flex gap-1.5">
        <input
          autoFocus
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search by product name, SKU, or offer name…"
          className="flex-1 min-w-0 bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
        />
        <button
          type="submit"
          disabled={busy}
          className="px-2 py-1 text-xs font-medium rounded bg-indigo-600 hover:bg-indigo-500 text-white disabled:opacity-50 shrink-0"
        >
          Search
        </button>
        <button type="button" onClick={onCancel} className="px-1 text-gray-400 hover:text-gray-200 shrink-0">×</button>
      </form>

      {error && <p className="text-[10px] text-red-400">{error}</p>}

      {selectedProduct ? (
        <div className="flex flex-col gap-1">
          <button
            type="button"
            onClick={() => { setSelectedProduct(null); setOffers([]) }}
            className="text-[10px] text-gray-500 hover:text-gray-300 self-start"
          >
            ← Back to results
          </button>
          {offers.length === 0 ? (
            <p className="text-[10px] text-gray-500 italic">No offers for this product.</p>
          ) : (
            offers.map((o) => (
              <button
                key={o.id}
                type="button"
                onClick={() => onPick(o.id, `${selectedProduct.description} — ${o.name}`)}
                className="flex items-center justify-between bg-gray-800 hover:bg-emerald-900/40 border border-gray-700 hover:border-emerald-600 rounded px-2 py-1 text-left transition-colors"
              >
                <span className="text-xs text-white truncate">{o.name}</span>
                <span className="text-xs text-emerald-400 font-medium shrink-0 ml-2">${o.fullPrice.toFixed(2)}</span>
              </button>
            ))
          )}
        </div>
      ) : (
        results.length > 0 && (
          <div className="flex flex-col gap-1 max-h-40 overflow-y-auto">
            {results.map((p) => (
              <button
                key={p.id}
                type="button"
                onClick={() => pickProduct(p)}
                className="flex items-center justify-between bg-gray-800 hover:bg-gray-750 border border-gray-700 rounded px-2 py-1 text-left transition-colors"
              >
                <span className="text-xs text-white truncate">{p.description}</span>
                <span className="text-[10px] text-gray-500 shrink-0 ml-2">{p.sku}</span>
              </button>
            ))}
          </div>
        )
      )}
    </div>
  )
}
