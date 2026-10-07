import { useEffect, useState } from 'react'
import { exportsApi, type CardDataStatus, type ExportSpec } from '../../../api/exports'

// Card data in a data export (S182) — for campaigns whose orders go out only as files to a fulfillment center that runs the
// cards itself. Platform-enabled per account, permission-gated, FTPS + PGP only, masked previews, wiped once delivered.

const VARIABLES: [string, string][] = [
  ['card.number', 'Full card number'],
  ['card.expiration', 'Expiration as captured (e.g. 1230)'],
  ['card.exp_month / card.exp_year', 'Expiration month (12) and 4-digit year (2030)'],
  ['card.cvv', 'Security code — only if the platform did NOT authorize the card (wiped after any authorization here)'],
  ['card.last4 / card.brand', 'Last four digits; Visa, Mastercard, American Express, Discover…'],
  ['card.zip', 'Billing ZIP captured with the card'],
  ['card.fields.<key>', 'Any captured field by its key in the secure-collect step'],
]

export default function ExportCardDataCard({ spec, onChange }: { spec: ExportSpec; onChange: (s: ExportSpec) => void }) {
  const [status, setStatus] = useState<CardDataStatus | null>(null)
  useEffect(() => { exportsApi.cardDataStatus().then(setStatus).catch(() => setStatus(null)) }, [])

  // Hidden unless the account has card-data exports — or this export already includes card data.
  if (!status || (!status.enabled && !spec.includesCardData)) return null
  const on = !!spec.includesCardData

  return (
    <div className={`border rounded-lg p-4 mb-4 ${on ? 'bg-amber-950/20 border-amber-700' : 'bg-gray-800/60 border-gray-700'}`}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-sm font-semibold text-gray-100">Card data</h2>
        <label className={`flex items-center gap-2 text-sm ${status.canManage ? 'text-gray-200' : 'text-gray-500'}`}>
          <input type="checkbox" checked={on} disabled={!status.canManage || (!status.enabled && !on)}
            onChange={(e) => onChange({ ...spec, includesCardData: e.target.checked })} />
          This file includes card data
        </label>
      </div>
      {!status.canManage && <p className="text-xs text-gray-500 mt-1">Your role can't change card-data exports (exports.card_data).</p>}

      <div className="mt-3 rounded border border-sky-800 bg-sky-950/30 px-3 py-2 text-xs text-sky-200">
        <b>Ask about a payment token first.</b> If the fulfillment center can accept a payment token (for example an
        Authorize.Net customer profile) instead of the card number, no card data has to leave the platform at all — the
        safest option for everyone. Use card data only when they run the cards through their own processor.
      </div>

      {on && (
        <div className="mt-3 space-y-3 text-xs text-gray-300">
          <ul className="list-disc pl-5 space-y-0.5">
            <li><b>Delivery:</b> FTPS only, PGP-encrypted to the recipient's public key — SFTP, email and password zips are refused.</li>
            <li><b>Stored encrypted:</b> the file is kept only encrypted and can't be downloaded; the preview and test files show card data masked (XXXX…1111).</li>
            <li><b>Wiped once delivered:</b> each call's card data is wiped when every delivery target confirms the file.</li>
            <li><b>Campaigns:</b> set card data retention to <i>"When a card-data export delivers it"</i> (Telephony → campaign) so the card is kept until the file goes.</li>
            <li><b>Changes are audited</b> and need vendor re-approval like any layout change.</li>
          </ul>
          <div>
            <div className="text-[10px] uppercase tracking-wide text-gray-500 mb-1">Card variables</div>
            <div className="grid sm:grid-cols-2 gap-x-4 gap-y-0.5">
              {VARIABLES.map(([v, d]) => (
                <div key={v}><code className="text-amber-200">{`{{ ${v} }}`}</code> <span className="text-gray-500">— {d}</span></div>
              ))}
            </div>
          </div>
        </div>
      )}

      {status.waiting > 0 && (
        <p className={`mt-3 text-xs ${status.waitingOverTwoDays ? 'text-red-300' : 'text-gray-400'}`}>
          {status.waiting} call{status.waiting === 1 ? '' : 's'} holding card data for export
          {status.waitingOverTwoDays ? ` — ${status.waitingOverTwoDays} waiting more than 2 days: check this export's deliveries.` : '.'}
        </p>
      )}
    </div>
  )
}
