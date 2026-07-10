import { useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { useAuthStore } from '../stores/authStore'
import { callTracesApi, type CaptureMode, type CallTraceStep } from '../api/callTraces'
import CallTraceFilterForm from '../components/calltrace/CallTraceFilterForm'
import CallTraceRunningView, { type CallTab } from '../components/calltrace/CallTraceRunningView'
import type { TracePresetFilters } from '../components/calltrace/openCallTrace'

type Status = 'configuring' | 'running' | 'stopped'

/// Rendered inside its own separate browser window (opened via openCallTrace/window.open),
/// not as an in-page modal — this is what makes it movable and independent of the main
/// app's navigation. It manages its own SignalR connection and local state entirely; the
/// only thing shared with the opener window is localStorage (auth token), since window.open
/// on the same origin shares storage automatically.
export default function CallTraceWindowPage() {
  const preset = readPresetFromUrl()
  const { token, tenantSubdomain } = useAuthStore()

  const [status, setStatus] = useState<Status>('configuring')
  const [subscriptionId, setSubscriptionId] = useState<string>()
  const [effectiveCaptureMode, setEffectiveCaptureMode] = useState<CaptureMode>()
  const [effectiveCaptureValue, setEffectiveCaptureValue] = useState<number>()
  const [stopReason, setStopReason] = useState<string>()
  const [calls, setCalls] = useState<CallTab[]>([])
  const [activeCallRecordId, setActiveCallRecordId] = useState<string>()

  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const subscriptionIdRef = useRef<string>()

  useEffect(() => { document.title = 'Call Trace' }, [])

  // One SignalR connection for the lifetime of this popup window.
  useEffect(() => {
    if (!token) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/call-trace?access_token=${token}`, {
        headers: { 'X-Tenant-Subdomain': tenantSubdomain ?? '' },
      })
      .withAutomaticReconnect()
      .build()

    function isOurs(subId: string) {
      return subId === subscriptionIdRef.current
    }

    connection.on('receiveCallMatched', (subId: string, callRecordId: string) => {
      if (!isOurs(subId)) return
      setCalls((prev) => prev.some((c) => c.callRecordId === callRecordId)
        ? prev
        : [...prev, { callRecordId, steps: [], ended: false }])
      setActiveCallRecordId((prev) => prev ?? callRecordId)
    })

    connection.on('receiveTraceStep', (subId: string, step: CallTraceStep) => {
      if (!isOurs(subId)) return
      setCalls((prev) => {
        const exists = prev.some((c) => c.callRecordId === step.callRecordId)
        return exists
          ? prev.map((c) => c.callRecordId === step.callRecordId ? { ...c, steps: [...c.steps, step] } : c)
          : [...prev, { callRecordId: step.callRecordId, steps: [step], ended: false }]
      })
      setActiveCallRecordId((prev) => prev ?? step.callRecordId)
    })

    connection.on('receiveCallEnded', (subId: string, callRecordId: string) => {
      if (!isOurs(subId)) return
      setCalls((prev) => prev.map((c) => c.callRecordId === callRecordId ? { ...c, ended: true } : c))
    })

    connection.on('receiveTraceStopped', (subId: string, reason: string) => {
      if (!isOurs(subId)) return
      setStatus('stopped')
      setStopReason(reason)
    })

    connection.start().catch((err) => console.error('[SignalR] CallTraceHub connection failed:', err))
    connectionRef.current = connection

    return () => { connection.stop() }
  }, [token, tenantSubdomain])

  async function handleStarted(result: { subscriptionId: string; effectiveCaptureMode: CaptureMode; effectiveCaptureValue: number }) {
    subscriptionIdRef.current = result.subscriptionId
    setSubscriptionId(result.subscriptionId)
    setEffectiveCaptureMode(result.effectiveCaptureMode)
    setEffectiveCaptureValue(result.effectiveCaptureValue)
    setStatus('running')
    try {
      await connectionRef.current?.invoke('JoinTrace', result.subscriptionId)
    } catch (err) {
      console.error('[SignalR] JoinTrace failed:', err)
    }
  }

  async function handleStop() {
    if (!subscriptionId) return
    try { await callTracesApi.stop(subscriptionId) } catch { /* server-side sweep will still stop it */ }
  }

  return (
    <div className="h-screen bg-gray-950 flex flex-col overflow-hidden">
      <div className="flex items-center px-4 py-2.5 border-b border-gray-800 shrink-0">
        <span className="text-sm font-semibold text-white">Call Trace</span>
      </div>
      <div className="flex-1 min-h-0 overflow-hidden">
        {status === 'configuring' ? (
          <CallTraceFilterForm preset={preset} onStarted={handleStarted} />
        ) : (
          <CallTraceRunningView
            calls={calls}
            activeCallRecordId={activeCallRecordId}
            onSelectCall={setActiveCallRecordId}
            status={status}
            stopReason={stopReason}
            effectiveCaptureMode={effectiveCaptureMode}
            effectiveCaptureValue={effectiveCaptureValue}
            onStop={handleStop}
          />
        )}
      </div>
    </div>
  )
}

function readPresetFromUrl(): TracePresetFilters {
  const params = new URLSearchParams(window.location.search)
  return {
    campaignId: params.get('campaignId') ?? undefined,
    flowId: params.get('flowId') ?? undefined,
    dnis: params.get('dnis') ?? undefined,
  }
}
