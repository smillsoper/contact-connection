import { useEffect, type ReactNode } from 'react'
import PublicLayout, { COMPANY } from './PublicLayout'

// Legal pages (S171). Starting text for counsel (WLR Law) to review — keep it in step with what the
// platform actually does, and with what we've told carriers (SignalWire STIR/SHAKEN vetting).

const UPDATED = 'October 2, 2026'

function LegalDoc({ title, children }: { title: string; children: ReactNode }) {
  useEffect(() => { document.title = `${title} — ContactConnection` }, [title])
  return (
    <PublicLayout>
      <article className="max-w-3xl mx-auto px-6 pt-16 legal">
        <h1 className="text-3xl font-bold text-white">{title}</h1>
        <p className="mt-2 text-sm text-gray-500">Last updated {UPDATED}</p>
        <div className="mt-8 space-y-5 text-[15px] leading-relaxed text-gray-300">{children}</div>
      </article>
    </PublicLayout>
  )
}

const H = ({ children }: { children: ReactNode }) => <h2 className="pt-4 text-lg font-semibold text-white">{children}</h2>
const UL = ({ children }: { children: ReactNode }) => <ul className="list-disc pl-6 space-y-1.5">{children}</ul>

const Who = () => (
  <p>
    {COMPANY.product} is operated by {COMPANY.legalName} ("we", "us"), {COMPANY.street}, {COMPANY.cityStateZip}.
    Questions: <a href={`mailto:${COMPANY.email}`} className="text-sky-400">{COMPANY.email}</a>.
  </p>
)

export function PrivacyPage() {
  return (
    <LegalDoc title="Privacy Policy">
      <Who />
      <p>
        {COMPANY.product} is a hosted contact center platform used by businesses ("customers") to handle calls with
        their own customers. This policy explains what information we handle and how.
      </p>

      <H>Two kinds of information</H>
      <UL>
        <li><b>Customer account information</b> — the names, email addresses and account details of the businesses that use the platform and their users (agents, supervisors, administrators). We are responsible for this information.</li>
        <li><b>Call and customer-of-customer data</b> — information about the people who call or are called by our customers: phone numbers, call recordings, details captured during a call (such as name, address and order details) and call history. We process this data on our customers' behalf and under their instructions; each customer is responsible for how they collect and use it, and for their own privacy notices.</li>
      </UL>

      <H>How we use information</H>
      <UL>
        <li>To provide, secure, support and improve the platform.</li>
        <li>To route and connect calls, record them where a customer has enabled recording, and keep the records customers rely on.</li>
        <li>To meet legal and carrier obligations, including responding to lawful requests and call traceback requests, and preventing fraud and abuse.</li>
      </UL>
      <p>We do not sell personal information, and we do not use our customers' call data for advertising.</p>

      <H>Payment card data</H>
      <p>
        Where a customer takes card payments, card numbers can be entered by the caller on the phone keypad so agents never
        see or hear them. Card data is encrypted, passed to the customer's payment processor, and deleted once it is no
        longer needed for the transaction. We do not keep full card numbers.
      </p>

      <H>Sharing</H>
      <p>
        We share information only with service providers that help us run the platform (for example telecommunications
        carriers, cloud hosting, email delivery, and services a customer chooses to connect such as payment processors or
        tax services), when a customer directs us to, or when required by law. Service providers may use it only to provide
        their services to us.
      </p>

      <H>Retention</H>
      <p>
        Call data is kept for as long as the customer's configuration and contract provide, then deleted. Account
        information is kept while the account is active and as needed afterwards for legal, tax and dispute purposes.
      </p>

      <H>Security</H>
      <p>
        Each customer's data is kept in its own separate database schema. Data is encrypted in transit, sensitive values
        such as card data and credentials are encrypted at rest, and access is limited by role and logged.
      </p>

      <H>Your choices</H>
      <p>
        If you called or were called by one of our customers, please contact that business about your information; we will
        help them respond. Customer account users can contact us at <a href={`mailto:${COMPANY.email}`} className="text-sky-400">{COMPANY.email}</a> to
        access, correct or delete their account information, subject to legal requirements.
      </p>

      <H>Changes</H>
      <p>We may update this policy; the date above shows the latest version.</p>
    </LegalDoc>
  )
}

