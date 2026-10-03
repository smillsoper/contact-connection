import { useEffect } from 'react'
import PublicLayout, { COMPANY } from './PublicLayout'

const FEATURES: { title: string; body: string }[] = [
  {
    title: 'Agent desktop & guided scripts',
    body: 'A three-panel workspace — softphone, scripted call flow and team chat. Visual flow designer, branching, data capture with validation, and scripts that pick up exactly where an agent left off.',
  },
  {
    title: 'Telephony & intelligent routing',
    body: 'Browser-based softphone, IVR and call flows, skills and tiered routing that offers calls to your best agents first, callbacks, recording, and live supervisor monitoring, coaching and barge.',
  },
  {
    title: 'Orders & payments',
    body: 'Product catalogs and offers, carts with tax and shipping, payment authorization, and order submission to your systems. Card numbers are entered on the phone keypad, never seen by the agent.',
  },
  {
    title: 'Media attribution',
    body: 'Every call attributed to the media buy that generated it — national and local stations, with local calls matched to the station nearest the caller — ready for agency reporting.',
  },
  {
    title: 'Commissions',
    body: 'Rules per client and campaign, pay periods, an agent view of their own earnings, and supervisor reports — including retroactive changes handled cleanly, with a full audit trail.',
  },
  {
    title: 'Dashboards & call records',
    body: 'Live supervisor dashboards, every call\'s complete record and history, post-call corrections and resubmission, and exports for the people who need the data.',
  },
]

export default function LandingPage() {
  useEffect(() => { document.title = 'ContactConnection — the contact center platform' }, [])

  return (
    <PublicLayout>
      {/* Hero */}
      <section className="relative overflow-hidden">
        <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_top,rgba(14,165,233,0.18),transparent_60%)]" />
        <div className="relative max-w-6xl mx-auto px-6 pt-24 pb-20 text-center">
          <p className="text-sky-400 text-sm font-medium tracking-wide uppercase">Hosted contact center platform</p>
          <h1 className="mt-4 text-4xl sm:text-5xl font-bold text-white tracking-tight leading-tight">
            Built by contact center people,<br className="hidden sm:block" /> for the way contact centers really work.
          </h1>
          <p className="mt-6 max-w-2xl mx-auto text-lg text-gray-400">
            ContactConnection brings telephony, scripted agent workflows, orders and payments, media attribution,
            commissions and supervisor tools into one platform — for businesses running customer service and sales lines.
          </p>
          <div className="mt-10 flex flex-wrap justify-center gap-3">
            <a href="#platform" className="px-6 py-3 text-sm font-medium text-white bg-sky-600 hover:bg-sky-500 rounded-lg">See the platform</a>
          </div>
          <p className="mt-6 inline-block text-sm text-amber-200 bg-amber-950/40 border border-amber-900/60 rounded-full px-4 py-1.5">
            Currently in a limited pilot — not accepting new customers yet.
          </p>
        </div>
      </section>

      {/* Platform */}
      <section id="platform" className="max-w-6xl mx-auto px-6 py-16 scroll-mt-20">
        <h2 className="text-2xl font-semibold text-white text-center">One platform, the whole call</h2>
        <p className="mt-3 text-center text-gray-400 max-w-2xl mx-auto">
          From the moment a call arrives to the commission it earns and the report it lands in.
        </p>
        <div className="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {FEATURES.map((f) => (
            <div key={f.title} className="bg-gray-900 border border-gray-800 rounded-xl p-6">
              <h3 className="text-white font-medium">{f.title}</h3>
              <p className="mt-2 text-sm text-gray-400 leading-relaxed">{f.body}</p>
            </div>
          ))}
        </div>
      </section>

      {/* About */}
      <section id="about" className="max-w-6xl mx-auto px-6 py-16 scroll-mt-20">
        <div className="grid gap-10 lg:grid-cols-2 items-start">
          <div>
            <h2 className="text-2xl font-semibold text-white">About us</h2>
            <p className="mt-4 text-gray-400 leading-relaxed">
              {COMPANY.product} is built and operated by {COMPANY.legalName}, based in Roseburg, Oregon. Our team has
              spent years running and building software for direct-response and customer-service contact centers, and
              we built {COMPANY.product} around what those operations actually need day to day.
            </p>
            <p className="mt-4 text-gray-400 leading-relaxed">
              Each customer runs in its own isolated environment, with its own phone numbers, users and data. We onboard
              every customer directly and review how they intend to use the platform before their lines go live.
            </p>
          </div>
          <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
            <h3 className="text-white font-medium">Responsible calling</h3>
            <ul className="mt-3 space-y-2 text-sm text-gray-400 leading-relaxed list-disc pl-5">
              <li>Customers must comply with the TCPA, the Telemarketing Sales Rule and state telemarketing laws, including consent and Do Not Call requirements.</li>
              <li>Outbound caller ID is limited to numbers the customer owns or has verified.</li>
              <li>Outbound dialing campaigns are enabled only after we review the customer's use case and consent practices.</li>
              <li>We respond to traceback requests and suspend customers whose calling generates complaints or abuse.</li>
              <li>Callers can always choose to talk to a person — any automated agent must say it's automated and hand off on request.</li>
              <li>Account decisions are made by people, never by automation alone, and every customer can ask for a human review.</li>
            </ul>
            <p className="mt-4 text-xs text-gray-500">
              See our <a href="/acceptable-use" className="text-sky-400 hover:text-sky-300">Acceptable Use Policy</a>. Report abuse to {COMPANY.abuseEmail}.
            </p>
          </div>
        </div>
      </section>

      {/* Contact */}
      <section id="contact" className="max-w-6xl mx-auto px-6 py-16 scroll-mt-20">
        <div className="bg-gradient-to-br from-sky-950/60 to-gray-900 border border-sky-900/50 rounded-2xl p-10 text-center">
          <h2 className="text-2xl font-semibold text-white">Currently in a limited pilot</h2>
          <p className="mt-3 text-gray-400">
            ContactConnection is in a limited pilot and isn't taking on new customers yet. For questions or availability
            updates, email us.
          </p>
          <a href={`mailto:${COMPANY.email}`} className="inline-block mt-6 px-6 py-3 text-sm font-medium text-white bg-sky-600 hover:bg-sky-500 rounded-lg">
            {COMPANY.email}
          </a>
          <p className="mt-6 text-sm text-gray-500">{COMPANY.legalName} · {COMPANY.street}, {COMPANY.cityStateZip}</p>
        </div>
      </section>
    </PublicLayout>
  )
}
