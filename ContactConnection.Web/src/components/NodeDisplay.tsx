import { useState, useEffect, useRef, type FormEvent } from 'react'
import type { FlowNodeState } from '../types/flow'
import { COUNTRIES } from '../data/countries'

// ── Address form constants ────────────────────────────────────────────────────

const ADDR1_PREFIXES = ['', 'PO Box', 'PMB', 'RR', 'SR', 'Hwy']
const ADDR2_PREFIXES = ['', 'Apt', 'Ste', 'Floor', 'Level', 'Space', 'Lot', 'Unit']

const US_STATES: [string, string][] = [
  ['', '— State —'],
  ['AL', 'AL'], ['AK', 'AK'], ['AZ', 'AZ'], ['AR', 'AR'], ['CA', 'CA'],
  ['CO', 'CO'], ['CT', 'CT'], ['DE', 'DE'], ['FL', 'FL'], ['GA', 'GA'],
  ['HI', 'HI'], ['ID', 'ID'], ['IL', 'IL'], ['IN', 'IN'], ['IA', 'IA'],
  ['KS', 'KS'], ['KY', 'KY'], ['LA', 'LA'], ['ME', 'ME'], ['MD', 'MD'],
  ['MA', 'MA'], ['MI', 'MI'], ['MN', 'MN'], ['MS', 'MS'], ['MO', 'MO'],
  ['MT', 'MT'], ['NE', 'NE'], ['NV', 'NV'], ['NH', 'NH'], ['NJ', 'NJ'],
  ['NM', 'NM'], ['NY', 'NY'], ['NC', 'NC'], ['ND', 'ND'], ['OH', 'OH'],
  ['OK', 'OK'], ['OR', 'OR'], ['PA', 'PA'], ['RI', 'RI'], ['SC', 'SC'],
  ['SD', 'SD'], ['TN', 'TN'], ['TX', 'TX'], ['UT', 'UT'], ['VT', 'VT'],
  ['VA', 'VA'], ['WA', 'WA'], ['WV', 'WV'], ['WI', 'WI'], ['WY', 'WY'],
  ['DC', 'DC'],
  ['PR', 'PR'], ['GU', 'GU'], ['VI', 'VI'], ['AS', 'AS'], ['MP', 'MP'],
  ['AA', 'AA'], ['AE', 'AE'], ['AP', 'AP'],
]

const CA_PROVINCES: [string, string][] = [
  ['', '— Province —'],
  ['AB', 'AB'], ['BC', 'BC'], ['MB', 'MB'], ['NB', 'NB'], ['NL', 'NL'],
  ['NS', 'NS'], ['NT', 'NT'], ['NU', 'NU'], ['ON', 'ON'], ['PE', 'PE'],
  ['QC', 'QC'], ['SK', 'SK'], ['YT', 'YT'],
]

interface AddrForm {
  firstName: string; middleInitial: string; lastName: string
  company: string
  address1Prefix: string; address1: string
  address2Prefix: string; address2: string
  zip: string; zip4: string; city: string; state: string
  country: string
}

const EMPTY_ADDR: AddrForm = {
  firstName: '', middleInitial: '', lastName: '',
  company: '',
  address1Prefix: '', address1: '',
  address2Prefix: '', address2: '',
  zip: '', zip4: '', city: '', state: '',
  country: 'US',
}

// ── WinForms-style input mask engine ──────────────────────────────────────
// Mask chars: 0=digit  9=digit/space  L=letter  ?=letter/space  A=alphanum  &=any required  C=any optional
// All other characters are literals and are auto-inserted.

const MASK_CHARS = new Set(['0', '9', 'L', '?', 'A', 'a', '&', 'C'])
const MASK_REQUIRED = new Set(['0', 'L', 'A', '&'])

function maskCharValid(ch: string, mc: string): boolean {
  switch (mc) {
    case '0': return /\d/.test(ch)
    case '9': return /[\d ]/.test(ch)
    case 'L': return /[a-zA-Z]/.test(ch)
    case '?': return /[a-zA-Z ]/.test(ch)
    case 'A': return /[a-zA-Z0-9]/.test(ch)
    case 'a': return /[a-zA-Z0-9 ]/.test(ch)
    case '&': return ch.length === 1
    case 'C': return true
    default:  return false
  }
}

