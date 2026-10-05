import { useEffect, useState } from 'react'
import { api } from '../../api/client'
import Keypad from './Keypad'

// Place call (S179, Sprint 1 item 1b, Slice A). Replaces the always-visible number field.
//  • Internal — people whose role is in the softphone internal dial list, with presence; dials their extension.
//  • External — client → the agent's manual outbound campaigns (auto-picked when only one) → number + keypad, or
//    Direct dial when the role allows it. Before an external call it shows the caller ID the call will use
//    ("Calling as … · campaign"), from the same server rule that sets the real caller ID. The server places the
//    call: it rings this softphone (answered automatically) and then the customer.

interface DirectoryEntry {
  agentId: string
  name: string
  extension: string
  stateCode: string
  stateLabel: string
  registered: boolean
}

interface CampaignOption {
  campaignId: string
  name: string
  callerId: string | null
  callerIdDisplay: string | null
  callerIdIsTenantDefault: boolean
  hoursStart: string
  hoursEnd: string
}

interface ClientOption { clientId: string; name: string; campaigns: CampaignOption[] }

interface OutboundOptions {
  clients: ClientOption[]
  canDirectDial: boolean
  directDialCallerId: string | null
  directDialCallerIdDisplay: string | null
}

export interface OutboundDialResult {
  callRecordId: string
  number: string
  callerId: string
  campaignId: string | null
  campaignName: string | null
}

const PRESENCE: Record<string, { dot: string; label?: string }> = {
  available: { dot: 'bg-green-500' },
  on_call: { dot: 'bg-blue-500' },
  acw: { dot: 'bg-purple-500' },
  logged_out: { dot: 'bg-gray-600' },
}

function presenceDot(e: DirectoryEntry) {
  if (!e.registered) return 'bg-gray-600'
  return PRESENCE[e.stateCode]?.dot ?? 'bg-amber-500'
}

function to12h(hhmm: string) {
  const [h, m] = hhmm.split(':').map(Number)
  const suffix = h >= 12 ? 'PM' : 'AM'
  return `${((h + 11) % 12) + 1}:${String(m).padStart(2, '0')} ${suffix}`
}

type Section = 'internal' | 'external' | null
/** null campaign = direct dial */
type Target = { kind: 'campaign'; client: ClientOption; campaign: CampaignOption } | { kind: 'direct' }

