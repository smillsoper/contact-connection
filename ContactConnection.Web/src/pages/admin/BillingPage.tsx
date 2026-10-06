import { useEffect, useMemo, useRef, useState } from 'react'
import { loadStripe, type Stripe } from '@stripe/stripe-js'
import { Elements, PaymentElement, useElements, useStripe } from '@stripe/react-stripe-js'
import AdminShell from '../../components/admin/AdminShell'
import { billingApi, type PaymentMethodView, type TenantInvoice } from '../../api/billing'

// Billing (S179, Sprint 1 item 4) — the tenant pays ContactConnection here. Card or bank details are entered in Stripe's
// own Payment Element (a Stripe-hosted frame) and never touch our servers; we keep only a label like "Visa •••• 4242".

const money = (n: number) => n.toLocaleString('en-US', { style: 'currency', currency: 'USD' })

function period(i: TenantInvoice) {
  if (!i.periodStart) return 'One-off'
  const [y, m] = i.periodStart.split('-').map(Number)
  return new Date(y, m - 1, 1).toLocaleDateString('en-US', { month: 'long', year: 'numeric' })
}

function statusChip(i: TenantInvoice) {
  if (i.kind === 'credit_note') return { text: 'credit', cls: 'bg-gray-800 text-gray-300 border-gray-700' }
  if (i.status === 'paid') return { text: 'paid', cls: 'bg-emerald-950/50 text-emerald-300 border-emerald-800' }
  if (i.status === 'void') return { text: 'void', cls: 'bg-gray-800 text-gray-400 border-gray-700' }
  if (i.paymentState === 'processing') return { text: 'payment processing', cls: 'bg-sky-950/50 text-sky-300 border-sky-800' }
  if (i.paymentState === 'failed') return { text: 'payment failed', cls: 'bg-red-950/50 text-red-300 border-red-800' }
  if (i.paymentState === 'disputed') return { text: 'disputed', cls: 'bg-amber-950/50 text-amber-300 border-amber-800' }
  return { text: 'due', cls: 'bg-amber-950/50 text-amber-300 border-amber-800' }
}