// Format a user input string against a mask, auto-inserting literal characters.
// Strips any existing literals from userInput first so re-formatting is idempotent.
function applyMask(userInput: string, mask: string): string {
  // Keep only alphanumeric chars (covers 0/9/L/A patterns); '&'/'C' users may need special chars but that's uncommon
  const raw = [...userInput].filter(c => /[a-zA-Z0-9]/.test(c))
  let result = ''
  let ri = 0

  for (let mi = 0; mi < mask.length; mi++) {
    if (ri >= raw.length) break
    const mc = mask[mi]
    if (!MASK_CHARS.has(mc)) {
      result += mc   // literal — auto-insert, don't consume raw
      continue
    }
    // Skip raw chars that don't satisfy this mask position
    while (ri < raw.length && !maskCharValid(raw[ri], mc)) ri++
    if (ri < raw.length) {
      result += raw[ri]
      ri++
    }
  }
  return result
}

// True when all required mask positions are filled
function isMaskComplete(value: string, mask: string): boolean {
  // The fully-filled mask has length === mask.length
  // Since applyMask inserts all literals exactly once, complete fill = full length
  return value.length === mask.length
    && [...mask].every((mc, i) => !MASK_REQUIRED.has(mc) || (i < value.length && maskCharValid(value[i] ?? '', mc)))
}

// ── US-or-Canada ZIP/postal dual-mode mask ─────────────────────────────────
// Sentinel stored in inputMask when this mode is selected in the designer.
const ZIP_CA_SENTINEL = '__zip_ca__'

// Determine mode from the first alphanumeric character typed.
function zipCaMode(value: string): 'us' | 'ca' {
  const first = [...value].find((c) => /[a-zA-Z0-9]/.test(c))
  return first && /[a-zA-Z]/.test(first) ? 'ca' : 'us'
}

// Format the raw input as a US ZIP (DDDDD or DDDDD-DDDD) or Canadian postal
// code (A1A 1A1) depending on the first character entered.
function applyZipCaMask(raw: string): string {
  if (zipCaMode(raw) === 'ca') {
    return applyMask(raw, 'L0L 0L0')
  }
  const digits = raw.replace(/\D/g, '')
  if (digits.length <= 5) return digits
  return `${digits.slice(0, 5)}-${digits.slice(5, 9)}`
}

// Accept 5-digit ZIP, ZIP+4, or Canadian A1A 1A1
function isZipCaComplete(value: string): boolean {
  return /^\d{5}$/.test(value)
    || /^\d{5}-\d{4}$/.test(value)
    || /^[A-Za-z]\d[A-Za-z] \d[A-Za-z]\d$/.test(value)
}

interface Props {
  node: FlowNodeState
  onAdvance: (input?: string) => void
  onJump?: (sectionNodeId: string) => void
  advancing: boolean
}

