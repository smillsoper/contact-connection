import CampaignCredentialCards from './CampaignCredentialCards'

// Campaign settings → Payment Gateways (S169). One card per registered gateway — see CampaignCredentialCards.
export default function PaymentGatewaysForm({ campaignId }: { campaignId: string }) {
  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
      <h2 className="text-white text-sm font-semibold mb-1">Payment Gateways</h2>
      <p className="text-xs text-gray-500 mb-5">
        Credentials the <span className="font-mono">authorize_payment</span> node uses for this campaign. A field left
        unset here falls back to the client-wide value, then the tenant default. Values are stored encrypted and are
        never shown again after saving.
      </p>
      <CampaignCredentialCards campaignId={campaignId} section="payment-gateways" />
    </div>
  )
}
