import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import PortalShell from '../../components/portal/PortalShell'
import {
  invoiceLabel, invoicesApi, money, periodLabel, STATUS_STYLE,
  type InvoiceDetail, type InvoiceLineKind,
} from '../../api/invoices'

// One invoice or credit note (S179, Sprint 1 item 2). Drafts are editable (usage refresh, hand-added lines, notes) and
// get issued from here; issued ones can be marked paid, voided (unpaid only) or credited. The preview is the exact HTML
// document that's emailed, and prints from here.

const ADDABLE: { kind: InvoiceLineKind; label: string }[] = [
  { kind: 'setup_fee', label: 'Setup fee' },
  { kind: 'adjustment', label: 'Adjustment (+/−)' },
  { kind: 'credit', label: 'Credit' },
]

const inputCls = 'bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500'

export default function InvoiceDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const [inv, setInv] = useState<InvoiceDetail | null>(null)
  const [html, setHtml] = useState<string>('')
  const [error, setError] = useState<string | null>(null)
  const [msg, setMsg] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const frame = useRef<HTMLIFrameElement>(null)

  // add-line form
  const [kind, setKind] = useState<InvoiceLineKind>('setup_fee')
  const [description, setDescription] = useState('Implementation fee')
  const [quantity, setQuantity] = useState('1')
  const [unitPrice, setUnitPrice] = useState('')
  const [reason, setReason] = useState('')
  const [notes, setNotes] = useState('')
  // actions on issued invoices
  const [confirmIssue, setConfirmIssue] = useState(false)
  const [paidRef, setPaidRef] = useState('')
  const [voidReason, setVoidReason] = useState('')
  const [creditAmount, setCreditAmount] = useState('')
  const [creditReason, setCreditReason] = useState('')
  const [panel, setPanel] = useState<'paid' | 'void' | 'credit' | null>(null)

  async function refresh(_next?: InvoiceDetail) {
    const fresh = await invoicesApi.get(id)
    setInv(fresh)
    setNotes(fresh.notes ?? '')
    setHtml(await invoicesApi.document(id))
  }

  useEffect(() => { refresh().catch((e: Error) => setError(e.message)) }, [id]) // eslint-disable-line react-hooks/exhaustive-deps

  async function act(fn: () => Promise<InvoiceDetail | void>, done?: string) {
    setBusy(true); setError(null); setMsg(null)
    try {
      const r = await fn()
      await refresh(r ?? undefined)
      if (done) setMsg(done)
    } catch (e) { setError(e instanceof Error ? e.message : 'Something went wrong.') } finally { setBusy(false) }
  }

  if (!inv) {
    return <PortalShell><div className="p-8 text-gray-400 text-sm">{error ?? 'Loading…'}</div></PortalShell>
  }

  const draft = inv.status === 'draft'
  const isCredit = inv.kind === 'credit_note'
  const addable = isCredit ? ADDABLE.filter((a) => a.kind === 'credit') : ADDABLE

  return (
    <PortalShell>
      <div className="max-w-6xl mx-auto px-6 py-8 space-y-6">
        <div>
          <Link to={`/portal/tenants/${inv.tenantId}`} className="text-indigo-400 hover:text-indigo-300 text-sm">← Tenant</Link>
          <div className="flex flex-wrap items-center gap-3 mt-2">
            <h1 className="text-white text-xl font-semibold">{isCredit ? 'Credit note' : 'Invoice'} {invoiceLabel(inv)}</h1>
            <span className={`text-xs px-2 py-0.5 rounded border ${STATUS_STYLE[inv.status]}`}>{inv.status}</span>
            <span className="text-gray-400 text-sm">{periodLabel(inv)}</span>
            <span className="ml-auto text-white text-xl font-mono">{money(inv.total)}</span>
          </div>
          {inv.creditsInvoiceId && (
            <Link to={`/portal/invoices/${inv.creditsInvoiceId}`} className="text-xs text-indigo-400 hover:text-indigo-300">
              Credits invoice {inv.creditsNumber ?? ''} →
            </Link>
          )}
          {isCredit && draft && (
            <div className="mt-3 rounded-lg border border-amber-800 bg-amber-950/40 px-4 py-2 text-sm text-amber-200">
              Not applied yet — issue this credit note (Actions below) to credit invoice {inv.creditsNumber ?? ''}.
            </div>
          )}
          {!isCredit && (inv.creditNotes?.length ?? 0) > 0 && (
            <div className="mt-3 rounded-lg border border-gray-800 bg-gray-900 px-4 py-2 text-sm space-y-1">
              {inv.creditNotes!.map((c) => (
                <div key={c.id} className="flex items-center gap-3">
                  <Link to={`/portal/invoices/${c.id}`} className="text-indigo-400 hover:text-indigo-300 font-mono text-xs">
                    {c.number ?? 'Credit note (draft)'}
                  </Link>
                  <span className={`text-[11px] px-1.5 py-0.5 rounded border ${STATUS_STYLE[c.status]}`}>{c.status}</span>
                  <span className="font-mono text-gray-300">{money(c.total)}</span>
                  {c.status === 'draft' && <span className="text-xs text-amber-300">not applied until issued</span>}
                </div>
              ))}
              {(inv.credited ?? 0) !== 0 && (
                <p className="text-gray-300 pt-1">
                  Credited <span className="font-mono">{money(inv.credited!)}</span> · Net <span className="font-mono text-white">{money(inv.net!)}</span>
                </p>
              )}
            </div>
          )}
          {inv.billToEmail && <p className="text-gray-500 text-xs mt-1">Sent to {inv.billToEmail}</p>}
          {inv.voidReason && <p className="text-red-300 text-xs mt-1">Voided: {inv.voidReason}</p>}
          {inv.paymentReference && <p className="text-emerald-300 text-xs mt-1">Payment: {inv.paymentReference}</p>}
          {inv.paymentState === 'processing' && <p className="text-sky-300 text-xs mt-1">Stripe payment processing (ACH clears in about 4 business days).</p>}
          {inv.paymentState === 'failed' && <p className="text-red-300 text-xs mt-1">Stripe payment failed{inv.paymentError ? `: ${inv.paymentError}` : ''}</p>}
          {inv.paymentState === 'disputed' && <p className="text-amber-300 text-xs mt-1">{inv.paymentError ?? 'Payment disputed'}</p>}
        </div>

        {error && <div className="rounded-lg border border-red-800 bg-red-950/50 px-4 py-2 text-sm text-red-300">{error}</div>}
        {msg && <div className="rounded-lg border border-emerald-800 bg-emerald-950/40 px-4 py-2 text-sm text-emerald-300">{msg}</div>}

        <div className="grid grid-cols-1 lg:grid-cols-[1fr_minmax(0,1.1fr)] gap-6">
          {/* Lines + actions */}
          <div className="space-y-6">
            <section className="bg-gray-900 border border-gray-800 rounded-xl p-5">
              <div className="flex items-center justify-between mb-3">
                <h2 className="text-white text-sm font-semibold">Lines</h2>
                {draft && inv.periodStart && !isCredit && (
                  <button disabled={busy} onClick={() => void act(() => invoicesApi.refreshUsage(id), 'Usage refreshed from the meter.')}
                    className="text-xs text-indigo-400 hover:text-indigo-300 disabled:opacity-50">Refresh usage</button>
                )}
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <tbody>
                    {inv.lines.map((l) => (
                      <tr key={l.id} className="border-b border-gray-800/60 align-top">
                        <td className="py-2 pr-3">
                          <p className="text-gray-200">{l.description}</p>
                          <p className="text-[11px] text-gray-500">
                            {l.kind.replace('_', ' ')} · {l.quantity} × {l.metered && l.kind !== 'minimum' ? `$${l.unitPrice.toFixed(4)}` : money(l.unitPrice)}
                            {l.reason ? ` · ${l.reason}` : ''}
                          </p>
                        </td>
                        <td className="py-2 pr-2 text-right font-mono text-gray-200 whitespace-nowrap">{money(l.amount)}</td>
                        <td className="py-2 w-6 text-right">
                          {draft && !l.metered && (
                            <button disabled={busy} title="Remove line" onClick={() => void act(() => invoicesApi.removeLine(id, l.id))}
                              className="text-gray-500 hover:text-red-400">×</button>
                          )}
                        </td>
                      </tr>
                    ))}
                    {inv.lines.length === 0 && (
                      <tr><td className="py-3 text-gray-500 text-sm">No lines — {inv.periodStart ? 'no billable usage this month.' : 'add one below.'}</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </section>

            {draft && (
              <section className="bg-gray-900 border border-gray-800 rounded-xl p-5 space-y-3">
                <h2 className="text-white text-sm font-semibold">Add a line</h2>
                <div className="flex flex-wrap gap-2">
                  <select value={kind} onChange={(e) => {
                    const k = e.target.value as InvoiceLineKind
                    setKind(k)
                    setDescription(k === 'setup_fee' ? 'Implementation fee' : '')
                  }} className={inputCls}>
                    {addable.map((a) => <option key={a.kind} value={a.kind}>{a.label}</option>)}
                  </select>
                  <input value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Description" className={`${inputCls} flex-1 min-w-48`} />
                </div>
                <div className="flex flex-wrap gap-2 items-center">
                  <label className="text-xs text-gray-400">Qty</label>
                  <input value={quantity} onChange={(e) => setQuantity(e.target.value)} className={`${inputCls} w-20`} />
                  <label className="text-xs text-gray-400">Unit price</label>
                  <input value={unitPrice} onChange={(e) => setUnitPrice(e.target.value)} placeholder="0.00" className={`${inputCls} w-28`} />
                  {(kind === 'adjustment' || kind === 'credit') && (
                    <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Reason (required)" className={`${inputCls} flex-1 min-w-48`} />
                  )}
                </div>
                {kind === 'credit' && <p className="text-[11px] text-gray-500">Enter a credit as a positive amount — it's subtracted.</p>}
                <button disabled={busy || !description.trim() || !unitPrice.trim()}
                  onClick={() => void act(() => invoicesApi.addLine(id, {
                    kind, description, quantity: Number(quantity) || 0, unitPrice: Number(unitPrice) || 0, reason: reason || undefined,
                  }).then((r) => { setUnitPrice(''); setReason(''); return r }))}
                  className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-3 py-1.5 text-sm">
                  Add line
                </button>
              </section>
            )}

            {draft && (
              <section className="bg-gray-900 border border-gray-800 rounded-xl p-5 space-y-3">
                <h2 className="text-white text-sm font-semibold">Notes on the invoice</h2>
                <textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} className={`${inputCls} w-full`}
                  placeholder="Optional — shown under the lines" />
                <button disabled={busy || notes === (inv.notes ?? '')} onClick={() => void act(() => invoicesApi.setNotes(id, notes), 'Notes saved.')}
                  className="border border-gray-700 hover:bg-gray-800 disabled:opacity-40 text-gray-200 rounded-lg px-3 py-1.5 text-sm">Save notes</button>
              </section>
            )}

            <section className="bg-gray-900 border border-gray-800 rounded-xl p-5 space-y-3">
              <h2 className="text-white text-sm font-semibold">Actions</h2>
              {draft && (
                <div className="flex flex-wrap gap-2 items-center">
                  {!confirmIssue ? (
                    <button disabled={busy || inv.lines.length === 0} onClick={() => setConfirmIssue(true)}
                      className="bg-emerald-700 hover:bg-emerald-600 disabled:opacity-50 text-white rounded-lg px-4 py-1.5 text-sm font-medium">
                      Issue {isCredit ? 'credit note' : 'invoice'}…
                    </button>
                  ) : (
                    <>
                      <span className="text-sm text-amber-200">Issue {money(inv.total)}? This numbers it, freezes it and emails the billing contact.</span>
                      <button disabled={busy} onClick={() => {
                        setConfirmIssue(false)
                        void act(async () => {
                          const r = await invoicesApi.issue(id)
                          if (r.emailError) setError(r.emailError)
                          else setMsg(`Issued and emailed to ${r.emailedTo}.`)
                          return r.invoice
                        })
                      }} className="bg-emerald-700 hover:bg-emerald-600 text-white rounded-lg px-3 py-1.5 text-sm">Yes, issue</button>
                      <button onClick={() => setConfirmIssue(false)} className="text-gray-400 hover:text-white text-sm px-2">Cancel</button>
                    </>
                  )}
                  <button disabled={busy} onClick={() => void act(async () => {
                    await invoicesApi.deleteDraft(id)
                    navigate(`/portal/tenants/${inv.tenantId}`)
                  })} className="ml-auto text-xs text-gray-400 hover:text-red-400">Delete draft</button>
                </div>
              )}

              {inv.status === 'issued' && (
                <div className="flex flex-wrap gap-2">
                  {!isCredit && <button onClick={() => setPanel(panel === 'paid' ? null : 'paid')} className="border border-emerald-800 text-emerald-300 hover:bg-emerald-950/40 rounded-lg px-3 py-1.5 text-sm">Mark paid</button>}
                  {!isCredit && <button onClick={() => setPanel(panel === 'credit' ? null : 'credit')} className="border border-gray-700 text-gray-200 hover:bg-gray-800 rounded-lg px-3 py-1.5 text-sm">Credit note</button>}
                  <button onClick={() => setPanel(panel === 'void' ? null : 'void')} className="border border-red-900 text-red-300 hover:bg-red-950/40 rounded-lg px-3 py-1.5 text-sm">Void</button>
                </div>
              )}
              {inv.status === 'paid' && !isCredit && (
                <button onClick={() => setPanel(panel === 'credit' ? null : 'credit')} className="border border-gray-700 text-gray-200 hover:bg-gray-800 rounded-lg px-3 py-1.5 text-sm">Credit note</button>
              )}
              {(inv.status === 'void' || (inv.status === 'paid' && isCredit)) && <p className="text-gray-500 text-sm">Nothing more to do here.</p>}

              {panel === 'paid' && (
                <div className="flex flex-wrap gap-2 items-center">
                  <input value={paidRef} onChange={(e) => setPaidRef(e.target.value)} placeholder="Payment reference (e.g. ACH trace)" className={`${inputCls} flex-1 min-w-56`} />
                  <button disabled={busy} onClick={() => void act(() => invoicesApi.markPaid(id, null, paidRef), 'Marked paid.')}
                    className="bg-emerald-700 hover:bg-emerald-600 text-white rounded-lg px-3 py-1.5 text-sm">Mark paid today</button>
                </div>
              )}
              {panel === 'void' && (
                <div className="flex flex-wrap gap-2 items-center">
                  <input value={voidReason} onChange={(e) => setVoidReason(e.target.value)} placeholder="Why it's being voided (required)" className={`${inputCls} flex-1 min-w-56`} />
                  <button disabled={busy || !voidReason.trim()} onClick={() => void act(() => invoicesApi.void(id, voidReason), 'Voided.')}
                    className="bg-red-800 hover:bg-red-700 disabled:opacity-50 text-white rounded-lg px-3 py-1.5 text-sm">Void</button>
                </div>
              )}
              {panel === 'credit' && (
                <div className="flex flex-wrap gap-2 items-center">
                  <input value={creditAmount} onChange={(e) => setCreditAmount(e.target.value)} placeholder="Amount" className={`${inputCls} w-28`} />
                  <input value={creditReason} onChange={(e) => setCreditReason(e.target.value)} placeholder="Reason (required)" className={`${inputCls} flex-1 min-w-56`} />
                  <button disabled={busy || !creditAmount.trim() || !creditReason.trim()} onClick={() => void act(async () => {
                    const note = await invoicesApi.creditNote(id, Number(creditAmount) || 0, '', creditReason)
                    navigate(`/portal/invoices/${note.id}`)
                  })} className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-3 py-1.5 text-sm">Create credit note draft</button>
                </div>
              )}
            </section>
          </div>

          {/* The document itself — what's emailed, and printable */}
          <section className="bg-gray-900 border border-gray-800 rounded-xl p-4 flex flex-col">
            <div className="flex items-center justify-between mb-3">
              <h2 className="text-white text-sm font-semibold">Preview</h2>
              <button onClick={() => frame.current?.contentWindow?.print()}
                className="border border-gray-700 hover:bg-gray-800 text-gray-200 rounded-lg px-3 py-1 text-sm">Print / PDF</button>
            </div>
            <iframe ref={frame} title="Invoice" srcDoc={html} className="w-full flex-1 min-h-[640px] rounded-lg bg-white" />
          </section>
        </div>
      </div>
    </PortalShell>
  )
}
