import { api } from '../../api/client'
import { useEffect, useState, useCallback } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import SearchableSelect from '../../components/SearchableSelect'
import PaymentGatewaysForm from '../../components/admin/PaymentGatewaysForm'
import CampaignCredentialCards from '../../components/admin/CampaignCredentialCards'
import {
  getCampaign, updateCampaign, updateCampaignRecording, updateCampaignSensitiveDataRetention, updateCampaignAiSettings, updateCampaignExternalRouting, setCampaignFlow, removeCampaignFlow,
  updateCampaignTax, type TaxProviderKey, type CampaignTaxSettings, type AvalaraFeeLine,
  setCampaignInboundFlow, removeCampaignInboundFlow,
  setCampaignOutboundFlow, removeCampaignOutboundFlow,
  activateCampaign, pauseCampaign, deactivateCampaign,
  assignCampaignAgent, bulkAssignCampaignAgents,
  updateCampaignAgentProficiency, removeCampaignAgent,
  listExternalNumbers, addExternalNumber, removeExternalNumber,
  setExternalNumberFlow, removeExternalNumberFlow,
  setExternalNumberTelephonyFlow, removeExternalNumberTelephonyFlow,
  type CampaignDetail, type AgentAssignment, type CampaignExternalNumber, type ExternalRoutingAcceptMode,
} from '../../api/telephony'
import { flowsApi, type FlowSummary } from '../../api/flows'
import { listAdminAgents, type AgentRecord } from '../../api/adminAgents'
import { US_STATES } from '../../constants/usStates'

const STATUS_COLORS: Record<string, string> = {
  active:   'bg-emerald-900/50 text-emerald-400',
  paused:   'bg-amber-900/50 text-amber-400',
  inactive: 'bg-gray-700/60 text-gray-400',
}

// ── Settings form ─────────────────────────────────────────────────────────────

interface SettingsFormProps {
  campaign: CampaignDetail
  flows: FlowSummary[]
  onSaved: (updated: CampaignDetail) => void
}

