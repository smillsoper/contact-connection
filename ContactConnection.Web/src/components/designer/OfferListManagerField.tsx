import { useState } from 'react'
import OfferPickerField from './OfferPickerField'

interface OfferListManagerFieldProps {
  offerIds: string[]
  offerNames: string[]
  onChange: (ids: string[], names: string[]) => void
  emptyMessage?: string
  addButtonLabel?: string
}

// Multi-select "pick one or more offers" list — a row per already-picked offer with a remove
// button, plus the shared OfferPickerField to add another. Used by add_to_cart's replace mode and
// by remove_cart_item, both of which need "which offer(s)" rather than a single offer.
export default function OfferListManagerField({
  offerIds,
  offerNames,
  onChange,
  emptyMessage = 'No offers selected yet.',
  addButtonLabel = '+ Add offer',
}: OfferListManagerFieldProps) {
  const [picking, setPicking] = useState(false)

  return (
    <div className="flex flex-col gap-1.5">
      {offerIds.length === 0 && !picking && (
        <p className="text-[10px] text-amber-400">{emptyMessage}</p>
      )}
      {offerIds.map((id, i) => (
        <div key={id} className="flex items-center justify-between bg-gray-800 border border-gray-700 rounded px-2 py-1.5">
          <span className="text-xs text-white truncate">{offerNames[i] ?? id}</span>
          <button
            type="button"
            onClick={() => onChange(offerIds.filter((_, j) => j !== i), offerNames.filter((_, j) => j !== i))}
            className="text-red-400 hover:text-red-300 text-xs shrink-0 ml-2"
          >
            Remove
          </button>
        </div>
      ))}
      {picking ? (
        <OfferPickerField
          onPick={(id, name) => {
            if (!offerIds.includes(id)) onChange([...offerIds, id], [...offerNames, name])
            setPicking(false)
          }}
          onCancel={() => setPicking(false)}
        />
      ) : (
        <button
          type="button"
          onClick={() => setPicking(true)}
          className="text-xs px-2 py-1.5 rounded bg-gray-700 hover:bg-gray-600 text-white self-start"
        >
          {addButtonLabel}
        </button>
      )}
    </div>
  )
}
