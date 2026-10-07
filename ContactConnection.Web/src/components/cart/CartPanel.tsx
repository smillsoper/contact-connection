import { useCallback, useEffect, useState } from 'react'
import { useCallStore } from '../../stores/callStore'
import { useFlowSessionsStore } from '../../stores/flowSessionsStore'
import { cartApi, type CartDocument } from '../../api/cart'
import CartModal from './CartModal'
import { CartIcon, WarningIcon } from '../icons/Icons'

// Always-visible cart summary strip at the top of the center Flow/Script column — clicking it
// opens the full CartModal. Renders nothing when there's no active call record (no cart context).
export default function CartPanel() {
  const callRecordId = useCallStore((s) => s.callRecordId)
  const cartVersion = useCallStore((s) => s.cartVersion)
  // The active tab's script session: its interaction owns the cart (S178 — a transferred call's CS agent has
  // their own cart, separate from the sales agent's).
  const sessionId = useFlowSessionsStore((s) => s.activeSessionId)
  const [cart, setCart] = useState<CartDocument | null>(null)
  const [modalOpen, setModalOpen] = useState(false)

  const refresh = useCallback(() => {
    if (!callRecordId) { setCart(null); return }
    cartApi.get(callRecordId, sessionId).then((c) => setCart(c ?? null)).catch(() => setCart(null))
  }, [callRecordId, sessionId])

  // Refetch on every flow advance/jump/start too (cartVersion), not just when callRecordId first
  // appears — cart-mutating CRM nodes (add_to_cart/remove_cart_item/reset_cart) have no display or
  // event of their own, so a node transition is the only signal that the cart might have changed.
  useEffect(() => { refresh() }, [refresh, cartVersion])

  if (!callRecordId) return null

  const itemCount = cart?.items.reduce((n, i) => n + i.quantity, 0) ?? 0
  const total = cart?.cartTotal ?? 0
  const taxProblem = !!cart && cart.items.length > 0 && (cart.taxStatus === 'error' || cart.taxStatus === 'pending_address')

  return (
    <>
      <div className="flex items-center justify-between px-4 py-2 bg-gray-900 border-b border-gray-800 shrink-0">
        <span className="text-sm text-gray-300">
          <CartIcon size={13} className="inline -mt-0.5 mr-1" />Cart: <span className="font-semibold text-white">{itemCount}</span> item{itemCount === 1 ? '' : 's'}
          {' — '}<span className="font-semibold text-white">${total.toFixed(2)}</span>
          {taxProblem && (
            <span
              title={cart?.taxMessage ?? ''}
              className={`ml-2 text-xs ${cart?.taxStatus === 'error' ? 'text-red-400' : 'text-amber-400'}`}
            >
              {cart?.taxStatus === 'error' ? <><WarningIcon size={10} className="inline -mt-0.5 mr-0.5" />tax not calculated</> : 'tax pending address'}
            </span>
          )}
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
          sessionId={sessionId}
          cart={cart}
          onChanged={setCart}
          onClose={() => setModalOpen(false)}
        />
      )}
    </>
  )
}
