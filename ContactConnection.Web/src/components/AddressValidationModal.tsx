import type { AddressValidationResult } from '../api/flows'

interface Props {
  result: AddressValidationResult
  originalAddress: Record<string, string>
  onSelectAddress: (address: Record<string, string>) => void
  onChangeAddress: () => void
}

function formatAddressLines(a: Record<string, string>): string[] {
  const lines: string[] = []
  const name = [a.firstName, a.middleInitial, a.lastName].filter(Boolean).join(' ')
  if (name) lines.push(name)
  if (a.company) lines.push(a.company)
  const addr1 = [a.address1Prefix, a.address1].filter(Boolean).join(' ')
  if (addr1) lines.push(addr1)
  const addr2 = [a.address2Prefix, a.address2].filter(Boolean).join(' ')
  if (addr2) lines.push(addr2)
  const zip = a.zip4 ? `${a.zip}-${a.zip4}` : a.zip
  const cityLine = [a.city, a.state, zip].filter(Boolean).join(', ')
  if (cityLine) lines.push(cityLine)
  if (a.country && a.country !== 'US') lines.push(a.country)
  return lines
}

function AddressCard({
  label,
  address,
  selected,
  onSelect,
}: {
  label: string
  address: Record<string, string>
  selected: boolean
  onSelect: () => void
}) {
  const lines = formatAddressLines(address)
  return (
    <button
      type="button"
      onClick={onSelect}
      className={`w-full text-left rounded-lg border px-4 py-3 transition-colors ${
        selected
          ? 'border-indigo-500 bg-indigo-950/40 ring-1 ring-indigo-500'
          : 'border-gray-600 bg-gray-800/60 hover:border-gray-500'
      }`}
    >
      <p className={`text-xs font-semibold mb-1.5 ${selected ? 'text-indigo-300' : 'text-gray-400'}`}>{label}</p>
      {lines.length > 0 ? (
        lines.map((l, i) => (
          <p key={i} className="text-sm text-gray-200 leading-snug">{l}</p>
        ))
      ) : (
        <p className="text-sm text-gray-500 italic">No address data</p>
      )}
    </button>
  )
}

export default function AddressValidationModal({ result, originalAddress, onSelectAddress, onChangeAddress }: Props) {
  const { outcomeKey, outcomeLabel, message } = result

  // Merge correctedFields over original address for "corrected" variant
  const correctedAddress: Record<string, string> = {
    ...originalAddress,
    ...(result.correctedFields ?? {}),
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm p-4">
      <div className="bg-gray-900 border border-gray-700 rounded-xl shadow-2xl w-full max-w-2xl overflow-hidden flex flex-col max-h-[90vh]">
        {/* Header */}
        <div className={`px-6 py-4 shrink-0 border-b border-gray-700/60 ${
          outcomeKey === 'error' ? 'bg-red-950/30' :
          outcomeKey === 'no_match' ? 'bg-orange-950/30' :
          'bg-indigo-950/30'
        }`}>
          <p className={`text-xs font-semibold uppercase tracking-wider mb-0.5 ${
            outcomeKey === 'error' ? 'text-red-400' :
            outcomeKey === 'no_match' ? 'text-orange-400' :
            'text-indigo-400'
          }`}>{outcomeLabel}</p>
          {message && (
            <p className="text-sm text-gray-300 whitespace-pre-line">{message}</p>
          )}
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-6 py-5 space-y-4">

          {/* ── corrected ──────────────────────────────────────── */}
          {outcomeKey === 'corrected' && (
            <CorrectedVariant
              originalAddress={originalAddress}
              correctedAddress={correctedAddress}
              onSelectAddress={onSelectAddress}
              onChangeAddress={onChangeAddress}
            />
          )}

          {/* ── multiple_matches ───────────────────────────────── */}
          {outcomeKey === 'multiple_matches' && (
            <MultipleMatchesVariant
              originalAddress={originalAddress}
              matches={result.matches ?? []}
              onSelectAddress={onSelectAddress}
              onChangeAddress={onChangeAddress}
            />
          )}

          {/* ── no_match | error ──────────────────────────────── */}
          {(outcomeKey === 'no_match' || outcomeKey === 'error') && (
            <NoMatchVariant
              originalAddress={originalAddress}
              onSelectAddress={onSelectAddress}
              onChangeAddress={onChangeAddress}
            />
          )}
        </div>
      </div>
    </div>
  )
}

// ── Corrected variant ─────────────────────────────────────────────────────────

