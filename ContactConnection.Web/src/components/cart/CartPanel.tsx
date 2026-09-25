import { useCallback, useEffect, useState } from 'react'
import { useCallStore } from '../../stores/callStore'
import { cartApi, type CartDocument } from '../../api/cart'
import CartModal from './CartModal'

// Always-visible cart summary strip at the top of the center Flow/Script column — clicking it
// opens the full CartModal. Renders nothing when there's no active call record (no cart context).
export default function CartPanel() {
  const callRecordId = useCallStore((s) => s.callRecordId)
  const cartVersion = useCallStore((s) => s.cartVersion)
  const [cart, setCart] = useState<CartDocument | null>(null)
  const [modalOpen, setModalOpen] = useState(false)

  const refresh = useCallback(() => {
    if (!callRecordId) { setCart(null); return }
    cartApi.get(callRecordId).then((c) => setCart(c ?? null)).catch(() => setCart(null))
  }, [callRecordId])

  // Refetch on every flow advance/jump/start too (cartVersion), not just when callRecordId first
  // appears — cart-mutating CRM nodes (add_to_cart/remove_cart_item/reset_cart) have no display or
  // event of their own, so a node transition is the only signal that the cart might have changed.
  useEffect(() => { refresh() }, [refresh, cartVersion])

  if (!callRecordId) return null

  const itemCount = cart?.items.reduce((n, i) => n + i.quantity, 0) ?? 0
  const total = cart?.cartTotal ?? 0

  return (
    <>
      <div className="flex items-center justify-between px-4 py-2 bg-gray-900 border-b border-gray-800 shrink-0">
        <span className="text-sm text-gray-300">
          🛒 Cart: <span className="font-semibold text-white">{itemCount}</span> item{itemCount === 1 ? '' : 's'}
          {' — '}<span className="font-semibold text-white">${total.toFixed(2)}</span>
        </span>
        <button
          type="button"
          onClick={() => setModalOpen(true)}
          className="text-xs px-3 py-1 rounded bg-indigo-600 hover:bg-indigo-500 text-white transition-colors"
        >
          View Cart
        </button>
      </div>

      {modalOpen && (
        <CartModal
          callRecordId={callRecordId}
          cart={cart}
          onChanged={setCart}
          onClose={() => setModalOpen(false)}
        />
      )}
    </>
  )
}
