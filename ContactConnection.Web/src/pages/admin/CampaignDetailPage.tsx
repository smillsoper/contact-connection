import { useEffect, useState, useCallback } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import AdminShell from '../../components/admin/AdminShell'
import SearchableSelect from '../../components/SearchableSelect'
import {
  getCampaign, updateCampaign, setCampaignFlow, removeCampaignFlow,
  setCampaignInboundFlow, removeCampaignInboundFlow,
  setCampaignOutboundFlow, removeCampaignOutboundFlow,
  activateCampaign, pauseCampaign, deactivateCampaign,
  assignCampaignAgent, bulkAssignCampaignAgents,
  updateCampaignAgentProficiency, removeCampaignAgent,
  listExternalNumbers, addExternalNumber, removeExternalNumber,
  setExternalNumberFlow, removeExternalNumberFlow,
  setExternalNumberTelephonyFlow, removeExternalNumberTelephonyFlow,
  type CampaignDetail, type AgentAssignment, type CampaignExternalNumber,
} from '../../api/telephony'
import { flowsApi, type FlowSummary } from '../../api/flows'
import { listAdminAgents, type AgentRecord } from '../../api/adminAgents'

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
      onSaved({ ...campaign, ...updated, flowId: flowId || undefined, inboundFlowId: inboundFlowId || undefined, outboundFlowId: outboundFlowId || undefined })
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
              type="number" min={1} value={maxQueueSize}
              onChange={(e) => setMaxQueueSize(Number(e.target.value))}
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-400 mb-1">Queue Timeout (seconds)</label>
            <input
              type="number" min={0} value={queueTimeout}
              onChange={(e) => setQueueTimeout(Number(e.target.value))}
              className="w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
            />
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