function CorrectedVariant({
  originalAddress,
  correctedAddress,
  onSelectAddress,
  onChangeAddress,
}: {
  originalAddress: Record<string, string>
  correctedAddress: Record<string, string>
  onSelectAddress: (a: Record<string, string>) => void
  onChangeAddress: () => void
}) {
  const [selected, setSelected] = React.useState<'corrected' | 'original'>('corrected')

  return (
    <>
      <p className="text-sm text-gray-400">
        A corrected address was found. Select the address you'd like to use and continue.
      </p>
      <div className="grid grid-cols-2 gap-3">
        <AddressCard
          label="Corrected Address"
          address={correctedAddress}
          selected={selected === 'corrected'}
          onSelect={() => setSelected('corrected')}
        />
        <AddressCard
          label="Original Address"
          address={originalAddress}
          selected={selected === 'original'}
          onSelect={() => setSelected('original')}
        />
      </div>
      <div className="flex justify-end gap-3 pt-2">
        <button
          type="button"
          onClick={onChangeAddress}
          className="px-4 py-2 rounded-lg text-sm text-gray-400 hover:text-gray-200 border border-gray-700 hover:border-gray-500 transition-colors"
        >
          Change Address
        </button>
        <button
          type="button"
          onClick={() => onSelectAddress(selected === 'corrected' ? correctedAddress : originalAddress)}
          className="px-4 py-2 rounded-lg text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-500 transition-colors"
        >
          Continue with {selected === 'corrected' ? 'Corrected' : 'Original'}
        </button>
      </div>
    </>
  )
}

// ── Multiple matches variant ──────────────────────────────────────────────────

function MultipleMatchesVariant({
  originalAddress,
  matches,
  onSelectAddress,
  onChangeAddress,
}: {
  originalAddress: Record<string, string>
  matches: Record<string, string>[]
  onSelectAddress: (a: Record<string, string>) => void
  onChangeAddress: () => void
}) {
  const [selected, setSelected] = React.useState<number | 'original'>(0)

  const allOptions: { label: string; address: Record<string, string> }[] = [
    ...matches.map((m, i) => ({ label: `Match ${i + 1}`, address: { ...originalAddress, ...m } })),
    { label: 'Original Address', address: originalAddress },
  ]

  const selectedAddress =
    selected === 'original' ? originalAddress : (allOptions[selected as number]?.address ?? originalAddress)

  return (
    <>
      <p className="text-sm text-gray-400">
        Multiple addresses were found. Select the correct address and continue.
      </p>
      <div className="space-y-2 max-h-72 overflow-y-auto pr-1">
        {allOptions.map((opt, i) => {
          const key = i < matches.length ? i : 'original'
          return (
            <AddressCard
              key={i}
              label={opt.label}
              address={opt.address}
              selected={selected === key}
              onSelect={() => setSelected(key)}
            />
          )
        })}
      </div>
      <div className="flex justify-end gap-3 pt-2">
        <button
          type="button"
          onClick={onChangeAddress}
          className="px-4 py-2 rounded-lg text-sm text-gray-400 hover:text-gray-200 border border-gray-700 hover:border-gray-500 transition-colors"
        >
          Change Address
        </button>
        <button
          type="button"
          onClick={() => onSelectAddress(selectedAddress)}
          className="px-4 py-2 rounded-lg text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-500 transition-colors"
        >
          Continue with Selected
        </button>
      </div>
    </>
  )
}

// ── No match / error variant ──────────────────────────────────────────────────

function NoMatchVariant({
  originalAddress,
  onSelectAddress,
  onChangeAddress,
}: {
  originalAddress: Record<string, string>
  onSelectAddress: (a: Record<string, string>) => void
  onChangeAddress: () => void
}) {
  const lines = formatAddressLines(originalAddress)
  return (
    <>
      <p className="text-sm text-gray-400">
        The address could not be verified. You can correct it, or continue as manually verified.
      </p>
      {lines.length > 0 && (
        <div className="bg-gray-800/50 border border-gray-700/50 rounded-lg px-4 py-3">
          <p className="text-xs font-semibold text-gray-400 mb-1.5">Entered Address</p>
          {lines.map((l, i) => (
            <p key={i} className="text-sm text-gray-200 leading-snug">{l}</p>
          ))}
        </div>
      )}
      <div className="flex justify-end gap-3 pt-2">
        <button
          type="button"
          onClick={onChangeAddress}
          className="px-4 py-2 rounded-lg text-sm text-gray-400 hover:text-gray-200 border border-gray-700 hover:border-gray-500 transition-colors"
        >
          Change Address
        </button>
        <button
          type="button"
          onClick={() => onSelectAddress({ ...originalAddress, isVerified: 'false' })}
          className="px-4 py-2 rounded-lg text-sm font-medium text-white bg-orange-600 hover:bg-orange-500 transition-colors"
        >
          Continue as Manually Verified
        </button>
      </div>
    </>
  )
}

// React import needed for useState in sub-components
import React from 'react'