export default function PlaceCallPanel({ onInternalDial, onDialed }: {
  onInternalDial: (extension: string, name: string) => void
  onDialed: (result: OutboundDialResult) => void
}) {
  const [open, setOpen] = useState(false)
  const [section, setSection] = useState<Section>(null)
  const [directory, setDirectory] = useState<DirectoryEntry[] | null>(null)
  const [options, setOptions] = useState<OutboundOptions | null>(null)
  const [client, setClient] = useState<ClientOption | null>(null)
  const [target, setTarget] = useState<Target | null>(null)
  const [number, setNumber] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    if (section === 'internal')
      api.get<DirectoryEntry[]>('/api/v1/softphone/internal-directory').then(setDirectory).catch(() => setDirectory([]))
    if (section === 'external')
      api.get<OutboundOptions>('/api/v1/softphone/outbound-options').then(setOptions).catch(() => setOptions(null))
  }, [open, section])

  function close() {
    setOpen(false); setSection(null); setClient(null); setTarget(null); setNumber(''); setError(null)
  }

  function pickClient(c: ClientOption) {
    setClient(c)
    setError(null)
    // Only one manual outbound campaign for this client — no need to ask.
    setTarget(c.campaigns.length === 1 ? { kind: 'campaign', client: c, campaign: c.campaigns[0] } : null)
  }

  async function dial() {
    if (!target || !number.trim()) return
    setBusy(true); setError(null)
    try {
      const result = await api.post<OutboundDialResult>('/api/v1/softphone/outbound-dial', {
        campaignId: target.kind === 'campaign' ? target.campaign.campaignId : null,
        number,
      })
      onDialed(result)
      close()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The call could not be placed.')
    } finally { setBusy(false) }
  }

  if (!open) {
    return (
      <button
        onClick={() => setOpen(true)}
        className="w-full py-2 rounded-lg bg-blue-600 hover:bg-blue-500 text-white text-sm font-medium transition-colors"
      >
        Place call
      </button>
    )
  }

  const preview = target?.kind === 'campaign'
    ? { callerId: target.campaign.callerIdDisplay, label: target.campaign.name, isDefault: target.campaign.callerIdIsTenantDefault,
        hours: `${to12h(target.campaign.hoursStart)} – ${to12h(target.campaign.hoursEnd)}` }
    : target?.kind === 'direct'
      ? { callerId: options?.directDialCallerIdDisplay ?? null, label: 'Direct dial', isDefault: true, hours: '8:00 AM – 9:00 PM' }
      : null

  const header = (label: string, s: Exclude<Section, null>) => (
    <button
      onClick={() => { setSection(section === s ? null : s); setClient(null); setTarget(null); setError(null) }}
      className="w-full flex items-center justify-between px-3 py-2 bg-gray-800 text-left"
    >
      <span className="text-xs font-medium text-gray-200">{label}</span>
      <svg className={`w-3 h-3 text-gray-500 transition-transform ${section === s ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
      </svg>
    </button>
  )

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <p className="text-xs font-medium text-gray-300">Place call</p>
        <button onClick={close} className="text-xs text-gray-500 hover:text-gray-300">Close</button>
      </div>

      <div className="border border-gray-700 rounded-lg overflow-hidden">
        {header('Internal', 'internal')}
        {section === 'internal' && (
          <div className="divide-y divide-gray-700/50 max-h-56 overflow-y-auto bg-gray-900">
            {directory === null && <p className="px-3 py-2 text-xs text-gray-500">Loading…</p>}
            {directory?.length === 0 && <p className="px-3 py-2 text-xs text-gray-500">No one is in the internal dial list.</p>}
            {directory?.map((e) => (
              <button
                key={e.agentId}
                onClick={() => { onInternalDial(e.extension, e.name); close() }}
                disabled={!e.registered}
                className="w-full flex items-center gap-2 px-3 py-2 text-left hover:bg-gray-800 disabled:opacity-50 disabled:hover:bg-transparent"
                title={e.registered ? `Call ${e.name} (ext. ${e.extension})` : `${e.name}'s softphone isn't connected`}
              >
                <span className={`w-2 h-2 rounded-full shrink-0 ${presenceDot(e)}`} />
                <span className="flex-1 min-w-0">
                  <span className="block text-xs text-gray-200 truncate">{e.name}</span>
                  <span className="block text-[10px] text-gray-500">{e.registered ? e.stateLabel : 'Softphone offline'}</span>
                </span>
                <span className="text-[10px] text-gray-500 font-mono">{e.extension}</span>
              </button>
            ))}
          </div>
        )}

        {header('External', 'external')}
        {section === 'external' && (
          <div className="bg-gray-900 p-2 flex flex-col gap-2">
            {options === null && <p className="text-xs text-gray-500">Loading…</p>}

            {/* Choose who you're calling for */}
            {options && !target && !client && (
              <div className="flex flex-col gap-1 max-h-48 overflow-y-auto">
                {options.clients.length === 0 && !options.canDirectDial && (
                  <p className="text-xs text-gray-500">You're not assigned to any manual outbound campaigns.</p>
                )}
                {options.clients.map((c) => (
                  <button key={c.clientId} onClick={() => pickClient(c)}
                    className="text-left px-2 py-1.5 rounded bg-gray-800 hover:bg-gray-700 text-xs text-gray-200">
                    {c.name}
                    <span className="text-gray-500"> · {c.campaigns.length} campaign{c.campaigns.length === 1 ? '' : 's'}</span>
                  </button>
                ))}
                {options.canDirectDial && (
                  <button onClick={() => setTarget({ kind: 'direct' })}
                    className="text-left px-2 py-1.5 rounded border border-dashed border-gray-600 hover:bg-gray-800 text-xs text-gray-300">
                    Direct dial <span className="text-gray-500">· no client or campaign</span>
                  </button>
                )}
              </div>
            )}

            {/* A client with several campaigns — pick one */}
            {options && client && !target && (
              <div className="flex flex-col gap-1">
                <button onClick={() => setClient(null)} className="text-left text-[10px] text-gray-500 hover:text-gray-300">← {client.name}</button>
                {client.campaigns.map((k) => (
                  <button key={k.campaignId} onClick={() => setTarget({ kind: 'campaign', client, campaign: k })}
                    className="text-left px-2 py-1.5 rounded bg-gray-800 hover:bg-gray-700 text-xs text-gray-200">
                    {k.name}
                  </button>
                ))}
              </div>
            )}

            {/* Number + keypad + caller ID preview */}
            {target && preview && (
              <div className="flex flex-col gap-2">
                <button
                  onClick={() => { setTarget(null); if (target.kind === 'campaign' && target.client.campaigns.length === 1) setClient(null) }}
                  className="text-left text-[10px] text-gray-500 hover:text-gray-300"
                >
                  ← {target.kind === 'campaign' ? `${target.client.name} · ${target.campaign.name}` : 'Direct dial'}
                </button>
                <input
                  type="tel"
                  autoFocus
                  value={number}
                  onChange={(e) => setNumber(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter') void dial() }}
                  placeholder="Number to call"
                  className="w-full bg-gray-800 text-white text-sm rounded-lg px-3 py-2 placeholder-gray-600 focus:outline-none focus:ring-1 focus:ring-blue-500"
                />
                <Keypad compact onDigit={(d) => setNumber((n) => n + d)} />
                {preview.callerId ? (
                  <div className="rounded-lg border border-blue-900 bg-blue-950/40 px-2 py-1.5">
                    <p className="text-[11px] text-blue-200">
                      Calling as <span className="font-mono font-semibold">{preview.callerId}</span> · {preview.label}
                    </p>
                    <p className="text-[10px] text-gray-500">
                      {preview.isDefault && target.kind === 'campaign' ? 'Tenant default caller ID (the campaign has none) · ' : ''}
                      Calls allowed {preview.hours}, customer's local time
                    </p>
                  </div>
                ) : (
                  <p className="text-[11px] text-amber-300">No outbound caller ID is set up — ask an administrator.</p>
                )}
                <button
                  onClick={() => void dial()}
                  disabled={busy || !number.trim() || !preview.callerId}
                  className="w-full py-2 rounded-lg bg-green-700 hover:bg-green-600 disabled:bg-gray-700 text-white text-sm font-medium transition-colors"
                >
                  {busy ? 'Placing call…' : 'Call'}
                </button>
              </div>
            )}

            {error && <p className="text-xs text-red-400">{error}</p>}
          </div>
        )}
      </div>
    </div>
  )
}