export default function NodeDisplay({ node, onAdvance, onJump, advancing }: Props) {
  const [inputValue, setInputValue] = useState('')
  const [localError, setLocalError] = useState<string | null>(null)

  // Address form state
  const [addrForm, setAddrForm] = useState<AddrForm>(EMPTY_ADDR)
  const [focusedField, setFocusedField] = useState<string | null>(null)

  // Reset address form when navigating to a new node; pre-populate when available
  const prevAddrNodeId = useRef<string | null>(null)
  useEffect(() => {
    if (node.nodeType !== 'address') return
    if (node.nodeId === prevAddrNodeId.current) return
    prevAddrNodeId.current = node.nodeId
    const p = node.prefilledAddress
    if (p && Object.keys(p).length > 0) {
      // When not international, combine zip+zip4 into the single dual-mode masked field
      const zipValue = !node.allowInternational && p.zip
        ? applyZipCaMask(p.zip4 ? `${p.zip}-${p.zip4}` : p.zip)
        : (p.zip ?? '')
      const derivedCountry = !node.allowInternational
        ? (zipCaMode(zipValue) === 'ca' ? 'CA' : 'US')
        : (p.country ?? 'US')
      setAddrForm({
        firstName:      p.firstName      ?? '',
        middleInitial:  p.middleInitial  ?? '',
        lastName:       p.lastName       ?? '',
        company:        p.company        ?? '',
        address1Prefix: p.address1Prefix ?? '',
        address1:       p.address1       ?? '',
        address2Prefix: p.address2Prefix ?? '',
        address2:       p.address2       ?? '',
        zip:            zipValue,
        zip4:           node.allowInternational ? (p.zip4 ?? '') : '',
        city:           p.city           ?? '',
        state:          p.state          ?? '',
        country:        derivedCountry,
      })
    } else {
      setAddrForm(EMPTY_ADDR)
    }
    setFocusedField(null)
  }, [node.nodeId, node.nodeType, node.prefilledAddress, node.allowInternational])

  // Pre-populate inputValue when navigating to a node whose output variable already has a value
  const prevInputNodeId = useRef<string | null>(null)
  useEffect(() => {
    const capturesInput = node.nodeType === 'input' || node.nodeType === 'email' || node.nodeType === 'phone'
    if (!capturesInput) return
    if (node.nodeId === prevInputNodeId.current) return
    prevInputNodeId.current = node.nodeId
    setInputValue(node.defaultValue ?? '')
  }, [node.nodeId, node.nodeType, node.defaultValue])

  // Auto-focus the primary input whenever the displayed node changes
  const focusRef = useRef<HTMLElement | null>(null)
  useEffect(() => {
    setLocalError(null)
    focusRef.current?.focus()
  }, [node])

  function handleTextChange(raw: string) {
    const mask = node.inputMask
    setLocalError(null)
    if (mask === ZIP_CA_SENTINEL) {
      setInputValue(applyZipCaMask(raw))
    } else {
      setInputValue(mask ? applyMask(raw, mask) : raw)
    }
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    const mask = node.inputMask
    if (mask === ZIP_CA_SENTINEL) {
      const isEmpty = inputValue.length === 0
      if (!isEmpty && !isZipCaComplete(inputValue)) {
        setLocalError(
          node.required
            ? 'Please enter a valid 5-digit ZIP, ZIP+4 (00000-0000), or Canadian postal code (A1A 1A1).'
            : 'Please enter a valid ZIP or postal code, or clear the field.',
        )
        return
      }
      if (node.required && isEmpty) {
        setLocalError('This field is required.')
        return
      }
      onAdvance(inputValue)
      setInputValue('')
      return
    }
    if (mask) {
      const isEmpty   = inputValue.length === 0
      const complete  = isMaskComplete(inputValue, mask)
      if (!isEmpty && !complete) {
        // Partial entry is never permitted — required or not
        setLocalError(
          node.required
            ? 'Please complete the entire field before continuing.'
            : 'Please complete the entire field or clear it before continuing.',
        )
        return
      }
      if (node.required && isEmpty) {
        setLocalError('This field is required.')
        return
      }
    } else {
      const len = inputValue.length
      if (node.minChars && len < node.minChars) {
        setLocalError(`Minimum ${node.minChars} characters required (${len} entered).`)
        return
      }
      if (node.maxChars && len > node.maxChars) {
        setLocalError(`Maximum ${node.maxChars} characters allowed (${len} entered).`)
        return
      }
    }
    onAdvance(inputValue)
    setInputValue('')
  }

  function handleEmailSubmit(e: FormEvent) {
    e.preventDefault()
    // Always pass the value (even empty string) so the handler can distinguish
    // "submitted blank" from "first display" — required check is backend-enforced
    onAdvance(inputValue)
    setInputValue('')
  }

  function handleAddressSubmit(e: FormEvent) {
    e.preventDefault()
    const form = { ...addrForm }
    // When using the US/CA dual-mode mask, ZIP+4 is stored combined (e.g. "90210-1234").
    // Split it so the backend receives separate zip and zip4 fields as it expects.
    if (!node.allowInternational) {
      const zip4match = form.zip.match(/^(\d{5})-(\d{4})$/)
      if (zip4match) {
        form.zip  = zip4match[1]
        form.zip4 = zip4match[2]
      }
    }
    onAdvance(JSON.stringify(form))
  }

  const activeFieldScript =
    focusedField && node.fieldScripts?.[focusedField]?.trim()
      ? node.fieldScripts[focusedField]
      : null

  const locked = node.sectionLocked === true

  // Jump dropdown — shown on nodes that accept input when sections exist
  const jumpDropdown = node.jumpTargets && node.jumpTargets.length > 0 && (
    <div className="flex items-center gap-2 mt-1">
      <label className="text-xs text-gray-500 shrink-0">Jump to:</label>
      <select
        className="flex-1 bg-gray-800 text-gray-300 rounded px-2 py-1 text-xs border border-gray-700 focus:outline-none focus:border-sky-500"
        defaultValue=""
        disabled={advancing}
        onChange={(e) => {
          if (e.target.value && onJump) {
            onJump(e.target.value)
            e.target.value = ''
          }
        }}
      >
        <option value="">— Jump to section —</option>
        {node.jumpTargets.map((t) => (
          <option key={t.sectionNodeId} value={t.sectionNodeId}>
            {t.isCurrentSection ? `↺ ${t.name} (current)` : t.name}
            {t.isLocked ? ' 🔒' : ''}
          </option>
        ))}
      </select>
    </div>
  )

  if (node.nodeType === 'end') {
    return (
      <div className="flex flex-col items-center justify-center h-full gap-4 p-8 text-center">
        <div className="w-12 h-12 rounded-full bg-green-900/40 flex items-center justify-center">
          <svg className="w-6 h-6 text-green-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
          </svg>
        </div>
        <p className="text-lg font-medium text-white">{node.label}</p>
        {node.content && (
          <p className="text-gray-400 text-sm max-w-md">{node.content}</p>
        )}
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6 p-6 max-w-2xl mx-auto">
      {/* Section bar — shown when inside a named section */}
      {node.currentSectionName && (
        <div className="flex items-center gap-2 px-3 py-2 rounded-lg bg-gray-800/60 border border-gray-700">
          <span className="text-[10px] font-semibold uppercase tracking-wider text-gray-500">Section</span>
          <span className="text-sm font-medium text-gray-200">{node.currentSectionName}</span>
          {locked && (
            <span className="ml-auto text-[10px] font-semibold text-amber-400 bg-amber-950/50 border border-amber-800 rounded px-1.5 py-0.5">
              LOCKED
            </span>
          )}
        </div>
      )}
      {/* Script context from preceding script node — shown above the current input/end node */}
      {node.scriptContext && (
        <div
          className="script-content bg-gray-900 rounded-xl p-5 text-gray-100 text-sm leading-relaxed border border-gray-800"
          dangerouslySetInnerHTML={{ __html: node.scriptContext }}
        />
      )}

      {/* Node's own content (e.g. input prompt) */}
      {node.content && (
        <div
          className="script-content bg-gray-900 rounded-xl p-5 text-gray-100 text-sm leading-relaxed border border-gray-800"
          dangerouslySetInnerHTML={{ __html: node.content }}
        />
      )}

      {/* Inline script (input/email/phone nodes) — address manages its own script display */}
      {node.nodeType !== 'address' && node.nodeScriptLabel && (
        <p className="text-xs font-semibold text-gray-300 uppercase tracking-wider">
          {node.nodeScriptLabel}
        </p>
      )}
      {node.nodeType !== 'address' && node.nodeScriptContent && (
        <div
          className="script-content bg-gray-900 rounded-xl p-5 text-gray-100 text-sm leading-relaxed border border-gray-800"
          dangerouslySetInnerHTML={{ __html: node.nodeScriptContent }}
        />
      )}

      {/* Step label */}
      <div className="flex items-center gap-2">
        <span className="text-xs uppercase tracking-wider text-gray-500 font-medium">
          {node.nodeType.replace('_', ' ')}
        </span>
        <span className="text-white font-medium">{node.label}</span>
      </div>

      {/* Input area */}
      {node.nodeType === 'input' && locked ? (
        <div className="flex flex-col gap-3">
          <div className="bg-gray-800/50 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-300 min-h-[36px]">
            {node.defaultValue || <span className="text-gray-600 italic">no value</span>}
          </div>
          <button
            onClick={() => onAdvance(node.defaultValue ?? '')}
            disabled={advancing}
            className="self-start bg-gray-700 hover:bg-gray-600 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
          >
            {advancing ? 'Advancing…' : 'Continue'}
          </button>
          {jumpDropdown}
        </div>
      ) : node.nodeType === 'input' && (
        <form onSubmit={handleSubmit} className="flex flex-col gap-3">
          {node.inputType === 'select' && node.options ? (
            <select
              ref={(el) => { focusRef.current = el }}
              value={inputValue}
              onChange={(e) => setInputValue(e.target.value)}
              className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow border border-gray-700"
            >
              <option value="">Select…</option>
              {node.options.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>
          ) : node.inputType === 'checkbox' ? (
            <label className="flex items-center gap-2 text-sm text-gray-200 cursor-pointer">
              <input
                type="checkbox"
                checked={inputValue === 'true'}
                onChange={(e) => setInputValue(e.target.checked ? 'true' : 'false')}
                className="rounded accent-indigo-500"
              />
              {node.label}
            </label>
          ) : (
            /* text (default) — supports mask + min/max */
            <div className="flex flex-col gap-1">
              <input
                ref={(el) => { focusRef.current = el }}
                type="text"
                value={inputValue}
                onChange={(e) => handleTextChange(e.target.value)}
                placeholder={
                  node.inputMask === ZIP_CA_SENTINEL
                    ? '00000 or A1A 1A1'
                    : node.inputMask
                    ? node.inputMask.replace(/0/g, '_').replace(/[LA]/g, '_')
                    : 'Enter value…'
                }
                className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow border border-gray-700 font-mono"
              />
              {(node.minChars || node.maxChars) && !node.inputMask && (
                <p className="text-[11px] text-gray-500">
                  {node.minChars && node.maxChars
                    ? `${node.minChars}–${node.maxChars} characters`
                    : node.minChars
                    ? `Minimum ${node.minChars} characters`
                    : `Maximum ${node.maxChars} characters`}
                  {inputValue.length > 0 && ` · ${inputValue.length} entered`}
                </p>
              )}
            </div>
          )}

          {localError && (
            <p className="text-sm text-red-400 bg-red-950/40 border border-red-800 rounded-lg px-3 py-2">
              {localError}
            </p>
          )}

          <button
            type="submit"
            disabled={advancing}
            className="self-start bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
          >
            {advancing ? 'Advancing…' : 'Next'}
          </button>
          {jumpDropdown}
        </form>
      )}

      {/* Phone node */}
      {node.nodeType === 'phone' && locked ? (
        <div className="flex flex-col gap-3">
          <div className="bg-gray-800/50 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-300 font-mono min-h-[36px]">
            {node.defaultValue || <span className="text-gray-600 italic">no value</span>}
          </div>
          <button
            onClick={() => onAdvance(node.defaultValue ?? '')}
            disabled={advancing}
            className="self-start bg-gray-700 hover:bg-gray-600 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
          >
            {advancing ? 'Advancing…' : 'Continue'}
          </button>
          {jumpDropdown}
        </div>
      ) : node.nodeType === 'phone' && (
        <form onSubmit={handleSubmit} className="flex flex-col gap-3">
          {node.validationError && (
            <p className="text-sm text-red-400 bg-red-950/40 border border-red-800 rounded-lg px-3 py-2">
              {node.validationError}
            </p>
          )}
          <input
            ref={(el) => { focusRef.current = el }}
            type="text"
            value={inputValue}
            onChange={(e) => handleTextChange(e.target.value)}
            placeholder={node.inputMask ?? 'Enter phone number…'}
            className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow-teal border border-gray-700 font-mono"
          />
          {localError && (
            <p className="text-sm text-red-400 bg-red-950/40 border border-red-800 rounded-lg px-3 py-2">
              {localError}
            </p>
          )}
          <button
            type="submit"
            disabled={advancing}
            className="self-start bg-teal-700 hover:bg-teal-600 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
          >
            {advancing ? 'Validating…' : 'Next'}
          </button>
          {jumpDropdown}
        </form>
      )}

      {/* Email node */}
      {node.nodeType === 'email' && locked ? (
        <div className="flex flex-col gap-3">
          <div className="bg-gray-800/50 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-300 min-h-[36px]">
            {node.defaultValue || <span className="text-gray-600 italic">no value</span>}
          </div>
          <button
            onClick={() => onAdvance(node.defaultValue ?? '')}
            disabled={advancing}
            className="self-start bg-gray-700 hover:bg-gray-600 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
          >
            {advancing ? 'Advancing…' : 'Continue'}
          </button>
          {jumpDropdown}
        </div>
      ) : node.nodeType === 'email' && (
        <form onSubmit={handleEmailSubmit} className="flex flex-col gap-3">
          {node.validationError && (
            <p className="text-sm text-red-400 bg-red-950/40 border border-red-800 rounded-lg px-3 py-2">
              {node.validationError}
            </p>
          )}
          <input
            ref={(el) => { focusRef.current = el }}
            type="email"
            value={inputValue}
            onChange={(e) => setInputValue(e.target.value)}
            placeholder="Enter email address…"
            required={node.required}
            className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow-cyan border border-gray-700"
          />
          <button
            type="submit"
            disabled={advancing}
            className="self-start bg-cyan-700 hover:bg-cyan-600 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
          >
            {advancing ? 'Validating…' : 'Next'}
          </button>
          {jumpDropdown}
        </form>
      )}

      {/* Address node */}
      {node.nodeType === 'address' && locked ? (
        <div className="flex flex-col gap-3">
          <div className="bg-gray-800/50 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-300 leading-relaxed">
            {node.prefilledAddress && Object.keys(node.prefilledAddress).length > 0 ? (
              <>
                {[node.prefilledAddress.firstName, node.prefilledAddress.middleInitial, node.prefilledAddress.lastName].filter(Boolean).join(' ')}<br />
                {node.prefilledAddress.company && <>{node.prefilledAddress.company}<br /></>}
                {[node.prefilledAddress.address1Prefix, node.prefilledAddress.address1].filter(Boolean).join(' ')}<br />
                {node.prefilledAddress.address2 && <>{[node.prefilledAddress.address2Prefix, node.prefilledAddress.address2].filter(Boolean).join(' ')}<br /></>}
                {[node.prefilledAddress.city, node.prefilledAddress.state, node.prefilledAddress.zip].filter(Boolean).join(', ')}
              </>
            ) : <span className="text-gray-600 italic">no value</span>}
          </div>
          <button
            onClick={() => onAdvance(JSON.stringify(node.prefilledAddress ?? {}))}
            disabled={advancing}
            className="self-start bg-gray-700 hover:bg-gray-600 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
          >
            {advancing ? 'Advancing…' : 'Continue'}
          </button>
          {jumpDropdown}
        </div>
      ) : node.nodeType === 'address' && (
        <form onSubmit={handleAddressSubmit} className="flex flex-col gap-3">
          {/* Per-field or main script */}
          {node.nodeScriptLabel && !activeFieldScript && (
            <p className="text-xs font-semibold text-gray-300 uppercase tracking-wider">
              {node.nodeScriptLabel}
            </p>
          )}
          {(activeFieldScript ?? node.nodeScriptContent) && (
            <div
              className="script-content bg-gray-900 rounded-xl p-5 text-gray-100 text-sm leading-relaxed border border-gray-800"
              dangerouslySetInnerHTML={{ __html: (activeFieldScript ?? node.nodeScriptContent)! }}
            />
          )}

          {/* Validation error */}
          {node.validationError && (
            <p className="text-sm text-red-400 bg-red-950/40 border border-red-800 rounded-lg px-3 py-2">
              {node.validationError}
            </p>
          )}

          {/* Name row */}
          <div className="flex gap-2">
            <input
              ref={(el) => { focusRef.current = el }}
              type="text"
              value={addrForm.firstName}
              onChange={(e) => setAddrForm((f) => ({ ...f, firstName: e.target.value }))}
              onFocus={() => setFocusedField('firstName')}
              onBlur={() => setFocusedField(null)}
              placeholder="First name"
              className="flex-1 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow-orange border border-gray-700"
            />
            {node.showMiddleInitial && (
              <input
                type="text"
                value={addrForm.middleInitial}
                onChange={(e) => setAddrForm((f) => ({ ...f, middleInitial: e.target.value }))}
                onFocus={() => setFocusedField('middleInitial')}
                onBlur={() => setFocusedField(null)}
                placeholder="MI"
                maxLength={1}
                className="w-12 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow-orange border border-gray-700 text-center"
              />
            )}
            <input
              type="text"
              value={addrForm.lastName}
              onChange={(e) => setAddrForm((f) => ({ ...f, lastName: e.target.value }))}
              onFocus={() => setFocusedField('lastName')}
              onBlur={() => setFocusedField(null)}
              placeholder="Last name"
              className="flex-1 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow-orange border border-gray-700"
            />
          </div>

          {/* Company */}
          {node.showCompany && (
            <input
              type="text"
              value={addrForm.company}
              onChange={(e) => setAddrForm((f) => ({ ...f, company: e.target.value }))}
              onFocus={() => setFocusedField('company')}
              onBlur={() => setFocusedField(null)}
              placeholder="Company"
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow-orange border border-gray-700"
            />
          )}

          {/* Address line 1 */}
          <div className="flex gap-2">
            <select
              value={addrForm.address1Prefix}
              onChange={(e) => setAddrForm((f) => ({ ...f, address1Prefix: e.target.value }))}
              onFocus={() => setFocusedField('address1')}
              onBlur={() => setFocusedField(null)}
              className="bg-gray-800 text-white rounded-lg px-2 py-2 text-sm input-focus-glow-orange border border-gray-700"
            >
              {ADDR1_PREFIXES.map((p) => (
                <option key={p} value={p}>{p || '(none)'}</option>
              ))}
            </select>
            <input
              type="text"
              value={addrForm.address1}
              onChange={(e) => setAddrForm((f) => ({ ...f, address1: e.target.value }))}
              onFocus={() => setFocusedField('address1')}
              onBlur={() => setFocusedField(null)}
              placeholder="Address line 1"
              className="flex-1 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow-orange border border-gray-700"
            />
          </div>

          {/* Address line 2 */}
          <div className="flex gap-2">
            <select
              value={addrForm.address2Prefix}
              onChange={(e) => setAddrForm((f) => ({ ...f, address2Prefix: e.target.value }))}
              onFocus={() => setFocusedField('address2')}
              onBlur={() => setFocusedField(null)}
              className="bg-gray-800 text-white rounded-lg px-2 py-2 text-sm input-focus-glow-orange border border-gray-700"
            >
              {ADDR2_PREFIXES.map((p) => (
                <option key={p} value={p}>{p || '(none)'}</option>
              ))}
            </select>
            <input
              type="text"
              value={addrForm.address2}
              onChange={(e) => setAddrForm((f) => ({ ...f, address2: e.target.value }))}
              onFocus={() => setFocusedField('address2')}
              onBlur={() => setFocusedField(null)}
              placeholder="Address line 2 (optional)"
              className="flex-1 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow-orange border border-gray-700"
            />
          </div>

          {/* ZIP / City / State row */}
          <div className="flex gap-2">
            {!node.allowInternational ? (
              /* US or Canada dual-mode: single field, country derived from first character */
              <input
                type="text"
                value={addrForm.zip}
                onChange={(e) => {
                  const formatted = applyZipCaMask(e.target.value)
                  const newCountry = zipCaMode(formatted) === 'ca' ? 'CA' : 'US'
                  setAddrForm((f) => ({
                    ...f,
                    zip: formatted,
                    zip4: '',
                    country: newCountry,
                    state: newCountry !== f.country ? '' : f.state,
                  }))
                }}
                onFocus={() => setFocusedField('zip')}
                onBlur={() => setFocusedField(null)}
                placeholder="00000 or A1A 1A1"
                className="w-32 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow-orange border border-gray-700 font-mono"
              />
            ) : (
              /* International: separate ZIP and +4 fields */
              <>
                <input
                  type="text"
                  value={addrForm.zip}
                  onChange={(e) => setAddrForm((f) => ({ ...f, zip: e.target.value }))}
                  onFocus={() => setFocusedField('zip')}
                  onBlur={() => setFocusedField(null)}
                  placeholder="ZIP"
                  className="w-20 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow-orange border border-gray-700 font-mono"
                />
                <input
                  type="text"
                  value={addrForm.zip4}
                  onChange={(e) => setAddrForm((f) => ({ ...f, zip4: e.target.value }))}
                  onFocus={() => setFocusedField('zip')}
                  onBlur={() => setFocusedField(null)}
                  placeholder="+4"
                  maxLength={4}
                  className="w-14 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow-orange border border-gray-700 font-mono"
                />
              </>
            )}
            <input
              type="text"
              value={addrForm.city}
              onChange={(e) => setAddrForm((f) => ({ ...f, city: e.target.value }))}
              onFocus={() => setFocusedField('city')}
              onBlur={() => setFocusedField(null)}
              placeholder="City"
              className="flex-1 bg-gray-800 text-white rounded-lg px-3 py-2 text-sm input-focus-glow-orange border border-gray-700"
            />
            <select
              value={addrForm.state}
              onChange={(e) => setAddrForm((f) => ({ ...f, state: e.target.value }))}
              onFocus={() => setFocusedField('state')}
              onBlur={() => setFocusedField(null)}
              className="w-36 bg-gray-800 text-white rounded-lg px-2 py-2 text-sm input-focus-glow-orange border border-gray-700"
            >
              {(addrForm.country === 'CA' ? CA_PROVINCES : US_STATES).map(([v, l]) => (
                <option key={v} value={v}>{l}</option>
              ))}
            </select>
          </div>

          {/* Country — auto-derived from ZIP (disabled) when domestic; full list when international */}
          <select
            value={addrForm.country}
            onChange={(e) => setAddrForm((f) => ({ ...f, country: e.target.value }))}
            onFocus={() => setFocusedField('country')}
            onBlur={() => setFocusedField(null)}
            disabled={!node.allowInternational}
            className={`w-full bg-gray-800 text-white rounded-lg px-2 py-2 text-sm border border-gray-700 ${node.allowInternational ? 'input-focus-glow-orange' : 'opacity-60 cursor-not-allowed'}`}
          >
            {node.allowInternational ? (
              <>
                <optgroup label="Common">
                  <option value="US">United States</option>
                  <option value="CA">Canada</option>
                </optgroup>
                <optgroup label="All Countries">
                  {COUNTRIES.slice(2).map(([code, name]) => (
                    <option key={code} value={code}>{name}</option>
                  ))}
                </optgroup>
              </>
            ) : (
              <>
                <option value="US">United States</option>
                <option value="CA">Canada</option>
              </>
            )}
          </select>

          <button
            type="submit"
            disabled={advancing}
            className="self-start bg-orange-700 hover:bg-orange-600 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
          >
            {advancing ? 'Validating…' : 'Next'}
          </button>
          {jumpDropdown}
        </form>
      )}

      {/* Script node — just a Next button */}
      {(node.nodeType === 'script' || node.nodeType === 'branch' || node.nodeType === 'set_variable' || node.nodeType === 'api_call') && (
        <button
          onClick={() => onAdvance()}
          disabled={advancing}
          className="self-start bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
        >
          {advancing ? 'Advancing…' : 'Continue'}
        </button>
      )}
    </div>
  )
}
