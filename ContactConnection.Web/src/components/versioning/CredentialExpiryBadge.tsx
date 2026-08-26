// Shared expiry display for the Admin/Portal Credentials pages — see API_HARDENING_CHECKLIST.md
// Tier 3 "credential expiry tracking/warnings". ExpiresOn is Azure Key Vault's own native secret
// property (SecretProperties.ExpiresOn), not a new field this app invented — SetAsync passes it
// straight through, ListAsync reads it straight back.
const WARNING_WINDOW_DAYS = 30

export type ExpiryLevel = 'none' | 'ok' | 'warning' | 'expired'

export function expiryLevel(expiresOn: string | null): ExpiryLevel {
  if (!expiresOn) return 'none'
  const daysLeft = (new Date(expiresOn).getTime() - Date.now()) / 86_400_000
  if (daysLeft < 0) return 'expired'
  if (daysLeft <= WARNING_WINDOW_DAYS) return 'warning'
  return 'ok'
}

const LEVEL_CLASSES: Record<ExpiryLevel, string> = {
  none: 'text-gray-500',
  ok: 'text-gray-400',
  warning: 'bg-amber-900/50 text-amber-400 px-1.5 py-0.5 rounded',
  expired: 'bg-red-900/50 text-red-400 px-1.5 py-0.5 rounded',
}

export default function CredentialExpiryBadge({ expiresOn }: { expiresOn: string | null }) {
  if (!expiresOn) return <span className="text-gray-600">—</span>

  const level = expiryLevel(expiresOn)
  const label = level === 'expired' ? 'Expired' : level === 'warning' ? 'Expiring soon' : null

  return (
    <span className={LEVEL_CLASSES[level]}>
      {new Date(expiresOn).toLocaleDateString()}
      {label && <span className="ml-1.5 text-[10px] font-medium uppercase tracking-wide">{label}</span>}
    </span>
  )
}
