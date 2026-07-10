import { useEffect, useState } from 'react'
import SearchableSelect from '../SearchableSelect'
import { listCampaigns, type Campaign } from '../../api/telephony'
import { flowsApi, type FlowSummary } from '../../api/flows'
import { callTracesApi, type CaptureMode, type StartTraceResult } from '../../api/callTraces'
import type { TracePresetFilters } from './openCallTrace'

const MAX_CAPTURE_COUNT = 500
const MAX_CAPTURE_DURATION_MINUTES = 60

interface Props {
  preset: TracePresetFilters
  onStarted: (result: StartTraceResult) => void
}

export default function CallTraceFilterForm({ preset, onStarted }: Props) {
  const [campaigns, setCampaigns] = useState<Campaign[]>([])
  const [flows, setFlows] = useState<FlowSummary[]>([])
  const [campaignId, setCampaignId] = useState(preset.campaignId ?? '')
  const [flowId, setFlowId] = useState(preset.flowId ?? '')
  const [dnis, setDnis] = useState(preset.dnis ?? '')
  const [ani, setAni] = useState('')
  const [captureMode, setCaptureMode] = useState<CaptureMode>('count')
  const [captureValue, setCaptureValue] = useState(10)
  const [starting, setStarting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([listCampaigns(), flowsApi.listAllByType('telephony')])
      .then(([c, f]) => { setCampaigns(c); setFlows(f) })
      .catch(() => {})
  }, [])

  const captureCap = captureMode === 'duration' ? MAX_CAPTURE_DURATION_MINUTES : MAX_CAPTURE_COUNT
  const captureValid = captureValue >= 1 && captureValue <= captureCap

  async function handleStart() {
    if (!captureValid || starting) return
    setStarting(true)
    setError(null)
    try {
      const result = await callTracesApi.start({
        campaignId: campaignId || undefined,
        flowId: flowId || undefined,
        dnis: dnis || undefined,
        ani: ani || undefined,
        captureMode,
        captureValue,
      })
      onStarted(result)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to start trace.')
    } finally {
      setStarting(false)
    }
  }

  return (
    <div className="p-4 space-y-4">
      <div>
        <label className="block text-gray-400 text-xs font-medium mb-1.5">Campaign</label>
        <SearchableSelect
          options={campaigns.map((c) => ({ value: c.id, label: c.name }))}
          value={campaignId}
          onChange={setCampaignId}
          allLabel="All campaigns"
          className="w-full"
        />
      </div>

      <div>
        <label className="block text-gray-400 text-xs font-medium mb-1.5">Telephony call flow</label>
        <SearchableSelect
          options={flows.map((f) => ({ value: f.id, label: f.name }))}
          value={flowId}
          onChange={setFlowId}
          allLabel="All telephony flows"
          className="w-full"
        />
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-gray-400 text-xs font-medium mb-1.5">DNIS (dialed number)</label>
          <input
            value={dnis}
            onChange={(e) => setDnis(e.target.value)}
            placeholder="Any"
            className="w-full bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 font-mono"
          />
        </div>
        <div>
          <label className="block text-gray-400 text-xs font-medium mb-1.5">ANI (caller number)</label>
          <input
            value={ani}
            onChange={(e) => setAni(e.target.value)}
            placeholder="Any"
            className="w-full bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500 font-mono"
          />
        </div>
      </div>

      <div>
        <label className="block text-gray-400 text-xs font-medium mb-1.5">Capture</label>
        <div className="flex items-center gap-2">
          <select
            value={captureMode}
            onChange={(e) => setCaptureMode(e.target.value as CaptureMode)}
            className="bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
          >
            <option value="count">calls</option>
            <option value="duration">minutes</option>
          </select>
          <input
            type="number"
            min={1}
            max={captureCap}
            value={captureValue}
            onChange={(e) => setCaptureValue(Number(e.target.value))}
            className="w-24 bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500"
          />
          <span className="text-gray-500 text-xs">
            {captureMode === 'duration' ? `max ${MAX_CAPTURE_DURATION_MINUTES} min` : `max ${MAX_CAPTURE_COUNT} calls`}
          </span>
        </div>
        <p className="text-gray-600 text-xs mt-1.5">
          {captureMode === 'count'
            ? 'The trace stops automatically once this many calls have been captured.'
            : 'The trace stops automatically after this many minutes.'}
        </p>
      </div>

      {error && <p className="text-red-400 text-xs">{error}</p>}

      <button
        onClick={handleStart}
        disabled={starting || !captureValid}
        className="w-full bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium transition-colors"
      >
        {starting ? 'Starting…' : 'Start Trace'}
      </button>
    </div>
  )
}
