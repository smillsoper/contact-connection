import { useEffect, useRef } from 'react'
import * as signalR from '@microsoft/signalr'
import { useAuthStore } from '../stores/authStore'
import { useCallTraceStore } from '../stores/callTraceStore'
import type { CallTraceStep } from '../api/callTraces'

/// One shared SignalR connection for every open trace popup — joins one group per
/// running subscription rather than opening a connection per window.
export function useCallTraceHub() {
  const { token, tenantSubdomain } = useAuthStore()
  const joinedRef = useRef<Set<string>>(new Set())
  const windows = useCallTraceStore((s) => s.windows)

  const connectionRef = useRef<signalR.HubConnection | null>(null)

  useEffect(() => {
    if (!token) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/call-trace?access_token=${token}`, {
        headers: { 'X-Tenant-Subdomain': tenantSubdomain ?? '' },
      })
      .withAutomaticReconnect()
      .build()

    function windowIdFor(subscriptionId: string): string | undefined {
      return useCallTraceStore.getState().windows.find((w) => w.subscriptionId === subscriptionId)?.id
    }

    connection.on('receiveCallMatched', (subscriptionId: string, callRecordId: string) => {
      const windowId = windowIdFor(subscriptionId)
      if (windowId) useCallTraceStore.getState().addCallMatched(windowId, callRecordId)
    })

    connection.on('receiveTraceStep', (subscriptionId: string, step: CallTraceStep) => {
      const windowId = windowIdFor(subscriptionId)
      if (windowId) useCallTraceStore.getState().addStep(windowId, step)
    })

    connection.on('receiveCallEnded', (subscriptionId: string, callRecordId: string) => {
      const windowId = windowIdFor(subscriptionId)
      if (windowId) useCallTraceStore.getState().markCallEnded(windowId, callRecordId)
    })

    connection.on('receiveTraceStopped', (subscriptionId: string, reason: string) => {
      const windowId = windowIdFor(subscriptionId)
      if (windowId) useCallTraceStore.getState().setStopped(windowId, reason)
      joinedRef.current.delete(subscriptionId)
    })

    connection.start()
      .then(() => console.log('[SignalR] CallTraceHub connected'))
      .catch((err) => console.error('[SignalR] CallTraceHub connection failed:', err))
    connectionRef.current = connection

    return () => { connection.stop(); connectionRef.current = null }
  }, [token, tenantSubdomain])

  // Join/leave trace groups as windows start running or get removed
  useEffect(() => {
    const connection = connectionRef.current
    if (!connection) return

    const activeSubscriptionIds = new Set(
      windows.filter((w) => w.status === 'running' && w.subscriptionId).map((w) => w.subscriptionId!),
    )

    for (const subscriptionId of activeSubscriptionIds) {
      if (joinedRef.current.has(subscriptionId)) continue
      connection.invoke('JoinTrace', subscriptionId).catch(() => {})
      joinedRef.current.add(subscriptionId)
    }

    for (const subscriptionId of [...joinedRef.current]) {
      if (activeSubscriptionIds.has(subscriptionId)) continue
      connection.invoke('LeaveTrace', subscriptionId).catch(() => {})
      joinedRef.current.delete(subscriptionId)
    }
  }, [windows])
}
