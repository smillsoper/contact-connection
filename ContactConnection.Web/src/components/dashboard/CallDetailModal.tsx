import { useState, type ReactNode } from 'react'
import type { CartDocument } from '../../api/cart'
import RecordingPlayer from './RecordingPlayer'

// A call's full details for dashboard viewers (S181) — the records widget's detail view on internal and client dashboards.
// Read-only: no audit history, no actions, never card data.

export interface CallDetailAddress {
  name: string; company: string | null; street: string; unit: string; city: string | null; state: string | null
  zip: string | null; country: string | null; verified: boolean
}

export interface CallDetailInteraction {
  number: number; campaignName: string | null; agentName: string | null; disposition: string | null; category: string | null
  status: string; startedAt: string | null; completedAt: string | null
  orderNumber: string | null; orderSubmittedAt: string | null; paymentStatus: string | null
  cart: CartDocument | null
  summary: { summary: string | null; reasonForCall: string | null; outcome: string | null; followUp: string | null } | null
  payments: { transactionType: string; amount: number; status: string; cardType: string | null; cardLast4: string | null; reason: string | null; at: string | null; voided: boolean }[]
  fields: Record<string, unknown> | null
  captured: { flowName: string | null; values: Record<string, string> }[]
}

export interface CallDetailData {
  id: string; startedAt: string | null; endedAt: string | null; handleTimeSeconds: number | null; source: string; overallStatus: string
  clientName: string | null; campaignName: string | null; callerId: string | null; dnis: string | null
  media: { agency: string; station: string; marketType: string; mediaType: string | null; adType: string | null; phoneNumber: string | null; fields: Record<string, string> } | null
  compoundDisposition: string | null
  contact: { firstName: string | null; lastName: string | null; email: string | null; phone: string | null; billingPhone: string | null; shippingPhone: string | null; accountNumber: string | null; clientNumber: string | null }
  billing: CallDetailAddress | null
  shipping: CallDetailAddress | null
  customFields: { label: string; value: string }[]
  interactions: CallDetailInteraction[]
  recordingStatus: string
}