export default function BillingPage() {
  const [stripePromise, setStripePromise] = useState<Promise<Stripe | null> | null>(null)
  const [configured, setConfigured] = useState<boolean | null>(null)
  const [method, setMethod] = useState<PaymentMethodView | null>(null)
  const [invoices, setInvoices] = useState<TenantInvoice[] | null>(null)
  const [clientSecret, setClientSecret] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [msg, setMsg] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [viewing, setViewing] = useState<{ id: string; html: string } | null>(null)
  // A hand-entered bank account waiting on its micro-deposit check (Stripe's hosted page); it's saved automatically once verified.
  const [verifyUrl, setVerifyUrl] = useState<string | null>(null)
  const frame = useRef<HTMLIFrameElement>(null)

  const load = () => {
    billingApi.paymentMethod().then(setMethod).catch((e: Error) => setError(e.message))
    billingApi.invoices().then(setInvoices).catch((e: Error) => setError(e.message))
  }

  useEffect(() => {
    billingApi.config().then((c) => {
      setConfigured(c.configured && !!c.publishableKey)
      if (c.publishableKey) setStripePromise(loadStripe(c.publishableKey))
    }).catch((e: Error) => setError(e.message))
    load()
  }, [])

  async function startAddMethod() {
    setError(null); setMsg(null); setBusy('setup')
    try { setClientSecret((await billingApi.setupIntent()).clientSecret) }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not start.') }
    finally { setBusy(null) }
  }

  async function toggleAutopay(enabled: boolean) {
    setError(null); setMsg(null)
    try { setMethod(await billingApi.setAutopay(enabled)) } catch (e) { setError(e instanceof Error ? e.message : 'Could not save.') }
  }

  async function pay(i: TenantInvoice) {
    setError(null); setMsg(null); setBusy(i.id)
    try {
      const r = await billingApi.pay(i.id)
      if (r.state === 'paid') setMsg(`Invoice ${i.number} is paid. Thank you!`)
      else if (r.state === 'processing') setMsg(r.message ?? 'Payment submitted.')
      else setError(r.message ?? 'The payment didn’t go through.')
      load()
    } catch (e) { setError(e instanceof Error ? e.message : 'Payment failed.') } finally { setBusy(null) }
  }

  async function view(i: TenantInvoice) {
    try { setViewing({ id: i.id, html: await billingApi.document(i.id) }) } catch (e) { setError(e instanceof Error ? e.message : 'Could not open it.') }
  }

  const due = (invoices ?? []).filter((i) => i.kind === 'invoice' && i.status === 'issued')
  const owedTotal = due.reduce((s, i) => s + i.owed, 0)

  return (
    <AdminShell>
      <div className="max-w-5xl mx-auto px-6 py-8 space-y-6">
        <div>
          <h1 className="text-white text-xl font-semibold">Billing</h1>
          <p className="text-gray-400 text-sm mt-1">Your ContactConnection invoices and how you pay them.</p>
        </div>

        {error && <div className="rounded-lg border border-red-800 bg-red-950/50 px-4 py-2 text-sm text-red-300">{error}</div>}
        {msg && <div className="rounded-lg border border-emerald-800 bg-emerald-950/40 px-4 py-2 text-sm text-emerald-300">{msg}</div>}
        {verifyUrl && (
          <div className="rounded-lg border border-sky-800 bg-sky-950/40 px-4 py-3 text-sm text-sky-200 space-y-1">
            <p>Your bank account needs a quick check: Stripe sends two small deposits (1–2 business days), then you confirm the amounts.</p>
            <p>
              <a href={verifyUrl} target="_blank" rel="noreferrer" className="underline font-medium">Verify your bank account →</a>
              <span className="text-sky-300/70"> · Once verified it becomes your payment method automatically — refresh this page.</span>
            </p>
          </div>
        )}
        {configured === false && (
          <div className="rounded-lg border border-amber-800 bg-amber-950/40 px-4 py-2 text-sm text-amber-200">
            Online payment isn't set up on this server yet — your invoices are still listed below.
          </div>
        )}

        <section className="bg-gray-900 border border-gray-800 rounded-xl p-5 space-y-4">
          <h2 className="text-white text-sm font-semibold">Payment method</h2>
          {method && (
            <div className="flex flex-wrap items-center gap-3">
              <span className="text-gray-200 text-sm">
                {method.configured ? method.label : 'No payment method on file'}
              </span>
              {configured && !clientSecret && (
                <button onClick={() => void startAddMethod()} disabled={busy === 'setup'}
                  className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-3 py-1.5 text-sm">
                  {method.configured ? 'Replace' : 'Add bank account or card'}
                </button>
              )}
            </div>
          )}
          {clientSecret && stripePromise && (
            <Elements stripe={stripePromise} options={{ clientSecret, appearance: { theme: 'night' } }}>
              <SetupForm
                onCancel={() => setClientSecret(null)}
                onSaved={(m) => { setMethod(m); setClientSecret(null); setMsg(`Saved ${m.label}.`) }}
                onError={setError}
                onPendingVerification={(url) => { setClientSecret(null); setError(null); setVerifyUrl(url) }}
              />
            </Elements>
          )}
          {method?.configured && (
            <label className="flex items-start gap-2 text-sm text-gray-300">
              <input type="checkbox" checked={method.autopay} onChange={(e) => void toggleAutopay(e.target.checked)} className="accent-indigo-500 mt-0.5" />
              <span>
                Autopay
                <span className="block text-gray-500 text-xs">Charge {method.label} automatically when an invoice is issued.</span>
              </span>
            </label>
          )}
          <p className="text-[11px] text-gray-500">
            Bank accounts (ACH) take about 4 business days to clear; the invoice shows "payment processing" until then.
            Card and bank details are handled by Stripe — ContactConnection never sees or stores them.
          </p>
        </section>

        <section className="bg-gray-900 border border-gray-800 rounded-xl p-5">
          <div className="flex items-center justify-between mb-3">
            <h2 className="text-white text-sm font-semibold">Invoices</h2>
            {owedTotal > 0 && <span className="text-sm text-amber-200">Balance due {money(owedTotal)}</span>}
          </div>
          {invoices === null && <p className="text-gray-500 text-sm">Loading…</p>}
          {invoices?.length === 0 && <p className="text-gray-500 text-sm">No invoices yet.</p>}
          {invoices && invoices.length > 0 && (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-gray-500 text-xs border-b border-gray-800">
                    <th className="py-2 pr-4 font-medium">Number</th>
                    <th className="py-2 pr-4 font-medium">Period</th>
                    <th className="py-2 pr-4 font-medium">Status</th>
                    <th className="py-2 pr-4 font-medium text-right">Amount</th>
                    <th className="py-2 pr-4 font-medium">Due</th>
                    <th className="py-2 font-medium" />
                  </tr>
                </thead>
                <tbody>
                  {invoices.map((i) => {
                    const chip = statusChip(i)
                    const canPay = configured && method?.configured && i.kind === 'invoice' && i.status === 'issued'
                      && i.owed > 0 && i.paymentState !== 'processing'
                    return (
                      <tr key={i.id} className="border-b border-gray-800/60 align-top">
                        <td className="py-2 pr-4 font-mono text-xs text-gray-200">{i.number}</td>
                        <td className="py-2 pr-4 text-gray-300">{period(i)}</td>
                        <td className="py-2 pr-4">
                          <span className={`text-[11px] px-1.5 py-0.5 rounded border ${chip.cls}`}>{chip.text}</span>
                          {i.paymentState === 'failed' && i.paymentError && <p className="text-[11px] text-red-300 mt-1">{i.paymentError}</p>}
                        </td>
                        <td className="py-2 pr-4 text-right font-mono text-gray-200">
                          {money(i.total)}
                          {i.credited !== 0 && <p className="text-[11px] text-gray-500">owed {money(i.owed)}</p>}
                        </td>
                        <td className="py-2 pr-4 text-gray-400 text-xs">{i.kind === 'invoice' ? i.dueOn ?? '—' : '—'}</td>
                        <td className="py-2 text-right whitespace-nowrap space-x-2">
                          <button onClick={() => void view(i)} className="text-xs text-indigo-400 hover:text-indigo-300">View</button>
                          {canPay && (
                            <button onClick={() => void pay(i)} disabled={busy === i.id}
                              className="bg-emerald-700 hover:bg-emerald-600 disabled:opacity-50 text-white rounded px-2 py-1 text-xs">
                              {busy === i.id ? 'Paying…' : `Pay ${money(i.owed)}`}
                            </button>
                          )}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
        </section>

        {viewing && (
          <div className="fixed inset-0 z-50 bg-black/70 flex items-center justify-center p-4" onClick={() => setViewing(null)}>
            <div className="bg-gray-900 rounded-xl w-full max-w-3xl h-[85vh] flex flex-col" onClick={(e) => e.stopPropagation()}>
              <div className="flex justify-end gap-2 p-3">
                <button onClick={() => frame.current?.contentWindow?.print()} className="border border-gray-700 hover:bg-gray-800 text-gray-200 rounded-lg px-3 py-1 text-sm">Print / PDF</button>
                <button onClick={() => setViewing(null)} className="text-gray-400 hover:text-white text-sm px-2">Close</button>
              </div>
              <iframe ref={frame} title="Invoice" srcDoc={viewing.html} className="flex-1 bg-white rounded-b-xl" />
            </div>
          </div>
        )}
      </div>
    </AdminShell>
  )
}

/** Stripe's Payment Element in SetupIntent mode: bank account (Financial Connections) or card, saved for later charges. */
function SetupForm({ onSaved, onCancel, onError, onPendingVerification }: {
  onSaved: (m: PaymentMethodView) => void
  onCancel: () => void
  onError: (msg: string) => void
  onPendingVerification: (url: string) => void
}) {
  const stripe = useStripe()
  const elements = useElements()
  const [saving, setSaving] = useState(false)
  const returnUrl = useMemo(() => `${window.location.origin}/admin/billing`, [])

  async function save() {
    if (!stripe || !elements) return
    setSaving(true)
    try {
      const { setupIntent, error } = await stripe.confirmSetup({ elements, redirect: 'if_required', confirmParams: { return_url: returnUrl } })
      if (error) { onError(error.message ?? 'Stripe couldn’t save that.'); return }
      if (!setupIntent) { onError('No result from Stripe.'); return }
      if (setupIntent.status === 'requires_action') {
        // Hand-entered bank details: verified by micro-deposits on Stripe's hosted page; the server saves the account when
        // Stripe reports it verified (setup_intent.succeeded).
        const url = setupIntent.next_action?.verify_with_microdeposits?.hosted_verification_url
        if (url) onPendingVerification(url)
        else onError('Your bank account needs verifying — follow the steps Stripe emails you, then refresh this page.')
        return
      }
      onSaved(await billingApi.savePaymentMethod(setupIntent.id))
    } catch (e) { onError(e instanceof Error ? e.message : 'Could not save.') } finally { setSaving(false) }
  }

  return (
    <div className="space-y-3 max-w-xl">
      <PaymentElement />
      <div className="flex gap-2">
        <button onClick={() => void save()} disabled={saving || !stripe}
          className="bg-emerald-700 hover:bg-emerald-600 disabled:opacity-50 text-white rounded-lg px-4 py-1.5 text-sm">
          {saving ? 'Saving…' : 'Save payment method'}
        </button>
        <button onClick={onCancel} className="text-gray-400 hover:text-white text-sm px-2">Cancel</button>
      </div>
    </div>
  )
}
