import { forwardRef, useEffect, useImperativeHandle, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'

/**
 * Port-in authorization (S184) — public, no sign-in. The owner of the account with the current phone company opens the
 * emailed link (token in the URL fragment, never sent to a server log), fills in SignalWire's letter of authorization,
 * uploads a recent bill, and signs. ContactConnection's porting desk takes it from there.
 */

interface PrevDetails {
  authorizedName: string; authorizedTitle: string | null; billingName: string; accountNumber: string | null; billingPhone: string
  currentProvider: string | null; longDistanceProvider: string | null; serviceStreet: string; serviceUnit: string | null; serviceCity: string
  serviceState: string; serviceZip: string; mailingAddress: string | null; alternateContact: string | null
}
interface View {
  reference: string; kind: 'local' | 'toll_free'; numbers: string[]; endUserName: string; accountType: string
  currentProvider: string | null; tenantName: string; requestedByName: string; correctionMessage: string | null
  previous: PrevDetails | null; pinNotApplicable: boolean; bill: string | null; tokenExpiresAt: string
}

const EMPTY: PrevDetails = {
  authorizedName: '', authorizedTitle: '', billingName: '', accountNumber: '', billingPhone: '', currentProvider: '', longDistanceProvider: '',
  serviceStreet: '', serviceUnit: '', serviceCity: '', serviceState: '', serviceZip: '', mailingAddress: '', alternateContact: '',
}

const input = 'w-full bg-white border border-gray-300 rounded-md px-3 py-2 text-sm text-gray-900 focus:outline-none focus:ring-2 focus:ring-blue-500'

async function post<T>(path: string, body: unknown): Promise<T> {
  const r = await fetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
  const data = await r.json().catch(() => null)
  if (!r.ok) throw new Error(data?.error ?? `Something went wrong (${r.status}).`)
  return data as T
}

export default function PortSignPage() {
  const [token] = useState(() => new URLSearchParams(window.location.hash.replace(/^#/, '')).get('t') ?? '')
  const [view, setView] = useState<View | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [d, setD] = useState<PrevDetails>(EMPTY)
  const [pin, setPin] = useState('')
  const [noPin, setNoPin] = useState(false)
  const [bill, setBill] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)
  const [typed, setTyped] = useState('')
  const [consent, setConsent] = useState(false)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const pad = useRef<SignaturePadHandle>(null)

  useEffect(() => {
    if (!token) { setError('This link is incomplete. Open it again from the email.'); return }
    post<View>('/api/v1/porting/sign/view', { token })
      .then((v) => {
        setView(v); setBill(v.bill); setNoPin(v.pinNotApplicable)
        setD({ ...EMPTY, ...(v.previous ?? {}), currentProvider: v.previous?.currentProvider ?? v.currentProvider ?? '' } as PrevDetails)
      })
      .catch((e: Error) => setError(e.message))
  }, [token])

  const set = (k: keyof PrevDetails) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setD({ ...d, [k]: e.target.value })

  async function upload(file: File) {
    setUploading(true); setFormError(null)
    try {
      const r = await fetch(`/api/v1/porting/sign/bill?name=${encodeURIComponent(file.name)}`, {
        method: 'POST', body: file, headers: { 'X-Port-Token': token, 'Content-Type': file.type || 'application/octet-stream' },
      })
      const data = await r.json().catch(() => null)
      if (!r.ok) throw new Error(data?.error ?? 'Upload failed.')
      setBill(data.name)
    } catch (e) { setFormError(e instanceof Error ? e.message : 'Upload failed.') }
    finally { setUploading(false) }
  }

  async function submit() {
    setFormError(null)
    const signaturePng = pad.current?.toPng()
    if (!signaturePng) { setFormError('Draw your signature in the box.'); return }
    setBusy(true)
    try {
      await post('/api/v1/porting/sign/submit', { token, details: d, pin: noPin ? null : pin, pinNotApplicable: noPin, signatureName: typed, signaturePng, consent })
      setDone(true)
      window.scrollTo(0, 0)
    } catch (e) { setFormError(e instanceof Error ? e.message : 'Something went wrong.') }
    finally { setBusy(false) }
  }

  const tf = view?.kind === 'toll_free'
  return (
    <div className="min-h-screen bg-gray-100 py-8 px-4">
      <div className="max-w-2xl mx-auto bg-white rounded-xl shadow-sm border border-gray-200 p-6 sm:p-8 text-gray-900">
        <div className="flex items-center gap-2 mb-5">
          <img src="/cc-navbar-light.svg" alt="ContactConnection" className="h-8 w-auto" />
          {view && <span className="ml-auto text-xs text-gray-500">{view.reference}</span>}
        </div>

        {error && <p className="text-red-600">{error}</p>}
        {!view && !error && <p className="text-gray-500">Loading…</p>}

        {done && (
          <div>
            <h1 className="text-xl font-semibold mb-2">Thank you — it's signed</h1>
            <p className="text-gray-700">We've emailed you a copy of the signed letter of authorization and the record of your signature.
              <b> Keep your service with your current phone company until the move is complete</b> — cancelling early can lose the numbers.</p>
          </div>
        )}

        {view && !done && (
          <>
            <h1 className="text-xl font-semibold">Authorize moving your phone numbers</h1>
            <p className="text-sm text-gray-600 mt-1">
              {view.requestedByName} at {view.tenantName} is moving these {tf ? 'toll-free' : ''} numbers to ContactConnection, whose carrier is
              SignalWire. As the owner of the account with the current phone company, fill in SignalWire's letter of authorization and sign it.
            </p>
            {view.correctionMessage && (
              <p className="mt-4 bg-amber-50 border border-amber-300 text-amber-900 rounded-md px-3 py-2 text-sm"><b>Please correct:</b> {view.correctionMessage}</p>
            )}
            <div className="mt-4 bg-gray-50 border border-gray-200 rounded-md p-3 text-sm">
              <div className="text-gray-500 text-xs mb-1">{view.numbers.length} number{view.numbers.length === 1 ? '' : 's'}</div>
              <div className="font-mono text-gray-800 max-h-28 overflow-y-auto">{view.numbers.join(', ')}</div>
            </div>
            <p className="mt-4 text-sm font-medium text-red-700">Enter everything exactly as it appears on your bill from the current phone company.</p>

            <Section title="The account">
              <Field label="Account owner's legal first and last name" hint="A person's name, not a company — the person signing below.">
                <input className={input} value={d.authorizedName} onChange={set('authorizedName')} autoComplete="name" />
              </Field>
              {tf && <Field label="Your title"><input className={input} value={d.authorizedTitle ?? ''} onChange={set('authorizedTitle')} /></Field>}
              <Field label="Billing name on the account" hint="Exactly as on the bill.">
                <input className={input} value={d.billingName} onChange={set('billingName')} />
              </Field>
              <div className="grid sm:grid-cols-2 gap-4">
                <Field label="Account number" hint="From the bill (leave blank if there isn't one).">
                  <input className={input} value={d.accountNumber ?? ''} onChange={set('accountNumber')} />
                </Field>
                <Field label="Billing telephone number" hint="The main number on the account.">
                  <input className={input} value={d.billingPhone} onChange={set('billingPhone')} inputMode="tel" />
                </Field>
              </div>
              <div className="grid sm:grid-cols-2 gap-4">
                <Field label="Current phone company"><input className={input} value={d.currentProvider ?? ''} onChange={set('currentProvider')} /></Field>
                {!tf && <Field label="Long-distance company" hint="Only if different."><input className={input} value={d.longDistanceProvider ?? ''} onChange={set('longDistanceProvider')} /></Field>}
              </div>
              <Field label="Account PIN or passcode" hint="Often required (Verizon Wireless, MagicJack, Twilio…). Kept encrypted and deleted after the move.">
                <div className="flex items-center gap-3">
                  <input className={`${input} max-w-[12rem]`} value={pin} onChange={(e) => setPin(e.target.value)} disabled={noPin} autoComplete="off" />
                  <label className="flex items-center gap-2 text-sm text-gray-700"><input type="checkbox" checked={noPin} onChange={(e) => setNoPin(e.target.checked)} />The account has no PIN</label>
                </div>
              </Field>
            </Section>

            <Section title="Service address" hint="Where the phones are — not a PO box. If you're unsure, the current phone company can give you a customer service record (CSR).">
              <div className="grid grid-cols-3 gap-4">
                <div className="col-span-2"><Field label="Street"><input className={input} value={d.serviceStreet} onChange={set('serviceStreet')} autoComplete="address-line1" /></Field></div>
                <Field label="Suite / unit"><input className={input} value={d.serviceUnit ?? ''} onChange={set('serviceUnit')} autoComplete="address-line2" /></Field>
              </div>
              <div className="grid grid-cols-6 gap-4">
                <div className="col-span-3"><Field label="City"><input className={input} value={d.serviceCity} onChange={set('serviceCity')} autoComplete="address-level2" /></Field></div>
                <div className="col-span-1"><Field label="State"><input className={input} value={d.serviceState} onChange={set('serviceState')} maxLength={2} autoComplete="address-level1" /></Field></div>
                <div className="col-span-2"><Field label="ZIP"><input className={input} value={d.serviceZip} onChange={set('serviceZip')} autoComplete="postal-code" /></Field></div>
              </div>
              {!tf && (
                <>
                  <Field label="Mailing address" hint="Only if different from the service address."><input className={input} value={d.mailingAddress ?? ''} onChange={set('mailingAddress')} /></Field>
                  <Field label="Another way to reach you" hint="A cell number or email, in case of questions."><input className={input} value={d.alternateContact ?? ''} onChange={set('alternateContact')} /></Field>
                </>
              )}
            </Section>

            <Section title="A recent bill" hint="Required — it must show the account number, billing name and service address. PDF, Word, JPG or PNG, up to 20 MB.">
              <div className="flex items-center gap-3">
                <label className="cursor-pointer bg-gray-100 hover:bg-gray-200 border border-gray-300 rounded-md px-3 py-2 text-sm">
                  {bill ? 'Replace' : 'Upload the bill'}
                  <input type="file" accept=".pdf,.doc,.docx,.jpg,.jpeg,.png" className="hidden"
                    onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ''; if (f) void upload(f) }} />
                </label>
                <span className="text-sm text-gray-600">{uploading ? 'Uploading…' : bill ?? 'No file yet'}</span>
              </div>
            </Section>

            <Section title="Sign">
              <p className="text-xs text-gray-600 leading-relaxed">
                {tf
                  ? 'By signing, you authorize SignalWire, Inc. to act on your behalf to change the Responsible Organization (RespOrg) of the toll-free numbers above to SignalWire (LQX01), and attest that you are the end-user subscriber of these numbers or their authorized representative.'
                  : 'By signing, you authorize SignalWire, Inc. to act on your behalf to port the numbers above from your current phone company, and confirm you have the authority to change the phone service provider of these numbers.'}
                {' '}You must keep your service with the current phone company until the move is complete.
              </p>
              <SignaturePad ref={pad} />
              <Field label="Type your full name to sign" hint="Must match the account owner's name above.">
                <input className={input} value={typed} onChange={(e) => setTyped(e.target.value)} />
              </Field>
              <label className="flex items-start gap-2 text-sm text-gray-700">
                <input type="checkbox" className="mt-1" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
                I agree to sign electronically, and that my electronic signature is the legal equivalent of my handwritten signature.
              </label>
            </Section>

            {formError && <p className="mt-4 text-sm text-red-600">{formError}</p>}
            <button onClick={() => void submit()} disabled={busy || uploading}
              className="mt-5 w-full bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white rounded-md py-3 font-medium">
              {busy ? 'Signing…' : 'Sign and submit'}
            </button>
            <p className="mt-3 text-xs text-gray-500">This link works until {new Date(view.tokenExpiresAt).toLocaleDateString()}.</p>
          </>
        )}
      </div>
    </div>
  )
}

function Section({ title, hint, children }: { title: string; hint?: string; children: React.ReactNode }) {
  return (
    <section className="mt-6 pt-5 border-t border-gray-200 space-y-4">
      <div>
        <h2 className="font-semibold">{title}</h2>
        {hint && <p className="text-xs text-gray-500 mt-0.5">{hint}</p>}
      </div>
      {children}
    </section>
  )
}

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="text-sm font-medium text-gray-800">{label}</span>
      {hint && <span className="block text-xs text-gray-500">{hint}</span>}
      <div className="mt-1">{children}</div>
    </label>
  )
}