function SettingsForm({ campaign, flows, onSaved }: SettingsFormProps) {
  const [name, setName] = useState(campaign.name)
  const [description, setDescription] = useState(campaign.description ?? '')
  const [direction, setDirection] = useState(campaign.direction)
  const [dialMode, setDialMode] = useState(campaign.dialMode ?? 'manual')
  const [priority, setPriority] = useState(campaign.priority)
  const [acwSeconds, setAcwSeconds] = useState(campaign.afterCallWorkSeconds)
  const [callerIdNumber, setCallerIdNumber] = useState(campaign.callerIdNumber ?? '')
  const [hoursStart, setHoursStart] = useState(campaign.outboundHoursStart ?? '')
  const [hoursEnd, setHoursEnd] = useState(campaign.outboundHoursEnd ?? '')
  const [flowId, setFlowId] = useState(campaign.flowId ?? '')
  const [inboundFlowId, setInboundFlowId] = useState(campaign.inboundFlowId ?? '')
  const [outboundFlowId, setOutboundFlowId] = useState(campaign.outboundFlowId ?? '')
  const [maxQueueSize, setMaxQueueSize] = useState(campaign.maxQueueSize)
  const [queueTimeout, setQueueTimeout] = useState(campaign.queueTimeoutSeconds)
  const [slThreshold, setSlThreshold] = useState(campaign.serviceLevelThresholdSeconds)
  // Was never tracked here at all (no state, no input, no save payload field) — every save
  // silently reset it server-side to the backend request record's default (10). Found while
  // wiring up the ring-strategy fields below; fixed alongside them since it's the same form/payload.
  const [shortAbandonThreshold, setShortAbandonThreshold] = useState(campaign.shortAbandonThresholdSeconds)
  const [accelEnabled, setAccelEnabled] = useState(campaign.queueAccelerationEnabled)
  const [accelInterval, setAccelInterval] = useState(campaign.queueAccelerationIntervalSeconds)
  const [accelBoost, setAccelBoost] = useState(campaign.queueAccelerationPriorityBoost)
  const [ringStrategy, setRingStrategy] = useState(campaign.ringStrategy)
  const [ringTopN, setRingTopN] = useState(campaign.ringTopN)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  async function handleSave() {
    setSaving(true); setSaveError(null); setSaved(false)
    try {
      // Save telephony settings
      const isOutbound = direction === 'outbound'
      const hasQueue = !isOutbound || dialMode === 'progressive' || dialMode === 'predictive'
      const updated = await updateCampaign(campaign.id, {
        name, description: description || undefined, direction,
        dialMode: isOutbound ? dialMode : 'manual',
        priority,
        afterCallWorkSeconds: acwSeconds,
        callerIdNumber: isOutbound ? callerIdNumber || undefined : undefined,
        maxQueueSize: hasQueue ? maxQueueSize : 50,
        queueTimeoutSeconds: hasQueue ? queueTimeout : 300,
        serviceLevelThresholdSeconds: !isOutbound ? slThreshold : 30,
        shortAbandonThresholdSeconds: shortAbandonThreshold,
        queueAccelerationEnabled: hasQueue ? accelEnabled : false,
        queueAccelerationIntervalSeconds: accelInterval,
        queueAccelerationPriorityBoost: accelBoost,
        ringStrategy: hasQueue ? ringStrategy : 'ring_all',
        ringTopN,
      })
      // Manual outbound calling window (S179) — its own endpoint; both blank = the 8 AM – 9 PM default.
      let hours: { outboundHoursStart: string | null; outboundHoursEnd: string | null } | null = null
      if (isOutbound && dialMode === 'manual'
          && (hoursStart !== (campaign.outboundHoursStart ?? '') || hoursEnd !== (campaign.outboundHoursEnd ?? ''))) {
        hours = await api.put(`/api/v1/campaigns/${campaign.id}/outbound-hours`, { start: hoursStart || null, end: hoursEnd || null })
      }
      // Save fallback script flow
      if (flowId && flowId !== campaign.flowId) {
        await setCampaignFlow(campaign.id, flowId)
      } else if (!flowId && campaign.flowId) {
        await removeCampaignFlow(campaign.id)
      }
      // Save inbound call flow (inbound campaigns only)
      if (!isOutbound) {
        if (inboundFlowId && inboundFlowId !== campaign.inboundFlowId) {
          await setCampaignInboundFlow(campaign.id, inboundFlowId)
        } else if (!inboundFlowId && campaign.inboundFlowId) {
          await removeCampaignInboundFlow(campaign.id)
        }
      }
      // Save outbound call flow (manual outbound only)
      if (isOutbound && dialMode === 'manual') {
        if (outboundFlowId && outboundFlowId !== campaign.outboundFlowId) {
          await setCampaignOutboundFlow(campaign.id, outboundFlowId)
        } else if (!outboundFlowId && campaign.outboundFlowId) {
          await removeCampaignOutboundFlow(campaign.id)
        }
      }
      onSaved({ ...campaign, ...updated, ...(hours ?? {}), flowId: flowId || undefined, inboundFlowId: inboundFlowId || undefined, outboundFlowId: outboundFlowId || undefined })
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  const crmFlows          = flows.filter((f) => f.flow_type === 'crm')
  const inboundFlows      = flows.filter(
    (f) => f.flow_type === 'telephony' && f.flow_direction === 'inbound'
  )
  const outboundFlows     = flows.filter(
    (f) => f.flow_type === 'telephony' && f.flow_direction === 'outbound' && f.flow_sub_type === 'manual'
  )
  const flowOptions         = crmFlows.map((f) => ({ value: f.id, label: f.name }))
  const inboundFlowOptions  = inboundFlows.map((f) => ({ value: f.id, label: f.name }))
  const outboundFlowOptions = outboundFlows.map((f) => ({ value: f.id, label: f.name }))

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
      <h2 className="text-white text-sm font-semibold mb-5">Settings</h2>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {/* Name */}
        <div className="md:col-span-2">
          <label className="block text-xs text-gray-400 mb-1">Name</label>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
          />
        </div>

        {/* Description */}
        <div className="md:col-span-2">
          <label className="block text-xs text-gray-400 mb-1">Description</label>
          <input
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Optional"
            className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
          />
        </div>

        {/* Direction */}
        <div>
          <label className="block text-xs text-gray-400 mb-1">Direction</label>
          <div className="flex gap-2">
            {(['inbound', 'outbound'] as const).map((d) => (
              <button
                key={d}
                type="button"
                onClick={() => setDirection(d)}
                className={`flex-1 py-2 rounded-lg text-sm font-medium transition-colors capitalize ${
                  direction === d
                    ? 'bg-indigo-600 text-white'
                    : 'bg-gray-800 text-gray-400 hover:text-white'
                }`}
              >
                {d}
              </button>
            ))}
          </div>
        </div>

        {/* Dial Mode — outbound only */}
        {direction === 'outbound' ? (
          <div>
            <label className="block text-xs text-gray-400 mb-1">Outbound Dial Mode</label>
            <select
              value={dialMode}
              onChange={(e) => setDialMode(e.target.value)}
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            >
              <option value="manual">Manual — agents dial contacts themselves</option>
              <option value="progressive">Progressive — system dials one contact per available agent</option>
              <option value="predictive">Predictive — system dials ahead of agent availability</option>
            </select>
          </div>
        ) : (
          /* Campaign Priority — inbound only (replaces dial mode slot) */
          <div>
            <label className="block text-xs text-gray-400 mb-1">
              Campaign Priority <span className="text-gray-600">(1–10, higher = preferred)</span>
            </label>
            <div className="flex items-center gap-3">
              <input
                type="range" min={1} max={10} value={priority}
                onChange={(e) => setPriority(Number(e.target.value))}
                className="flex-1 accent-indigo-500"
              />
              <span className="text-white text-sm w-5 text-right">{priority}</span>
            </div>
          </div>
        )}

        {/* After-call work — always shown */}
        <div>
          <label className="block text-xs text-gray-400 mb-1">After-Call Work Time (seconds)</label>
          <input
            type="number" min={0} value={acwSeconds}
            onChange={(e) => setAcwSeconds(Number(e.target.value))}
            className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
          />
          {campaign.aiSummaryEnabled && (
            <p className="text-[11px] text-amber-300 mt-1 leading-snug">
              AI call summaries are on for this campaign — agents review the summary during wrap-up. Allow roughly 20–30 extra
              seconds of after-call work time for it.
            </p>
          )}
        </div>

        {/* Caller ID — outbound only */}
        {direction === 'outbound' && (
          <div>
            <label className="block text-xs text-gray-400 mb-1">Caller ID Number (E.164)</label>
            <input
              value={callerIdNumber}
              onChange={(e) => setCallerIdNumber(e.target.value)}
              placeholder="+15035551234"
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 font-mono"
            />
            <p className="text-[11px] text-gray-500 mt-1">Agents' manual outbound calls on this campaign show this number. Blank = the tenant default (Telephony → Phone Numbers).</p>
          </div>
        )}

        {/* Manual outbound calling window (S179) */}
        {direction === 'outbound' && dialMode === 'manual' && (
          <div>
            <label className="block text-xs text-gray-400 mb-1">Calling hours (customer's local time)</label>
            <div className="flex items-center gap-2">
              <input type="time" value={hoursStart} onChange={(e) => setHoursStart(e.target.value)} min="08:00" max="21:00"
                className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500" />
              <span className="text-gray-500 text-sm">to</span>
              <input type="time" value={hoursEnd} onChange={(e) => setHoursEnd(e.target.value)} min="08:00" max="21:00"
                className="bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500" />
              {(hoursStart || hoursEnd) && (
                <button type="button" onClick={() => { setHoursStart(''); setHoursEnd('') }} className="text-xs text-gray-400 hover:text-white">Use default</button>
              )}
            </div>
            <p className="text-[11px] text-gray-500 mt-1">
              Blank = 8:00 AM – 9:00 PM (the federal TCPA window). You can narrow it, not widen it. Dials outside the window
              are blocked and logged; the customer's time zone comes from their last call's address, else the tenant's.
            </p>
          </div>
        )}

        {/* Campaign Priority for outbound progressive/predictive */}
        {direction === 'outbound' && (dialMode === 'progressive' || dialMode === 'predictive') && (
          <div>
            <label className="block text-xs text-gray-400 mb-1">
              Campaign Priority <span className="text-gray-600">(1–10, higher = preferred)</span>
            </label>
            <div className="flex items-center gap-3">
              <input
                type="range" min={1} max={10} value={priority}
                onChange={(e) => setPriority(Number(e.target.value))}
                className="flex-1 accent-indigo-500"
              />
              <span className="text-white text-sm w-5 text-right">{priority}</span>
            </div>
          </div>
        )}

        {/* Inbound call flow — inbound campaigns only */}
        {direction === 'inbound' && (
          <div className="md:col-span-2">
            <label className="block text-xs text-gray-400 mb-1">Inbound Call Flow</label>
            <SearchableSelect
              options={inboundFlowOptions}
              value={inboundFlowId}
              onChange={setInboundFlowId}
              allLabel="No inbound flow"
              placeholder="Select a telephony flow…"
              className="w-full"
            />
            <p className="text-gray-600 text-xs mt-1">
              Telephony flow the ESL engine executes when a call arrives on this campaign's DIDs.
              Individual DIDs can override this with their own flow assignment.
            </p>
          </div>
        )}

        {/* Fallback script flow */}
        <div className="md:col-span-2">
          <label className="block text-xs text-gray-400 mb-1">Fallback Script Flow</label>
          <SearchableSelect
            options={flowOptions}
            value={flowId}
            onChange={setFlowId}
            allLabel="No flow assigned"
            placeholder="Select a flow…"
            className="w-full"
          />
          <p className="text-gray-600 text-xs mt-1">Applied when no flow is specified by the telephony call-flow node.</p>
        </div>

        {/* Outbound call flow — manual outbound only */}
        {direction === 'outbound' && dialMode === 'manual' && (
          <div className="md:col-span-2">
            <label className="block text-xs text-gray-400 mb-1">Outbound Call Flow</label>
            <SearchableSelect
              options={outboundFlowOptions}
              value={outboundFlowId}
              onChange={setOutboundFlowId}
              allLabel="No outbound flow"
              placeholder="Select a telephony flow…"
              className="w-full"
            />
            <p className="text-gray-600 text-xs mt-1">
              Telephony flow that executes before the agent dials — use to set caller ID, confirm details, or log call intent.
              Also auto-pops as a script tab when an agent uses this campaign's transfer numbers on an inbound call.
            </p>
          </div>
        )}

        {/* Queue settings — inbound always; outbound only for progressive/predictive */}
        {(direction === 'inbound' || dialMode === 'progressive' || dialMode === 'predictive') && (<>
          <div>
            <label className="block text-xs text-gray-400 mb-1">Max Queue Size</label>
            <input
              type="number" min={0} value={maxQueueSize}
              onChange={(e) => setMaxQueueSize(Number(e.target.value))}
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            />
            <p className="text-xs text-gray-500 mt-1">0 = unlimited (no queue ceiling)</p>
          </div>
          <div>
            <label className="block text-xs text-gray-400 mb-1">Queue Timeout (seconds)</label>
            <input
              type="number" min={0} value={queueTimeout}
              onChange={(e) => setQueueTimeout(Number(e.target.value))}
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            />
            <p className="text-xs text-gray-500 mt-1">0 = never time out (queue forever)</p>
          </div>

          {/* Service Level Threshold — inbound only */}
          {direction === 'inbound' && (
            <div>
              <label className="block text-xs text-gray-400 mb-1">Service Level Threshold (seconds)</label>
              <input
                type="number" min={0} value={slThreshold}
                onChange={(e) => setSlThreshold(Number(e.target.value))}
                className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
          )}

          {/* Short Abandon Threshold — inbound only */}
          {direction === 'inbound' && (
            <div>
              <label className="block text-xs text-gray-400 mb-1">
                Short Abandon Threshold <span className="text-gray-600">(seconds)</span>
              </label>
              <input
                type="number" min={0} value={shortAbandonThreshold}
                onChange={(e) => setShortAbandonThreshold(Number(e.target.value))}
                className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
              />
              <p className="text-gray-600 text-xs mt-1">Hang-ups within this many seconds of entering the queue count as a "short" abandon rather than "long" in reporting.</p>
            </div>
          )}

          {/* Ring Strategy */}
          <div className="md:col-span-2">
            <label className="block text-xs text-gray-400 mb-1">Ring Strategy</label>
            <select
              value={ringStrategy}
              onChange={(e) => setRingStrategy(e.target.value)}
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            >
              <option value="ring_all">Ring All — broadcast to every available agent, first to click wins</option>
              <option value="auto_answer_best_agent">Auto-Answer Best Agent — system picks the top-ranked agent and connects them automatically</option>
              <option value="ring_top_n_by_proficiency">Ring Top N by Proficiency — ring only the highest-ranked agents, first to click wins</option>
            </select>
            <p className="text-gray-600 text-xs mt-1">
              Ranking is by agent proficiency for this campaign, then longest idle. Ring All is the best fit for a shared line where any agent should grab the next call; the other two route to specific best-qualified agents instead.
            </p>
            {ringStrategy === 'ring_top_n_by_proficiency' && (
              <div className="mt-3 max-w-[160px]">
                <label className="block text-xs text-gray-400 mb-1">Ring Top</label>
                <input
                  type="number" min={1} value={ringTopN}
                  onChange={(e) => setRingTopN(Number(e.target.value))}
                  className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>
            )}
          </div>

          {/* Queue Acceleration */}
          <div className="md:col-span-2">
            <div className="flex items-center gap-3 mb-3">
              <button
                type="button"
                onClick={() => setAccelEnabled((v) => !v)}
                className={`relative inline-flex h-5 w-10 shrink-0 cursor-pointer items-center rounded-full transition-colors ${accelEnabled ? 'bg-indigo-600' : 'bg-gray-700'}`}
              >
                <span className={`inline-block h-4 w-4 rounded-full bg-white shadow transition-transform ${accelEnabled ? 'translate-x-5' : 'translate-x-1'}`} />
              </button>
              <span className="text-sm text-gray-300 font-medium">Queue Acceleration</span>
              <span className="text-xs text-gray-500">Increase waiting callers' priority over time</span>
            </div>
            {accelEnabled && (
              <div className="grid grid-cols-2 gap-4 pl-13">
                <div>
                  <label className="block text-xs text-gray-400 mb-1">Boost Every (seconds)</label>
                  <input
                    type="number" min={1} value={accelInterval}
                    onChange={(e) => setAccelInterval(Number(e.target.value))}
                    className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                  />
                </div>
                <div>
                  <label className="block text-xs text-gray-400 mb-1">Priority Boost per Interval</label>
                  <input
                    type="number" min={1} value={accelBoost}
                    onChange={(e) => setAccelBoost(Number(e.target.value))}
                    className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                  />
                </div>
              </div>
            )}
          </div>
        </>)}
      </div>

      <div className="flex items-center gap-3 mt-6 pt-4 border-t border-gray-800">
        <button
          onClick={handleSave}
          disabled={saving || !name.trim()}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
        >
          {saving ? 'Saving…' : 'Save settings'}
        </button>
        {saved && <span className="text-emerald-400 text-sm">Saved</span>}
        {saveError && <span className="text-red-400 text-sm">{saveError}</span>}
      </div>
    </div>
  )
}

// ── Call recording settings ──────────────────────────────────────────────────

const RECORDING_MODES: { value: string; label: string; hint: string }[] = [
  { value: 'disabled', label: 'Disabled', hint: 'No recording on this campaign. A tf_record(start) node is a no-op.' },
  { value: 'full', label: 'Full / IVR', hint: 'Recording may run from call arrival (IVR, hold, queue) through the agent conversation to disconnect.' },
  { value: 'conversation', label: 'Conversation only', hint: 'Recording may only run once the caller is bridged to an agent.' },
  { value: 'record_always_retain_by_disposition', label: 'Always record, retain by disposition', hint: 'Record every call; each disposition (or its category) decides whether the file is kept, cut to the conversation, or discarded, and for how long — set in Admin → Dispositions.' },
]

const CONSENT_MODELS: { value: string; label: string; hint: string }[] = [
  { value: 'one_party', label: 'One-party consent', hint: 'Record without an announcement.' },
  { value: 'two_party_announce', label: 'Two-party — announce', hint: 'An announcement must play before recording begins.' },
  { value: 'two_party_announce_optout', label: 'Two-party — announce + opt-out', hint: 'Announcement plays and the caller may decline recording via DTMF.' },
]

interface RecordingSettingsFormProps {
  campaign: CampaignDetail
  onSaved: (updated: CampaignDetail) => void
}

function RecordingSettingsForm({ campaign, onSaved }: RecordingSettingsFormProps) {
  const [recordingMode, setRecordingMode] = useState(campaign.recordingMode ?? 'disabled')
  const [consentModel, setConsentModel] = useState(campaign.consentModel ?? 'one_party')
  const [recordingRequired, setRecordingRequired] = useState(campaign.recordingRequired ?? false)
  const [recordStereo, setRecordStereo] = useState(campaign.recordStereo ?? true)
  const [recordingBeepEnabled, setRecordingBeepEnabled] = useState(campaign.recordingBeepEnabled ?? false)
  const [autoMaskOnHold, setAutoMaskOnHold] = useState(campaign.autoMaskOnHold ?? false)
  const [retentionDays, setRetentionDays] = useState(campaign.recordingRetentionDays ?? 90)
  const [unmappedDays, setUnmappedDays] = useState<string>(campaign.unmappedRecordingRetentionDays?.toString() ?? '')
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const enabled = recordingMode !== 'disabled'
  const inputCls = 'w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500'
  const labelCls = 'block text-xs text-gray-400 mb-1'

  const Toggle = ({ on, set, label, hint }: { on: boolean; set: (v: boolean) => void; label: string; hint: string }) => (
    <div className="flex items-start gap-3">
      <button
        type="button"
        onClick={() => set(!on)}
        className={`relative inline-flex h-5 w-10 shrink-0 cursor-pointer items-center rounded-full transition-colors mt-0.5 ${on ? 'bg-indigo-600' : 'bg-gray-700'}`}
      >
        <span className={`inline-block h-4 w-4 rounded-full bg-white shadow transition-transform ${on ? 'translate-x-5' : 'translate-x-1'}`} />
      </button>
      <div>
        <span className="text-sm text-gray-300 font-medium">{label}</span>
        <p className="text-xs text-gray-500 mt-0.5 leading-snug">{hint}</p>
      </div>
    </div>
  )

  async function handleSave() {
    setSaving(true); setSaveError(null); setSaved(false)
    try {
      const updated = await updateCampaignRecording(campaign.id, {
        recordingMode, consentModel, recordingRequired, recordStereo,
        recordingBeepEnabled, autoMaskOnHold,
        recordingRetentionDays: retentionDays,
        unmappedRecordingRetentionDays: unmappedDays.trim() ? Number(unmappedDays) : null,
      })
      onSaved({ ...campaign, ...updated })
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
      <h2 className="text-white text-sm font-semibold mb-1">Call Recording</h2>
      <p className="text-xs text-gray-500 mb-5">
        This is the policy ceiling. A <span className="font-mono">tf_record</span> node in the telephony
        flow does the actual start/stop/mask; where it sits decides coverage.
      </p>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div>
          <label className={labelCls}>Recording mode</label>
          <select className={inputCls} value={recordingMode} onChange={(e) => setRecordingMode(e.target.value)}>
            {RECORDING_MODES.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
          </select>
          <p className="text-xs text-gray-500 mt-1 leading-snug">
            {RECORDING_MODES.find((m) => m.value === recordingMode)?.hint}
          </p>
        </div>

        <div>
          <label className={labelCls}>Consent model</label>
          <select
            className={`${inputCls} disabled:opacity-50`}
            value={consentModel}
            disabled={!enabled}
            onChange={(e) => setConsentModel(e.target.value)}
          >
            {CONSENT_MODELS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
          </select>
          <p className="text-xs text-gray-500 mt-1 leading-snug">
            {CONSENT_MODELS.find((m) => m.value === consentModel)?.hint}
          </p>
        </div>

        <div>
          <label className={labelCls}>Retention (days)</label>
          <input
            type="number" min={1} max={3650} value={retentionDays}
            disabled={!enabled}
            onChange={(e) => setRetentionDays(Number(e.target.value))}
            className={`${inputCls} disabled:opacity-50`}
          />
          <p className="text-xs text-gray-500 mt-1 leading-snug">How long finished recordings are kept before the purge job removes them.</p>
        </div>

        {recordingMode === 'record_always_retain_by_disposition' && (
          <div>
            <label className={labelCls}>Missing or unmapped disposition — keep (days)</label>
            <input
              type="number" min={1} max={3650} value={unmappedDays} placeholder={`${retentionDays} (the retention above)`}
              onChange={(e) => setUnmappedDays(e.target.value)}
              className={inputCls}
            />
            <p className="text-xs text-gray-500 mt-1 leading-snug">
              Calls whose disposition isn't recorded or isn't in the catalog. A call with several interactions keeps its recording if any of
              them keeps it, for the longest period. Discards and conversation-only cuts wait 24 hours so a wrong disposition can be fixed.
            </p>
          </div>
        )}

        <div className={`space-y-4 md:col-span-2 ${enabled ? '' : 'opacity-50 pointer-events-none'}`}>
          <Toggle on={recordStereo} set={setRecordStereo}
            label="Stereo capture"
            hint="Caller and agent on separate channels — near-free, and needed for clean transcription / selective redaction later." />
          <Toggle on={recordingRequired} set={setRecordingRequired}
            label="Recording required"
            hint="If recording can't start, play an apology and don't connect the call rather than proceeding un-recorded." />
          <Toggle on={autoMaskOnHold} set={setAutoMaskOnHold}
            label="Auto-mask on hold"
            hint="Automatically mask the recording whenever the agent places the caller on hold." />
          <Toggle on={recordingBeepEnabled} set={setRecordingBeepEnabled}
            label="Periodic beep"
            hint="Play an audible tone at intervals while recording (required in some jurisdictions)." />
        </div>
      </div>

      <div className="flex items-center gap-3 mt-6 pt-4 border-t border-gray-800">
        <button
          onClick={handleSave}
          disabled={saving}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
        >
          {saving ? 'Saving…' : 'Save recording policy'}
        </button>
        {saved && <span className="text-emerald-400 text-sm">Saved</span>}
        {saveError && <span className="text-red-400 text-sm">{saveError}</span>}
      </div>
    </div>
  )
}

// ── PCI sensitive-data retention override ───────────────────────────────────

interface SensitiveDataRetentionFormProps {
  campaign: CampaignDetail
  onSaved: (updated: CampaignDetail) => void
}

const SENSITIVE_DATA_RETENTION_MAX_MINUTES = 43200 // 30 days — matches the backend clamp

/** AI call summary in wrap-up (S171) — opt-in per campaign. */
function AiSettingsForm({ campaign, onSaved }: { campaign: CampaignDetail; onSaved: (updated: CampaignDetail) => void }) {
  const [enabled, setEnabled] = useState(!!campaign.aiSummaryEnabled)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function toggle(next: boolean) {
    setSaving(true); setError(null)
    try {
      const updated = await updateCampaignAiSettings(campaign.id, next)
      setEnabled(next)
      onSaved({ ...campaign, ...updated })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
      <h2 className="text-white text-sm font-semibold mb-1">AI Call Summary</h2>
      <p className="text-xs text-gray-500 mb-4">
        When a call's script finishes, the AI drafts a short summary, the reason for the call and a suggested disposition. The agent
        reviews it during wrap-up — edits it, confirms it or discards it — and only then is it saved to the call. Payment card data is
        never sent to the AI, and personal details are withheld. Costs a fraction of a cent per call. Turn on only with the client's
        agreement.
      </p>
      <div className="flex items-start gap-3">
        <button
          type="button" disabled={saving} onClick={() => toggle(!enabled)}
          className={`relative inline-flex h-5 w-10 shrink-0 cursor-pointer items-center rounded-full transition-colors mt-0.5 disabled:opacity-50 ${enabled ? 'bg-indigo-600' : 'bg-gray-700'}`}
        >
          <span className={`inline-block h-4 w-4 rounded-full bg-white shadow transition-transform ${enabled ? 'translate-x-5' : 'translate-x-1'}`} />
        </button>
        <div>
          <span className="text-sm text-gray-300 font-medium">AI call summary in wrap-up</span>
          <p className="text-xs text-gray-500 mt-0.5 leading-snug">
            {enabled ? 'On — allow agents some extra after-call work time to review it.' : 'Off — agents can still open a summary from Call Records.'}
          </p>
        </div>
      </div>
      {error && <p className="text-red-400 text-xs mt-3">{error}</p>}
    </div>
  )
}

function SensitiveDataRetentionForm({ campaign, onSaved }: SensitiveDataRetentionFormProps) {
  const [useOverride, setUseOverride] = useState(campaign.sensitiveDataRetentionMinutes != null)
  const [minutes, setMinutes] = useState(campaign.sensitiveDataRetentionMinutes ?? 1440)
  const [cardMode, setCardMode] = useState(campaign.cardDataRetention ?? 'until_script_ends')
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const inputCls = 'w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500'
  const labelCls = 'block text-xs text-gray-400 mb-1'

  async function handleSave() {
    setSaving(true); setSaveError(null); setSaved(false)
    try {
      const updated = await updateCampaignSensitiveDataRetention(campaign.id, useOverride ? minutes : null, cardMode)
      onSaved({ ...campaign, ...updated })
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
      <h2 className="text-white text-sm font-semibold mb-1">PCI Captured-Data Retention</h2>
      <p className="text-xs text-gray-500 mb-5">
        How long a captured card/CVV/SSN blob (from a <span className="font-mono">tf_secure_collect</span> node)
        survives before the platform's safety-net job wipes it. If this campaign runs a daily or weekly secure
        export (FTPS, PGP, encrypted zip, etc.), set a window long enough for that job to run before the wipe —
        otherwise the platform default applies.
      </p>

      <div className="mb-5">
        <p className={labelCls}>Card data is wiped…</p>
        <div className="space-y-2">
          {[
            { value: 'until_script_ends', title: 'When the script finishes (default)',
              desc: 'Wiped when the agent finishes the script or passes a Commit Point — the card is only kept while the call can still re-authorize.' },
            { value: 'until_order_submitted', title: 'When the order is submitted',
              desc: 'Kept past the script until an API Call node marked "Order submission — release card data" succeeds — in the CRM flow, the telephony flow, or a resubmit from Call Records — so a reviewer can re-authorize a corrected order. The retention period below still wipes it if the order is never submitted; set it long enough for your review turnaround.' },
          ].map((o) => (
            <label key={o.value} className={`flex items-start gap-3 rounded-lg border p-3 cursor-pointer ${cardMode === o.value ? 'border-indigo-600 bg-indigo-950/30' : 'border-gray-800 hover:border-gray-700'}`}>
              <input type="radio" name="cardDataRetention" className="mt-1" checked={cardMode === o.value} onChange={() => setCardMode(o.value)} />
              <span>
                <span className="text-sm text-gray-200 font-medium">{o.title}</span>
                <span className="block text-xs text-gray-500 mt-0.5 leading-snug">{o.desc}</span>
              </span>
            </label>
          ))}
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div className="flex items-start gap-3">
          <button
            type="button"
            onClick={() => setUseOverride(!useOverride)}
            className={`relative inline-flex h-5 w-10 shrink-0 cursor-pointer items-center rounded-full transition-colors mt-0.5 ${useOverride ? 'bg-indigo-600' : 'bg-gray-700'}`}
          >
            <span className={`inline-block h-4 w-4 rounded-full bg-white shadow transition-transform ${useOverride ? 'translate-x-5' : 'translate-x-1'}`} />
          </button>
          <div>
            <span className="text-sm text-gray-300 font-medium">Override platform default</span>
            <p className="text-xs text-gray-500 mt-0.5 leading-snug">
              Off = use the platform-wide default retention window for every campaign.
            </p>
          </div>
        </div>

        <div>
          <label className={labelCls}>Retention (minutes)</label>
          <input
            type="number" min={1} max={SENSITIVE_DATA_RETENTION_MAX_MINUTES} value={minutes}
            disabled={!useOverride}
            onChange={(e) => setMinutes(Number(e.target.value))}
            className={`${inputCls} disabled:opacity-50`}
          />
          <p className="text-xs text-gray-500 mt-1 leading-snug">
            E.g. 1440 = 1 day, 10080 = 1 week. Clamped to 1–{SENSITIVE_DATA_RETENTION_MAX_MINUTES} minutes (30 days).
          </p>
        </div>
      </div>

      <div className="flex items-center gap-3 mt-6 pt-4 border-t border-gray-800">
        <button
          onClick={handleSave}
          disabled={saving}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
        >
          {saving ? 'Saving…' : 'Save retention settings'}
        </button>
        {saved && <span className="text-emerald-400 text-sm">Saved</span>}
        {saveError && <span className="text-red-400 text-sm">{saveError}</span>}
      </div>
    </div>
  )
}

// ── Routing tiers + external routing (docs/design/parallel-queuing.md) ─────────

const ACCEPT_MODES: { value: ExternalRoutingAcceptMode; label: string; limitLabel?: string; defaultLimit?: number }[] = [
  { value: 'queue_count',     label: 'Max calls in queue',   limitLabel: 'Accept while fewer than this many calls are queued', defaultLimit: 1 },
  { value: 'queue_wait',      label: 'Max queue wait',       limitLabel: 'Accept while the longest wait is under (seconds)',   defaultLimit: 30 },
  { value: 'agent_available', label: 'Agent available now' },
]

function RoutingForm({ campaign, onSaved }: { campaign: CampaignDetail; onSaved: (updated: CampaignDetail) => void }) {
  const [mode, setMode] = useState<ExternalRoutingAcceptMode>(campaign.externalRoutingAcceptMode ?? 'queue_count')
  const [limit, setLimit] = useState(campaign.externalRoutingLimit?.toString() ?? '')
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const modeInfo = ACCEPT_MODES.find((m) => m.value === mode)!
  const tiers = campaign.groupAssignments
    .filter((g) => g.isActive)
    .slice()
    .sort((a, b) => (b.routingTier ?? 0) - (a.routingTier ?? 0))

  async function handleSave() {
    setSaving(true); setSaveError(null); setSaved(false)
    try {
      const updated = await updateCampaignExternalRouting(campaign.id, mode, modeInfo.limitLabel && limit ? Number(limit) : null)
      onSaved({ ...campaign, ...updated })
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
      <h2 className="text-white text-sm font-semibold mb-1">Routing Tiers &amp; External Routing</h2>
      <p className="text-xs text-gray-500 mb-4">
        Agent groups on this campaign and the tier each is offered calls at (edit on the Agent Groups tab).
        The highest tier with anyone available gets a waiting call first.
      </p>
      {tiers.length === 0 ? (
        <p className="text-xs text-gray-500 mb-5">No agent groups assigned — every agent is in the regular pool.</p>
      ) : (
        <div className="flex flex-wrap gap-2 mb-5">
          {tiers.map((g) => (
            <span key={g.groupId} className="inline-flex items-center gap-1.5 bg-gray-800 rounded-lg px-2.5 py-1 text-xs text-gray-200">
              {g.group?.name ?? g.groupId}
              <span className="text-gray-500">tier {g.routingTier ?? 0}</span>
              {g.tierLabel && <span className="px-1.5 rounded-full bg-fuchsia-600/30 text-fuchsia-300 font-semibold uppercase text-[10px]">{g.tierLabel}</span>}
              {g.exclusiveWindowSeconds != null && <span className="text-gray-500">· {g.exclusiveWindowSeconds}s exclusive</span>}
            </span>
          ))}
        </div>
      )}

      <p className="text-xs text-gray-400 font-medium mb-1">External router accept rule</p>
      <p className="text-xs text-gray-500 mb-3">
        How this campaign answers a routing platform (e.g. RingSquared) asking whether to send a call —
        <span className="font-mono"> /api/v1/external-routing/{campaign.id}/Routing</span>. Every rule also needs an eligible agent logged in.
      </p>
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div>
          <label className="block text-xs text-gray-400 mb-1">Accept when</label>
          <select
            value={mode}
            onChange={(e) => { setMode(e.target.value as ExternalRoutingAcceptMode); setLimit('') }}
            className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
          >
            {ACCEPT_MODES.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
          </select>
        </div>
        {modeInfo.limitLabel && (
          <div>
            <label className="block text-xs text-gray-400 mb-1">{modeInfo.limitLabel}</label>
            <input
              type="number" min={1} max={10000} value={limit}
              placeholder={`default ${modeInfo.defaultLimit}`}
              onChange={(e) => setLimit(e.target.value)}
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>
        )}
      </div>

      <div className="flex items-center gap-3 mt-6 pt-4 border-t border-gray-800">
        <button
          onClick={handleSave}
          disabled={saving}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
        >
          {saving ? 'Saving…' : 'Save accept rule'}
        </button>
        {saved && <span className="text-emerald-400 text-sm">Saved</span>}
        {saveError && <span className="text-red-400 text-sm">{saveError}</span>}
      </div>
    </div>
  )
}

// ── Sales tax ─────────────────────────────────────────────────────────────────

interface StateRateRow {
  state: string
  pct: string
  taxShipping: boolean
  hasFee: boolean
  feeDesc: string
  feeAmt: string
  feeMin: string
  feeCode: string
}

const emptyStateRateRow = (): StateRateRow => ({
  state: '', pct: '', taxShipping: false, hasFee: false, feeDesc: '', feeAmt: '', feeMin: '', feeCode: '',
})

interface SalesTaxFormProps {
  campaign: CampaignDetail
  onSaved: (updated: CampaignDetail) => void
}

function SalesTaxForm({ campaign, onSaved }: SalesTaxFormProps) {
  const initial = campaign.taxSettings ?? {}
  const [provider, setProvider] = useState<TaxProviderKey>(campaign.taxProvider ?? '')
  // Rates are edited as percentages ("2.9") but stored as fractions (0.029).
  const [stateRates, setStateRates] = useState<StateRateRow[]>(
    (initial.rates ?? []).map((r) => ({
      state: r.state.toUpperCase(), pct: String(+(r.rate * 100).toFixed(4)), taxShipping: r.taxShipping ?? false,
      hasFee: !!r.fee, feeDesc: r.fee?.description ?? '', feeAmt: r.fee ? String(r.fee.amount) : '',
      feeMin: r.fee?.minTaxableSubtotal ? String(r.fee.minTaxableSubtotal) : '', feeCode: r.fee?.code ?? '',
    })),
  )
  const [feeLines, setFeeLines] = useState<AvalaraFeeLine[]>(initial.feeLines ?? [])
  const [companyCode, setCompanyCode] = useState(initial.companyCode ?? '')
  const [productTaxCode, setProductTaxCode] = useState(initial.productTaxCode ?? '')
  const [shippingTaxCode, setShippingTaxCode] = useState(initial.shippingTaxCode ?? '')
  const [customerCode, setCustomerCode] = useState(initial.customerCode ?? '')
  const [fromStreet, setFromStreet] = useState(initial.shipFrom?.street ?? '')
  const [fromCity, setFromCity] = useState(initial.shipFrom?.city ?? '')
  const [fromState, setFromState] = useState(initial.shipFrom?.state ?? '')
  const [fromZip, setFromZip] = useState(initial.shipFrom?.zip ?? '')
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const inputCls = 'w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500'
  const labelCls = 'block text-xs text-gray-400 mb-1'

  const badNum = (v: string, allowBlank = false) => {
    if (v.trim() === '') return !allowBlank
    const n = Number(v)
    return Number.isNaN(n) || n < 0
  }
  const rowInvalid = (r: StateRateRow) => {
    const n = Number(r.pct)
    if (!r.state || r.pct.trim() === '' || Number.isNaN(n) || n < 0 || n >= 100) return true
    return r.hasFee && (!r.feeDesc.trim() || badNum(r.feeAmt) || badNum(r.feeMin, true))
  }
  const feeLineInvalid = (f: AvalaraFeeLine) => !f.state || !f.taxCode.trim() || !f.description.trim()
  const rateInvalid = (provider === '' && stateRates.some(rowInvalid))
    || (provider === 'avalara' && feeLines.some(feeLineInvalid))

  function updateRow(i: number, patch: Partial<StateRateRow>) {
    setStateRates((rows) => rows.map((r, j) => (j === i ? { ...r, ...patch } : r)))
  }

  function updateFeeLine(i: number, patch: Partial<AvalaraFeeLine>) {
    setFeeLines((rows) => rows.map((r, j) => (j === i ? { ...r, ...patch } : r)))
  }

  function buildSettings(): CampaignTaxSettings | null {
    if (provider === '') {
      if (stateRates.length === 0) return null
      return {
        rates: stateRates.map((r) => ({
          state: r.state,
          rate: +(Number(r.pct) / 100).toFixed(6),
          taxShipping: r.taxShipping,
          fee: r.hasFee
            ? {
                description: r.feeDesc.trim(),
                amount: Number(r.feeAmt),
                minTaxableSubtotal: r.feeMin.trim() ? Number(r.feeMin) : undefined,
                code: r.feeCode.trim() || undefined,
              }
            : null,
        })),
      }
    }
    const t = (v: string) => v.trim() || undefined
    const shipFrom = fromZip.trim()
      ? { street: t(fromStreet), city: t(fromCity), state: t(fromState)?.toUpperCase(), zip: fromZip.trim(), country: 'US' }
      : undefined
    return {
      companyCode: t(companyCode), productTaxCode: t(productTaxCode), shippingTaxCode: t(shippingTaxCode),
      customerCode: t(customerCode), shipFrom,
      feeLines: feeLines.map((f) => ({
        state: f.state, taxCode: f.taxCode.trim(), description: f.description.trim(), code: f.code?.trim() || undefined,
      })),
    }
  }

  async function handleSave() {
    setSaving(true); setSaveError(null); setSaved(false)
    try {
      const updated = await updateCampaignTax(campaign.id, provider, buildSettings())
      onSaved({ ...campaign, ...updated })
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
      <h2 className="text-white text-sm font-semibold mb-1">Sales Tax</h2>
      <p className="text-xs text-gray-500 mb-5">
        How this campaign's carts are taxed. Tax is recalculated automatically every time the cart changes and
        whenever an address node saves a shipping address to the call record.
      </p>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div>
          <label className={labelCls}>Tax provider</label>
          <select value={provider} onChange={(e) => setProvider(e.target.value as TaxProviderKey)} className={inputCls}>
            <option value="">Flat rate</option>
            <option value="avalara">Avalara AvaTax</option>
          </select>
        </div>

      </div>

      {provider === '' && (
        <div className="mt-4">
          <p className="text-xs text-gray-400 font-medium mb-1">Taxable states</p>
          <p className="text-xs text-gray-500 mb-3 leading-snug">
            A static rate per state, applied to the ship-to state (billing if there's no shipping address). States not
            listed are not taxed. Tax-exempt offers are skipped. Check "Tax shipping" for states that tax delivery charges —
            shipping is taxed at the same rate, on the taxable share of the cart only. "State fee" adds a fixed per-order fee
            shown separately from tax (e.g. Colorado's Retail Delivery Fee), charged only when the order has taxable items
            and meets the optional minimum. Static rates and fees can go stale; use a tax API provider when the client has one.
          </p>
          {stateRates.length === 0 && (
            <p className="text-xs text-gray-500 italic mb-2">No taxable states — carts on this campaign are not taxed.</p>
          )}
          <div className="flex flex-col gap-2">
            {stateRates.map((row, i) => {
              const taken = new Set(stateRates.filter((_, j) => j !== i).map((r) => r.state))
              return (
                <div key={i} className="flex flex-col gap-1.5 border-b border-gray-800/60 pb-2">
                <div className="flex items-center gap-2 flex-wrap">
                  <select
                    value={row.state}
                    onChange={(e) => updateRow(i, { state: e.target.value })}
                    className={`${inputCls} max-w-xs ${!row.state ? 'ring-2 ring-red-500' : ''}`}
                  >
                    <option value="">Select a state…</option>
                    {US_STATES.filter(([code]) => !taken.has(code)).map(([code, name]) => (
                      <option key={code} value={code}>{name} ({code})</option>
                    ))}
                  </select>
                  <div className="relative w-32">
                    <input
                      value={row.pct}
                      onChange={(e) => updateRow(i, { pct: e.target.value })}
                      placeholder="e.g. 2.9"
                      inputMode="decimal"
                      className={`${inputCls} pr-7 ${rowInvalid(row) && row.state ? 'ring-2 ring-red-500' : ''}`}
                    />
                    <span className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-500 text-sm">%</span>
                  </div>
                  <label className="flex items-center gap-1.5 text-xs text-gray-300 cursor-pointer whitespace-nowrap">
                    <input
                      type="checkbox"
                      checked={row.taxShipping}
                      onChange={(e) => updateRow(i, { taxShipping: e.target.checked })}
                    />
                    Tax shipping
                  </label>
                  <label className="flex items-center gap-1.5 text-xs text-gray-300 cursor-pointer whitespace-nowrap">
                    <input
                      type="checkbox"
                      checked={row.hasFee}
                      onChange={(e) => updateRow(i, { hasFee: e.target.checked })}
                    />
                    State fee
                  </label>
                  <button
                    type="button"
                    onClick={() => setStateRates((rows) => rows.filter((_, j) => j !== i))}
                    className="text-gray-500 hover:text-red-400 text-xs px-2"
                  >
                    Remove
                  </button>
                </div>
                {row.hasFee && (
                  <div className="flex items-center gap-2 flex-wrap pl-4">
                    <input
                      value={row.feeDesc}
                      onChange={(e) => updateRow(i, { feeDesc: e.target.value })}
                      placeholder="Fee description, e.g. Retail Delivery Fee"
                      className={`${inputCls} max-w-xs ${!row.feeDesc.trim() ? 'ring-2 ring-red-500' : ''}`}
                    />
                    <div className="relative w-28">
                      <span className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 text-sm">$</span>
                      <input
                        value={row.feeAmt}
                        onChange={(e) => updateRow(i, { feeAmt: e.target.value })}
                        placeholder="0.29"
                        inputMode="decimal"
                        className={`${inputCls} pl-6 ${badNum(row.feeAmt) ? 'ring-2 ring-red-500' : ''}`}
                      />
                    </div>
                    <div className="relative w-40">
                      <span className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 text-sm">min $</span>
                      <input
                        value={row.feeMin}
                        onChange={(e) => updateRow(i, { feeMin: e.target.value })}
                        placeholder="optional"
                        inputMode="decimal"
                        className={`${inputCls} pl-14`}
                      />
                    </div>
                    <input
                      value={row.feeCode}
                      onChange={(e) => updateRow(i, { feeCode: e.target.value })}
                      placeholder={`Code (default ${row.state || 'ST'}_FEE)`}
                      className={`${inputCls} max-w-[11rem]`}
                    />
                  </div>
                )}
                </div>
              )
            })}
          </div>
          <button
            type="button"
            onClick={() => setStateRates((rows) => [...rows, emptyStateRateRow()])}
            className="mt-2 text-indigo-400 hover:text-indigo-300 text-xs font-medium"
          >
            + Add state
          </button>
        </div>
      )}

      {provider === 'avalara' && (
        <>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mt-4">
            <div>
              <label className={labelCls}>Product tax code</label>
              <input value={productTaxCode} onChange={(e) => setProductTaxCode(e.target.value)} placeholder="e.g. PF050714" className={inputCls} />
            </div>
            <div>
              <label className={labelCls}>Shipping tax code</label>
              <input value={shippingTaxCode} onChange={(e) => setShippingTaxCode(e.target.value)} placeholder="e.g. FR020200" className={inputCls} />
            </div>
            <div>
              <label className={labelCls}>Company code</label>
              <input value={companyCode} onChange={(e) => setCompanyCode(e.target.value)} placeholder="blank = account default" className={inputCls} />
            </div>
            <div>
              <label className={labelCls}>Customer code</label>
              <input value={customerCode} onChange={(e) => setCustomerCode(e.target.value)} placeholder="blank = ContactConnection" className={inputCls} />
            </div>
          </div>

          <p className="text-xs text-gray-400 font-medium mt-5 mb-2">Ship-from address</p>
          <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
            <div className="md:col-span-2">
              <label className={labelCls}>Street</label>
              <input value={fromStreet} onChange={(e) => setFromStreet(e.target.value)} className={inputCls} />
            </div>
            <div>
              <label className={labelCls}>City</label>
              <input value={fromCity} onChange={(e) => setFromCity(e.target.value)} className={inputCls} />
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className={labelCls}>State</label>
                <input value={fromState} maxLength={2} onChange={(e) => setFromState(e.target.value)} className={inputCls} />
              </div>
              <div>
                <label className={labelCls}>ZIP</label>
                <input value={fromZip} onChange={(e) => setFromZip(e.target.value)} className={inputCls} />
              </div>
            </div>
          </div>
          <p className="text-xs text-gray-500 mt-1 leading-snug">
            Optional. Without it, Avalara taxes by the customer's ship-to address alone.
          </p>

          <p className="text-xs text-gray-400 font-medium mt-5 mb-1">State fee lines</p>
          <p className="text-xs text-gray-500 mb-2 leading-snug">
            Fees Avalara calculates from a dedicated line when the order ships to that state — e.g. Colorado's Retail
            Delivery Fee (tax code OF400000). Avalara decides whether it applies and the current amount; the cart shows it
            separately from tax.
          </p>
          <div className="flex flex-col gap-2">
            {feeLines.map((f, i) => (
              <div key={i} className="flex items-center gap-2 flex-wrap">
                <select
                  value={f.state}
                  onChange={(e) => updateFeeLine(i, { state: e.target.value })}
                  className={`${inputCls} max-w-[14rem] ${!f.state ? 'ring-2 ring-red-500' : ''}`}
                >
                  <option value="">Select a state…</option>
                  {US_STATES.map(([code, name]) => (
                    <option key={code} value={code}>{name} ({code})</option>
                  ))}
                </select>
                <input
                  value={f.taxCode}
                  onChange={(e) => updateFeeLine(i, { taxCode: e.target.value })}
                  placeholder="Tax code"
                  className={`${inputCls} max-w-[8rem] ${!f.taxCode.trim() ? 'ring-2 ring-red-500' : ''}`}
                />
                <input
                  value={f.description}
                  onChange={(e) => updateFeeLine(i, { description: e.target.value })}
                  placeholder="Description"
                  className={`${inputCls} max-w-xs ${!f.description.trim() ? 'ring-2 ring-red-500' : ''}`}
                />
                <input
                  value={f.code ?? ''}
                  onChange={(e) => updateFeeLine(i, { code: e.target.value })}
                  placeholder={`Code (default ${f.state || 'ST'}_FEE)`}
                  className={`${inputCls} max-w-[11rem]`}
                />
                <button
                  type="button"
                  onClick={() => setFeeLines((rows) => rows.filter((_, j) => j !== i))}
                  className="text-gray-500 hover:text-red-400 text-xs px-2"
                >
                  Remove
                </button>
              </div>
            ))}
          </div>
          <div className="flex items-center gap-4 mt-2">
            <button
              type="button"
              onClick={() => setFeeLines((rows) => [...rows, { state: '', taxCode: '', description: '' }])}
              className="text-indigo-400 hover:text-indigo-300 text-xs font-medium"
            >
              + Add fee line
            </button>
            {!feeLines.some((f) => f.state === 'CO' && f.taxCode.trim().toUpperCase() === 'OF400000') && (
              <button
                type="button"
                onClick={() => setFeeLines((rows) => [...rows,
                  { state: 'CO', taxCode: 'OF400000', description: 'Retail Delivery Fee', code: 'CO_RDF' }])}
                className="text-indigo-400 hover:text-indigo-300 text-xs font-medium"
              >
                + Colorado Retail Delivery Fee
              </button>
            )}
          </div>
        </>
      )}

      <div className="flex items-center gap-3 mt-6 pt-4 border-t border-gray-800">
        <button
          onClick={handleSave}
          disabled={saving || rateInvalid}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium transition-colors"
        >
          {saving ? 'Saving…' : 'Save sales tax'}
        </button>
        {saved && <span className="text-emerald-400 text-sm">Saved</span>}
        {rateInvalid && <span className="text-red-400 text-sm">Fix the highlighted fields before saving.</span>}
        {saveError && <span className="text-red-400 text-sm">{saveError}</span>}
      </div>

      {provider !== '' && (
        <div className="mt-6 pt-5 border-t border-gray-800">
          <p className="text-xs text-gray-400 font-medium mb-1">Provider credentials</p>
          <p className="text-xs text-gray-500 mb-4 leading-snug">
            Saved separately from the settings above, and never stored with the campaign. A field left unset here falls
            back to the client-wide value, then the tenant default.
          </p>
          <CampaignCredentialCards campaignId={campaign.id} section="tax-providers" only={provider} />
        </div>
      )}
    </div>
  )
}

// ── Agents section ────────────────────────────────────────────────────────────

interface AgentsSectionProps {
  campaignId: string
  assignments: AgentAssignment[]
  allAgents: AgentRecord[]
  onChanged: () => void
}

function AgentsSection({ campaignId, assignments, allAgents, onChanged }: AgentsSectionProps) {
  const [mode, setMode] = useState<'none' | 'single' | 'bulk'>('none')

  // Single add
  const [singleAgentId, setSingleAgentId] = useState('')
  const [singleProficiency, setSingleProficiency] = useState(50)
  const [singleAdding, setSingleAdding] = useState(false)
  const [singleError, setSingleError] = useState<string | null>(null)

  // Bulk add
  const [bulkSearch, setBulkSearch] = useState('')
  const [bulkSelected, setBulkSelected] = useState<Set<string>>(new Set())
  const [bulkProficiency, setBulkProficiency] = useState(50)
  const [bulkAdding, setBulkAdding] = useState(false)
  const [bulkResult, setBulkResult] = useState<string | null>(null)

  // Per-row proficiency editing
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editProficiency, setEditProficiency] = useState(50)
  const [removingId, setRemovingId] = useState<string | null>(null)

  const assignedIds = new Set(assignments.map((a) => a.agentId))
  const unassigned = allAgents.filter((a) => !assignedIds.has(a.id) && a.isActive)

  const agentById = Object.fromEntries(allAgents.map((a) => [a.id, a]))

  const bulkFiltered = unassigned.filter((a) => {
    const q = bulkSearch.toLowerCase()
    return (
      a.firstName.toLowerCase().includes(q) ||
      a.lastName.toLowerCase().includes(q) ||
      a.email.toLowerCase().includes(q)
    )
  })

  function resetMode() {
    setMode('none')
    setSingleAgentId(''); setSingleProficiency(50); setSingleError(null)
    setBulkSearch(''); setBulkSelected(new Set()); setBulkProficiency(50); setBulkResult(null)
  }

  async function handleSingleAdd() {
    if (!singleAgentId) return
    setSingleAdding(true); setSingleError(null)
    try {
      await assignCampaignAgent(campaignId, singleAgentId, singleProficiency)
      onChanged()
      resetMode()
    } catch (e) {
      setSingleError(e instanceof Error ? e.message : 'Failed to add agent.')
    } finally {
      setSingleAdding(false)
    }
  }

  async function handleBulkAdd() {
    if (bulkSelected.size === 0) return
    setBulkAdding(true); setBulkResult(null)
    try {
      const agents = Array.from(bulkSelected).map((id) => ({ agentId: id, proficiency: bulkProficiency }))
      const result = await bulkAssignCampaignAgents(campaignId, agents)
      setBulkResult(`Added ${result.added}, updated ${result.updated}.`)
      onChanged()
      setBulkSelected(new Set())
    } catch (e) {
      setBulkResult(e instanceof Error ? e.message : 'Bulk add failed.')
    } finally {
      setBulkAdding(false)
    }
  }

  async function handleUpdateProficiency(agentId: string) {
    try {
      await updateCampaignAgentProficiency(campaignId, agentId, editProficiency)
      onChanged()
      setEditingId(null)
    } catch {}
  }

  async function handleRemove(agentId: string) {
    setRemovingId(agentId)
    try {
      await removeCampaignAgent(campaignId, agentId)
      onChanged()
    } catch {}
    finally { setRemovingId(null) }
  }

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
      <div className="flex items-center justify-between mb-5">
        <h2 className="text-white text-sm font-semibold">Agents</h2>
        {mode === 'none' && (
          <div className="flex gap-2">
            <button
              onClick={() => setMode('single')}
              className="bg-gray-800 hover:bg-gray-700 text-gray-300 hover:text-white rounded-lg px-3 py-1.5 text-xs font-medium transition-colors"
            >
              + Add agent
            </button>
            <button
              onClick={() => setMode('bulk')}
              className="bg-gray-800 hover:bg-gray-700 text-gray-300 hover:text-white rounded-lg px-3 py-1.5 text-xs font-medium transition-colors"
            >
              + Bulk add
            </button>
          </div>
        )}
        {mode !== 'none' && (
          <button onClick={resetMode} className="text-gray-500 hover:text-white text-xs transition-colors">
            Cancel
          </button>
        )}
      </div>

      {/* Single add */}
      {mode === 'single' && (
        <div className="bg-gray-800/50 border border-gray-700 rounded-lg p-4 mb-4">
          <p className="text-gray-300 text-xs font-medium mb-3">Add a single agent</p>
          <div className="flex items-end gap-3 flex-wrap">
            <div className="flex-1 min-w-[200px]">
              <label className="block text-xs text-gray-500 mb-1">Agent</label>
              <SearchableSelect
                options={unassigned.map((a) => ({ value: a.id, label: `${a.firstName} ${a.lastName} — ${a.email}` }))}
                value={singleAgentId}
                onChange={setSingleAgentId}
                placeholder="Search agents…"
                className="w-full"
              />
            </div>
            <div className="w-40">
              <label className="block text-xs text-gray-500 mb-1">Proficiency (1–100)</label>
              <input
                type="number" min={1} max={100} value={singleProficiency}
                onChange={(e) => setSingleProficiency(Number(e.target.value))}
                className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
            <button
              onClick={handleSingleAdd}
              disabled={singleAdding || !singleAgentId}
              className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
            >
              {singleAdding ? 'Adding…' : 'Add'}
            </button>
          </div>
          {singleError && <p className="text-red-400 text-xs mt-2">{singleError}</p>}
        </div>
      )}

      {/* Bulk add */}
      {mode === 'bulk' && (
        <div className="bg-gray-800/50 border border-gray-700 rounded-lg p-4 mb-4">
          <div className="flex items-center justify-between mb-3">
            <p className="text-gray-300 text-xs font-medium">Bulk add agents</p>
            <div className="flex items-center gap-3">
              <label className="text-xs text-gray-500">Default proficiency</label>
              <input
                type="number" min={1} max={100} value={bulkProficiency}
                onChange={(e) => setBulkProficiency(Number(e.target.value))}
                className="w-20 bg-gray-800 text-white rounded-lg px-2 py-1 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
          </div>
          <input
            value={bulkSearch}
            onChange={(e) => setBulkSearch(e.target.value)}
            placeholder="Search agents…"
            className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 mb-3"
          />
          <div className="max-h-52 overflow-y-auto border border-gray-700 rounded-lg divide-y divide-gray-700/50 mb-3">
            {bulkFiltered.length === 0 && (
              <p className="text-gray-500 text-xs p-3">
                {unassigned.length === 0 ? 'All active agents are already assigned.' : 'No agents match.'}
              </p>
            )}
            {bulkFiltered.map((a) => (
              <label key={a.id} className="flex items-center gap-3 px-3 py-2 hover:bg-gray-700/40 cursor-pointer">
                <input
                  type="checkbox"
                  checked={bulkSelected.has(a.id)}
                  onChange={(e) => {
                    setBulkSelected((prev) => {
                      const next = new Set(prev)
                      e.target.checked ? next.add(a.id) : next.delete(a.id)
                      return next
                    })
                  }}
                  className="accent-indigo-500"
                />
                <span className="text-sm text-gray-200">{a.firstName} {a.lastName}</span>
                <span className="text-xs text-gray-500 ml-auto">{a.email}</span>
              </label>
            ))}
          </div>
          <div className="flex items-center gap-3">
            <button
              onClick={handleBulkAdd}
              disabled={bulkAdding || bulkSelected.size === 0}
              className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
            >
              {bulkAdding ? 'Adding…' : `Add ${bulkSelected.size > 0 ? bulkSelected.size : ''} selected`}
            </button>
            {bulkResult && <span className="text-xs text-gray-400">{bulkResult}</span>}
          </div>
        </div>
      )}

      {/* Assignment list */}
      {assignments.length === 0 ? (
        <p className="text-gray-500 text-sm">No agents assigned yet.</p>
      ) : (
        <div className="border border-gray-800 rounded-lg overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-800 text-gray-400 text-left">
                <th className="px-4 py-2.5 font-medium text-xs">Agent</th>
                <th className="px-4 py-2.5 font-medium text-xs">Email</th>
                <th className="px-4 py-2.5 font-medium text-xs">Proficiency</th>
                <th className="px-4 py-2.5" />
              </tr>
            </thead>
            <tbody>
              {assignments.map((a) => {
                const agent = agentById[a.agentId]
                return (
                  <tr key={a.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30">
                    <td className="px-4 py-2.5 text-white font-medium">
                      {agent ? `${agent.firstName} ${agent.lastName}` : <span className="text-gray-500 font-mono text-xs">{a.agentId}</span>}
                    </td>
                    <td className="px-4 py-2.5 text-gray-400 text-xs">{agent?.email ?? '—'}</td>
                    <td className="px-4 py-2.5">
                      {editingId === a.agentId ? (
                        <div className="flex items-center gap-2">
                          <input
                            type="number" min={1} max={100} value={editProficiency}
                            onChange={(e) => setEditProficiency(Number(e.target.value))}
                            className="w-20 bg-gray-800 text-white rounded px-2 py-1 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
                          />
                          <button
                            onClick={() => handleUpdateProficiency(a.agentId)}
                            className="text-xs text-indigo-400 hover:text-indigo-300"
                          >
                            Save
                          </button>
                          <button
                            onClick={() => setEditingId(null)}
                            className="text-xs text-gray-500 hover:text-white"
                          >
                            ✕
                          </button>
                        </div>
                      ) : (
                        <button
                          onClick={() => { setEditingId(a.agentId); setEditProficiency(a.proficiency) }}
                          className="text-gray-300 hover:text-white text-xs"
                        >
                          {a.proficiency} <span className="text-gray-600">/ 100</span>
                          <span className="text-gray-600 ml-1 hover:text-gray-400"> ✎</span>
                        </button>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-right">
                      <button
                        onClick={() => handleRemove(a.agentId)}
                        disabled={removingId === a.agentId}
                        className="text-xs text-red-500 hover:text-red-400 disabled:opacity-50 transition-colors"
                      >
                        {removingId === a.agentId ? 'Removing…' : 'Remove'}
                      </button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

// ── External Numbers section (manual outbound campaigns only) ─────────────────

interface ExternalNumbersSectionProps {
  campaignId: string
  flows: FlowSummary[]
}

function ExternalNumbersSection({ campaignId, flows }: ExternalNumbersSectionProps) {
  const [numbers, setNumbers]   = useState<CampaignExternalNumber[]>([])
  const [loading, setLoading]   = useState(true)
  const [newLabel, setNewLabel] = useState('')
  const [newNumber, setNewNumber] = useState('')
  const [adding, setAdding]     = useState(false)
  const [addError, setAddError] = useState<string | null>(null)
  const [removingId, setRemovingId] = useState<string | null>(null)

  const crmFlows      = flows.filter((f) => f.flow_type === 'crm')
  const outboundFlows = flows.filter(
    (f) => f.flow_type === 'telephony' && f.flow_direction === 'outbound' && f.flow_sub_type === 'manual'
  )
  const crmOptions      = crmFlows.map((f) => ({ value: f.id, label: f.name }))
  const outboundOptions = outboundFlows.map((f) => ({ value: f.id, label: f.name }))

  useEffect(() => {
    listExternalNumbers(campaignId)
      .then(setNumbers)
      .catch(() => {})
      .finally(() => setLoading(false))
  }, [campaignId])

  async function handleAdd() {
    if (!newLabel.trim() || !newNumber.trim()) return
    setAdding(true); setAddError(null)
    try {
      const created = await addExternalNumber(campaignId, newLabel.trim(), newNumber.trim())
      setNumbers((prev) => [...prev, created])
      setNewLabel(''); setNewNumber('')
    } catch (e) {
      setAddError(e instanceof Error ? e.message : 'Failed to add number.')
    } finally {
      setAdding(false)
    }
  }

  async function handleRemove(numberId: string) {
    setRemovingId(numberId)
    try {
      await removeExternalNumber(campaignId, numberId)
      setNumbers((prev) => prev.filter((n) => n.id !== numberId))
    } catch {}
    finally { setRemovingId(null) }
  }

  async function handleFlowChange(numberId: string, flowId: string) {
    try {
      const updated = flowId
        ? await setExternalNumberFlow(campaignId, numberId, flowId)
        : await removeExternalNumberFlow(campaignId, numberId)
      setNumbers((prev) => prev.map((n) => n.id === numberId ? { ...n, flowId: updated.flowId } : n))
    } catch {}
  }

  async function handleTelephonyFlowChange(numberId: string, flowId: string) {
    try {
      const updated = flowId
        ? await setExternalNumberTelephonyFlow(campaignId, numberId, flowId)
        : await removeExternalNumberTelephonyFlow(campaignId, numberId)
      setNumbers((prev) => prev.map((n) => n.id === numberId ? { ...n, telephonyFlowId: updated.telephonyFlowId } : n))
    } catch {}
  }

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-6">
      <div className="mb-5">
        <h2 className="text-white text-sm font-semibold">External Transfer Numbers</h2>
        <p className="text-gray-500 text-xs mt-1">
          Numbers agents can dial for transfers while on an inbound call from this client.
          Shown directly in the softphone during a call. Flow overrides let each transfer number
          pop a specific script or execute a specific telephony flow.
        </p>
      </div>

      {loading ? (
        <p className="text-gray-500 text-sm">Loading…</p>
      ) : (
        <>
          {numbers.length > 0 && (
            <div className="border border-gray-800 rounded-lg overflow-x-auto mb-4">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-gray-800 text-gray-400 text-left">
                    <th className="px-4 py-2.5 font-medium text-xs whitespace-nowrap">Label</th>
                    <th className="px-4 py-2.5 font-medium text-xs whitespace-nowrap">Number</th>
                    <th className="px-4 py-2.5 font-medium text-xs whitespace-nowrap">Script Flow Override</th>
                    <th className="px-4 py-2.5 font-medium text-xs whitespace-nowrap">Telephony Flow Override</th>
                    <th className="px-4 py-2.5" />
                  </tr>
                </thead>
                <tbody>
                  {numbers.map((n) => (
                    <tr key={n.id} className="border-b border-gray-800 last:border-0 hover:bg-gray-800/30">
                      <td className="px-4 py-2.5 text-white whitespace-nowrap">{n.label}</td>
                      <td className="px-4 py-2.5 text-gray-300 font-mono text-xs whitespace-nowrap">{n.number}</td>
                      <td className="px-4 py-2.5 min-w-[200px]">
                        <SearchableSelect
                          options={crmOptions}
                          value={n.flowId ?? ''}
                          onChange={(v) => handleFlowChange(n.id, v)}
                          allLabel="Campaign default"
                          placeholder="Select script flow…"
                        />
                      </td>
                      <td className="px-4 py-2.5 min-w-[200px]">
                        <SearchableSelect
                          options={outboundOptions}
                          value={n.telephonyFlowId ?? ''}
                          onChange={(v) => handleTelephonyFlowChange(n.id, v)}
                          allLabel="Campaign default"
                          placeholder="Select telephony flow…"
                        />
                      </td>
                      <td className="px-4 py-2.5 text-right whitespace-nowrap">
                        <button
                          onClick={() => handleRemove(n.id)}
                          disabled={removingId === n.id}
                          className="text-xs text-red-500 hover:text-red-400 disabled:opacity-50 transition-colors"
                        >
                          {removingId === n.id ? 'Removing…' : 'Remove'}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {numbers.length === 0 && (
            <p className="text-gray-500 text-sm mb-4">No transfer numbers configured yet.</p>
          )}

          {/* Add form */}
          <div className="flex items-end gap-3 flex-wrap">
            <div className="flex-1 min-w-[160px]">
              <label className="block text-xs text-gray-400 mb-1">Label</label>
              <input
                value={newLabel}
                onChange={(e) => setNewLabel(e.target.value)}
                placeholder="e.g. Customer Service"
                className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
            <div className="flex-1 min-w-[160px]">
              <label className="block text-xs text-gray-400 mb-1">Phone Number</label>
              <input
                value={newNumber}
                onChange={(e) => setNewNumber(e.target.value)}
                placeholder="+15035551234"
                className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500 font-mono"
              />
            </div>
            <button
              onClick={handleAdd}
              disabled={adding || !newLabel.trim() || !newNumber.trim()}
              className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors shrink-0"
            >
              {adding ? 'Adding…' : '+ Add'}
            </button>
          </div>
          {addError && <p className="text-red-400 text-xs mt-2">{addError}</p>}
        </>
      )}
    </div>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function CampaignDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [campaign, setCampaign] = useState<CampaignDetail | null>(null)
  const [flows, setFlows] = useState<FlowSummary[]>([])
  const [allAgents, setAllAgents] = useState<AgentRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [togglingStatus, setTogglingStatus] = useState(false)

  const load = useCallback(async () => {
    if (!id) return
    try {
      const [c, f] = await Promise.all([
        getCampaign(id),
        flowsApi.listAll(),
      ])
      setCampaign(c); setFlows(f)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load campaign.')
    } finally {
      setLoading(false)
    }
    // Agents load separately — failure shows empty list, not page error
    try {
      const a = await listAdminAgents()
      setAllAgents(a)
    } catch {
      // AgentsSection will show with an empty list
    }
  }, [id])

  useEffect(() => { load() }, [load])

  async function handleStatusToggle(action: 'activate' | 'pause' | 'deactivate') {
    if (!campaign) return
    setTogglingStatus(true)
    try {
      const fn = action === 'activate' ? activateCampaign
        : action === 'pause' ? pauseCampaign
        : deactivateCampaign
      const updated = await fn(campaign.id)
      setCampaign((prev) => prev ? { ...prev, status: updated.status } : prev)
    } catch {}
    finally { setTogglingStatus(false) }
  }

  if (loading) {
    return (
      <AdminShell>
        <div className="flex items-center justify-center h-40 text-gray-500 text-sm">Loading…</div>
      </AdminShell>
    )
  }

  if (error || !campaign) {
    return (
      <AdminShell>
        <div className="p-6">
          <p className="text-red-400 text-sm">{error ?? 'Campaign not found.'}</p>
          <button onClick={() => navigate('/admin/telephony', { state: { tab: 'campaigns' } })} className="text-indigo-400 text-sm mt-2 hover:underline">
            ← Back to Telephony
          </button>
        </div>
      </AdminShell>
    )
  }

  return (
    <AdminShell>
      <div className="p-6 max-w-5xl">
        {/* Header */}
        <div className="flex items-start justify-between mb-6">
          <div>
            <button
              onClick={() => navigate('/admin/telephony', { state: { tab: 'campaigns' } })}
              className="text-gray-500 hover:text-gray-300 text-xs mb-2 flex items-center gap-1 transition-colors"
            >
              ← Clients / Telephony
            </button>
            <div className="flex items-center gap-3">
              <h1 className="text-white text-xl font-semibold">{campaign.name}</h1>
              <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${STATUS_COLORS[campaign.status] ?? STATUS_COLORS.inactive}`}>
                {campaign.status}
              </span>
              <span className="text-gray-500 text-xs capitalize">{campaign.direction}</span>
            </div>
            {campaign.client && (
              <p className="text-gray-500 text-sm mt-0.5">Client: {campaign.client.name}</p>
            )}
          </div>

          {/* Status actions */}
          <div className="flex items-center gap-2">
            {campaign.status !== 'active' && (
              <button
                onClick={() => handleStatusToggle('activate')}
                disabled={togglingStatus}
                className="text-xs text-emerald-400 hover:text-emerald-300 border border-emerald-800 hover:border-emerald-600 rounded px-2.5 py-1 disabled:opacity-50 transition-colors"
              >
                Activate
              </button>
            )}
            {campaign.status === 'active' && (
              <button
                onClick={() => handleStatusToggle('pause')}
                disabled={togglingStatus}
                className="text-xs text-amber-400 hover:text-amber-300 border border-amber-800 hover:border-amber-600 rounded px-2.5 py-1 disabled:opacity-50 transition-colors"
              >
                Pause
              </button>
            )}
            {campaign.status !== 'inactive' && (
              <button
                onClick={() => handleStatusToggle('deactivate')}
                disabled={togglingStatus}
                className="text-xs text-gray-500 hover:text-gray-300 border border-gray-700 hover:border-gray-500 rounded px-2.5 py-1 disabled:opacity-50 transition-colors"
              >
                Deactivate
              </button>
            )}
          </div>
        </div>

        <div className="space-y-6">
          <SettingsForm
            campaign={campaign}
            flows={flows}
            onSaved={(updated) => setCampaign(updated)}
          />
          <RecordingSettingsForm
            campaign={campaign}
            onSaved={(updated) => setCampaign(updated)}
          />
          <AiSettingsForm
            campaign={campaign}
            onSaved={(updated) => setCampaign(updated)}
          />
          <SensitiveDataRetentionForm
            campaign={campaign}
            onSaved={(updated) => setCampaign(updated)}
          />
          <SalesTaxForm
            campaign={campaign}
            onSaved={(updated) => setCampaign(updated)}
          />
          <PaymentGatewaysForm campaignId={campaign.id} />
          {campaign.direction === 'inbound' && (
            <RoutingForm
              campaign={campaign}
              onSaved={(updated) => setCampaign(updated)}
            />
          )}
          <AgentsSection
            campaignId={campaign.id}
            assignments={campaign.agentAssignments}
            allAgents={allAgents}
            onChanged={load}
          />
          {campaign.direction === 'outbound' && campaign.dialMode === 'manual' && (
            <ExternalNumbersSection campaignId={campaign.id} flows={flows} />
          )}
        </div>
      </div>
    </AdminShell>
  )
}