export function TermsPage() {
  return (
    <LegalDoc title="Terms of Service">
      <Who />
      <p>
        These terms govern use of the {COMPANY.product} platform. Customers using the platform under a signed agreement
        with us are bound by that agreement; where it conflicts with these terms, the agreement controls.
      </p>

      <H>Accounts</H>
      <p>
        The platform is provided to businesses. Customers are responsible for their users, for keeping credentials secure,
        and for all activity under their account. We may require verification of a customer's identity and business before
        enabling service or specific features.
      </p>

      <H>Customer responsibilities</H>
      <UL>
        <li>Use the platform in compliance with all applicable laws and our <a href="/acceptable-use" className="text-sky-400">Acceptable Use Policy</a>.</li>
        <li>Obtain any consents required for calling, messaging and call recording, and give the notices the law requires.</li>
        <li>Be responsible for the content of calls, scripts and data they place on the platform.</li>
      </UL>

      <H>Telephone numbers and caller ID</H>
      <p>
        Outbound calls may present only telephone numbers the customer owns or is authorized to use. Numbers may be subject
        to carrier rules, including STIR/SHAKEN caller-ID authentication and number porting requirements.
      </p>

      <H>Suspension</H>
      <p>
        We may suspend service, in whole or in part, to protect the platform, our carriers or the public — including for
        suspected unlawful or abusive calling, a traceback or carrier complaint, a security risk, or non-payment. Where
        practical we will give notice first.
      </p>

      <H>Fees</H>
      <p>Fees are set out in the customer's order or agreement.</p>

      <H>Data</H>
      <p>
        Customers own their data. We process it to provide the service as described in our <a href="/privacy" className="text-sky-400">Privacy Policy</a> and
        the customer's agreement.
      </p>

      <H>Disclaimers and liability</H>
      <p>
        Except as stated in a customer's agreement, the platform is provided "as is" without warranties of any kind, and our
        total liability is limited to the fees paid for the service in the twelve months before the claim. Neither party is
        liable for indirect or consequential damages. The platform is not intended for emergency calling.
      </p>

      <H>Governing law</H>
      <p>These terms are governed by the laws of the State of Oregon.</p>

      <H>Changes</H>
      <p>We may update these terms; the date above shows the latest version.</p>
    </LegalDoc>
  )
}

export function AcceptableUsePage() {
  return (
    <LegalDoc title="Acceptable Use Policy">
      <Who />
      <p>
        This policy applies to every customer and user of {COMPANY.product}. It exists to protect the people our customers
        call, our carriers, and the platform.
      </p>

      <H>Calling and messaging</H>
      <p>Customers must comply with all laws that apply to their calls and messages, including:</p>
      <UL>
        <li>the Telephone Consumer Protection Act (TCPA) and FCC rules;</li>
        <li>the Telemarketing Sales Rule (TSR), including calling-time restrictions and call abandonment limits;</li>
        <li>the National Do Not Call Registry, their own internal do-not-call lists, and state telemarketing and do-not-call laws;</li>
        <li>call recording consent and notice laws.</li>
      </UL>

      <H>Not permitted</H>
      <UL>
        <li>Calls or messages without the consent the law requires, including prerecorded or artificial-voice calls without documented prior express written consent where required.</li>
        <li>Presenting a caller ID number the customer does not own or is not authorized to use, or any caller-ID spoofing.</li>
        <li>Fraud, scams, phishing, impersonation, or deceptive practices.</li>
        <li>Harassment, threats, or abusive calling.</li>
        <li>Calling numbers on the National Do Not Call Registry or an internal do-not-call list without a lawful exemption.</li>
        <li>Attempting to bypass platform limits, security controls, or carrier authentication.</li>
        <li>Unlawful content, or use that infringes others' rights.</li>
      </UL>

      <H>Outbound dialing campaigns</H>
      <p>
        Automated outbound dialing (progressive or predictive) is enabled only after we review the customer's use case and
        consent practices. Dialing lists must be scrubbed against the National Do Not Call Registry and the customer's
        internal list, calls are limited to permitted hours in the called party's time zone, and predictive campaigns must
        keep call abandonment within the legal limit.
      </p>

      <H>Enforcement</H>
      <p>
        We monitor platform traffic and respond to carrier reports and traceback requests. We may investigate, require
        changes, or suspend or terminate a customer's service for any violation of this policy.
      </p>

      <H>Reporting abuse</H>
      <p>
        If you received a call you believe violates this policy, email <a href={`mailto:${COMPANY.abuseEmail}`} className="text-sky-400">{COMPANY.abuseEmail}</a> with
        the number that called you and the date and time of the call.
      </p>
    </LegalDoc>
  )
}