interface SignaturePadHandle { toPng: () => string | null }


const SignaturePad = forwardRef<SignaturePadHandle>(function SignaturePad(_, ref) {
  const canvas = useRef<HTMLCanvasElement>(null)
  const drawing = useRef(false)
  const [drawn, setDrawn] = useState(false)

  useEffect(() => {
    const c = canvas.current!
    const ratio = window.devicePixelRatio || 1
    c.width = c.offsetWidth * ratio; c.height = c.offsetHeight * ratio
    const g = c.getContext('2d')!
    g.scale(ratio, ratio); g.lineWidth = 2.2; g.lineCap = 'round'; g.lineJoin = 'round'; g.strokeStyle = '#0b2a6f'
  }, [])

  const point = (e: ReactPointerEvent<HTMLCanvasElement>) => {
    const r = canvas.current!.getBoundingClientRect()
    return [e.clientX - r.left, e.clientY - r.top] as const
  }
  function down(e: ReactPointerEvent<HTMLCanvasElement>) {
    canvas.current!.setPointerCapture(e.pointerId)
    drawing.current = true
    const g = canvas.current!.getContext('2d')!
    const [x, y] = point(e)
    g.beginPath(); g.moveTo(x, y)
  }
  function move(e: ReactPointerEvent<HTMLCanvasElement>) {
    if (!drawing.current) return
    const g = canvas.current!.getContext('2d')!
    const [x, y] = point(e)
    g.lineTo(x, y); g.stroke()
    setDrawn(true)
  }
  function clear() {
    const c = canvas.current!
    c.getContext('2d')!.clearRect(0, 0, c.width, c.height)
    setDrawn(false)
  }
  useImperativeHandle(ref, () => ({ toPng: () => (drawn ? canvas.current!.toDataURL('image/png') : null) }), [drawn])

  return (
    <div>
      <div className="flex items-center justify-between mb-1">
        <span className="text-sm font-medium text-gray-800">Draw your signature</span>
        <button type="button" onClick={clear} className="text-xs text-blue-600 hover:underline">Clear</button>
      </div>
      <canvas ref={canvas} onPointerDown={down} onPointerMove={move} onPointerUp={() => (drawing.current = false)} onPointerLeave={() => (drawing.current = false)}
        className="w-full h-32 border border-gray-300 rounded-md bg-white touch-none cursor-crosshair" />
    </div>
  )
})