const money = (n: number | null | undefined) => (n == null ? '—' : `$${n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`)
const duration = (s: number | null) => s == null ? null : s >= 3600
  ? `${Math.floor(s / 3600)}:${String(Math.floor(s / 60) % 60).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`
  : `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
const humanize = (key: string) => key.replace(/^(input|flow)\./, '').replace(/[._]/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2')

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="mb-5">
      <div className="text-[10px] uppercase tracking-wide text-gray-500 mb-2">{title}</div>
      {children}
    </div>
  )
}

function Fields({ rows }: { rows: [string, ReactNode][] }) {
  const shown = rows.filter(([, v]) => v !== null && v !== undefined && v !== '')
  if (shown.length === 0) return <p className="text-xs text-gray-600">Nothing recorded.</p>
  return (
    <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-1.5 text-sm">
      {shown.map(([k, v]) => (
        <div key={k} className="flex gap-3 min-w-0">
          <dt className="text-gray-500 w-36 shrink-0">{k}</dt>
          <dd className="text-gray-100 break-words min-w-0">{v}</dd>
        </div>
      ))}
    </dl>
  )
}

function AddressBlock({ title, a }: { title: string; a: CallDetailAddress | null }) {
  return (
    <div className="border border-gray-800 rounded-lg p-3">
      <div className="text-xs text-gray-400 mb-1">{title}</div>
      {!a ? <p className="text-xs text-gray-600">None</p> : (
        <div className="text-sm text-gray-100 leading-relaxed">
          {a.name && <div>{a.name}</div>}
          {a.company && <div>{a.company}</div>}
          <div>{[a.street, a.unit].filter(Boolean).join(' ')}</div>
          <div>{[a.city, a.state].filter(Boolean).join(', ')} {a.zip}</div>
          {a.country && a.country !== 'US' && <div>{a.country}</div>}
          {a.verified && <div className="text-[11px] text-emerald-400 mt-1">Verified address</div>}
        </div>
      )}
    </div>
  )
}

function Cart({ cart }: { cart: CartDocument }) {
  return (
    <div className="border border-gray-800 rounded-lg overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="text-xs text-gray-500">
          <tr className="border-b border-gray-800">
            <th className="text-left px-3 py-1.5">Item</th><th className="text-left px-3 py-1.5">SKU</th>
            <th className="text-right px-3 py-1.5">Qty</th><th className="text-right px-3 py-1.5">Price</th><th className="text-right px-3 py-1.5">Total</th>
          </tr>
        </thead>
        <tbody>
          {cart.items.map((it, i) => (
            <tr key={i} className="border-b border-gray-800/60">
              <td className="px-3 py-1.5 text-gray-100">{it.description}</td>
              <td className="px-3 py-1.5 text-gray-400 font-mono text-xs">{it.sku}</td>
              <td className="px-3 py-1.5 text-right text-gray-200">{it.quantity}</td>
              <td className="px-3 py-1.5 text-right text-gray-200">{money(it.fullPrice)}</td>
              <td className="px-3 py-1.5 text-right text-gray-100">{money(it.extendedPrice)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot className="text-gray-300">
          <tr><td colSpan={4} className="px-3 pt-2 text-right text-gray-500">Subtotal</td><td className="px-3 pt-2 text-right">{money(cart.cartSubtotal)}</td></tr>
          <tr><td colSpan={4} className="px-3 text-right text-gray-500">Shipping</td><td className="px-3 text-right">{money(cart.shipping)}</td></tr>
          <tr><td colSpan={4} className="px-3 text-right text-gray-500">Tax</td><td className="px-3 text-right">{money(cart.salesTax)}</td></tr>
          {(cart.fees ?? []).map((f) => (
            <tr key={f.code}><td colSpan={4} className="px-3 text-right text-gray-500">{f.description}</td><td className="px-3 text-right">{money(f.amount)}</td></tr>
          ))}
          <tr><td colSpan={4} className="px-3 pb-2 text-right text-gray-300 font-medium">Total</td><td className="px-3 pb-2 text-right text-white font-medium">{money(cart.cartTotal)}</td></tr>
        </tfoot>
      </table>
    </div>
  )
}

function InteractionCard({ ix, multiple }: { ix: CallDetailInteraction; multiple: boolean }) {
  return (
    <div className="border border-gray-800 rounded-xl p-4 mb-4 bg-gray-950/40">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 mb-3">
        {multiple && <span className="text-white font-medium">Interaction #{ix.number}</span>}
        <span className="text-gray-200">{ix.campaignName ?? '—'}</span>
        {ix.agentName && <span className="text-gray-400">{ix.agentName}</span>}
        <span className="text-gray-100">{ix.disposition ?? <span className="text-gray-500">No disposition</span>}</span>
        {ix.category && <span className="text-[11px] bg-gray-800 text-gray-300 border border-gray-700 rounded px-1.5 py-0.5">{ix.category}</span>}
        <span className="ml-auto text-xs text-gray-500">{ix.startedAt}{ix.completedAt ? ` → ${ix.completedAt}` : ''}</span>
      </div>
      {ix.summary && (ix.summary.summary || ix.summary.reasonForCall) && (
        <Section title="Call summary">
          <Fields rows={[['Summary', ix.summary.summary], ['Reason for call', ix.summary.reasonForCall], ['Outcome', ix.summary.outcome], ['Follow-up', ix.summary.followUp]]} />
        </Section>
      )}
      <Section title="Order">
        <Fields rows={[['Order number', ix.orderNumber], ['Submitted', ix.orderSubmittedAt], ['Payment', ix.paymentStatus]]} />
      </Section>
      {ix.cart && ix.cart.items.length > 0 && <Section title="Cart"><Cart cart={ix.cart} /></Section>}
      {ix.payments.length > 0 && (
        <Section title="Payments">
          <div className="space-y-1 text-sm">
            {ix.payments.map((p, i) => (
              <div key={i} className="flex flex-wrap gap-x-4 text-gray-300">
                <span className="text-gray-500 w-40">{p.at}</span>
                <span>{p.transactionType}</span>
                <span className="text-gray-100">{money(p.amount)}</span>
                <span className={p.status === 'approved' && !p.voided ? 'text-emerald-400' : 'text-amber-300'}>{p.voided ? 'voided' : p.status}</span>
                {p.cardLast4 && <span className="text-gray-400">{p.cardType ?? 'Card'} ending {p.cardLast4}</span>}
                {p.reason && p.status !== 'approved' && <span className="text-gray-500">{p.reason}</span>}
              </div>
            ))}
          </div>
        </Section>
      )}
    </div>
  )
}

type Tab = 'overview' | 'customer' | 'interactions' | 'captured'

export default function CallDetailModal({ data, canPlayRecording, loadRecording, onClose }: {
  data: CallDetailData | null
  error?: string | null
  canPlayRecording: boolean
  loadRecording: () => Promise<Response>
  onClose: () => void
}) {
  const [tab, setTab] = useState<Tab>('overview')
  const multiple = (data?.interactions.length ?? 0) > 1
  const capturedCount = data?.interactions.reduce((n, i) => n + i.captured.reduce((m, c) => m + Object.keys(c.values).length, 0)
    + Object.keys(i.fields ?? {}).length, 0) ?? 0
  const tabs: { key: Tab; label: string }[] = [
    { key: 'overview', label: 'Overview' },
    { key: 'customer', label: 'Customer' },
    { key: 'interactions', label: multiple ? `Interactions (${data?.interactions.length})` : 'Order & summary' },
    { key: 'captured', label: `Captured data${capturedCount ? ` (${capturedCount})` : ''}` },
  ]

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-3 sm:p-6" onClick={onClose}>
      <div className="bg-gray-900 border border-gray-800 rounded-xl shadow-xl w-full max-w-5xl max-h-[92vh] flex flex-col" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-start justify-between gap-4 px-5 pt-4">
          <div className="min-w-0">
            <h3 className="text-base font-semibold text-white truncate">
              {data ? [data.contact.firstName, data.contact.lastName].filter(Boolean).join(' ') || data.callerId || 'Call' : 'Call details'}
            </h3>
            {data && <p className="text-xs text-gray-400">{[data.campaignName, data.startedAt, data.compoundDisposition].filter(Boolean).join(' · ')}</p>}
          </div>
          <button className="text-gray-400 hover:text-white text-lg leading-none" onClick={onClose}>✕</button>
        </div>
        <div className="flex gap-1 border-b border-gray-800 px-5 mt-3 overflow-x-auto">
          {tabs.map((t) => (
            <button key={t.key} onClick={() => setTab(t.key)}
              className={`text-xs px-3 py-2 -mb-px border-b-2 whitespace-nowrap ${tab === t.key ? 'border-sky-500 text-white' : 'border-transparent text-gray-400 hover:text-gray-200'}`}>
              {t.label}
            </button>
          ))}
        </div>

        <div className="overflow-y-auto min-h-0 flex-1 px-5 py-4">
          {!data && <p className="text-sm text-gray-500">Loading…</p>}
          {data && tab === 'overview' && (
            <>
              <Section title="Call">
                <Fields rows={[
                  ['Started', data.startedAt], ['Ended', data.endedAt], ['Duration', duration(data.handleTimeSeconds)],
                  ['Client', data.clientName], ['Campaign', data.campaignName], ['Disposition', data.compoundDisposition],
                  ['Caller number', data.callerId], ['Number dialed', data.dnis], ['Direction', data.source], ['Status', data.overallStatus],
                ]} />
              </Section>
              {data.media && (
                <Section title="Media">
                  <Fields rows={[
                    ['Agency', data.media.agency], ['Station', data.media.station], ['Market', data.media.marketType],
                    ['Media type', data.media.mediaType], ['Ad type', data.media.adType], ['Tracking number', data.media.phoneNumber],
                    ...Object.entries(data.media.fields ?? {}).map(([k, v]) => [k, v] as [string, ReactNode]),
                  ]} />
                </Section>
              )}
              <Section title="Recording">
                {canPlayRecording ? <RecordingPlayer key={data.id} load={loadRecording} />
                  : <p className="text-sm text-gray-500">{data.recordingStatus === 'purged' ? 'Deleted under the retention policy.'
                      : data.recordingStatus === 'none' ? 'No recording for this call.' : 'Not available to you.'}</p>}
              </Section>
            </>
          )}
          {data && tab === 'customer' && (
            <>
              <Section title="Contact">
                <Fields rows={[
                  ['First name', data.contact.firstName], ['Last name', data.contact.lastName], ['Email', data.contact.email],
                  ['Phone', data.contact.phone], ['Billing phone', data.contact.billingPhone], ['Shipping phone', data.contact.shippingPhone],
                  ['Account number', data.contact.accountNumber], ['Client number', data.contact.clientNumber],
                ]} />
              </Section>
              <Section title="Addresses">
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  <AddressBlock title="Billing" a={data.billing} />
                  <AddressBlock title="Shipping" a={data.shipping} />
                </div>
              </Section>
              {data.customFields.length > 0 && (
                <Section title="Call fields">
                  <Fields rows={data.customFields.map((f) => [f.label, f.value] as [string, ReactNode])} />
                </Section>
              )}
            </>
          )}
          {data && tab === 'interactions' && (
            data.interactions.length === 0
              ? <p className="text-sm text-gray-500">No agent interaction on this call.</p>
              : data.interactions.map((ix) => <InteractionCard key={ix.number} ix={ix} multiple={multiple} />)
          )}
          {data && tab === 'captured' && (
            capturedCount === 0 ? <p className="text-sm text-gray-500">No values were captured on this call.</p> : data.interactions.map((ix) => (
              <div key={ix.number} className="mb-5">
                {multiple && <div className="text-sm text-white font-medium mb-2">Interaction #{ix.number} · {ix.campaignName}</div>}
                {ix.fields && Object.keys(ix.fields).length > 0 && (
                  <Section title="Fields recorded">
                    <Fields rows={Object.entries(ix.fields).map(([k, v]) => [humanize(k), String(v ?? '')] as [string, ReactNode])} />
                  </Section>
                )}
                {ix.captured.filter((c) => Object.keys(c.values).length > 0).map((c, i) => (
                  <Section key={i} title={`Script${c.flowName ? ` — ${c.flowName}` : ''}`}>
                    <Fields rows={Object.entries(c.values).sort(([a], [b]) => a.localeCompare(b)).map(([k, v]) => [humanize(k), v] as [string, ReactNode])} />
                  </Section>
                ))}
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  )
}
