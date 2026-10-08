import { useEffect, useState } from 'react'
import { api } from '../api/client'
import { connectExtension, useExtensionStore } from '../lib/extensionBridge'
import { useCallHealthStore } from '../lib/callHealth'
import { useScreenShareStore } from '../lib/screenRecorder'
import { useChatStore } from '../stores/chatStore'
import { useCallStore } from '../stores/callStore'
import { useSipStore } from '../stores/sipStore'
import { WrenchIcon } from './icons/Icons'

/**
 * Remote fixes, the agent's side (S184): a supervisor asked this portal to run diagnostics, re-check the extension,
 * re-register the softphone, clear a stuck call screen or reload. It does it, tells the agent who did what, and reports
 * the result back to the supervisor.
 */

const LABEL: Record<string, string> = {
  diagnostics: 'checked your portal',
  extension: 're-checked your browser extension',
  reregister: 're-registered your softphone',
  'clear-call': 'cleared your call screen',
  refresh: 'is refreshing your portal',
}

const startedAt = Date.now()

async function report(id: string, ok: boolean, detail: string) {
  await api.post(`/api/v1/remote-actions/${id}/result`, { ok, detail }).catch(() => {})
}

/** Everything a supervisor needs to see why a portal misbehaves — no call content, nothing personal. */
async function diagnostics() {
  const nav = navigator as Navigator & { connection?: { effectiveType?: string; downlink?: number; rtt?: number } }
  const ua = navigator.userAgent
  const browser = /Edg\/(\d+)/.exec(ua) ? `Edge ${/Edg\/(\d+)/.exec(ua)![1]}` : /Chrome\/(\d+)/.exec(ua) ? `Chrome ${/Chrome\/(\d+)/.exec(ua)![1]}` : ua.slice(0, 60)
  let mic = 'unknown'
  try { mic = (await navigator.permissions.query({ name: 'microphone' as PermissionName })).state } catch { /* not supported */ }
  let inputs = -1, outputs = -1
  try {
    const devices = await navigator.mediaDevices.enumerateDevices()
    inputs = devices.filter((d) => d.kind === 'audioinput').length
    outputs = devices.filter((d) => d.kind === 'audiooutput').length
  } catch { /* not allowed */ }
  const ext = useExtensionStore.getState()
  const health = useCallHealthStore.getState().health
  return {
    browser,
    online: navigator.onLine,
    network: nav.connection?.effectiveType ?? null,
    downlinkMbps: nav.connection?.downlink ?? null,
    pageVisible: document.visibilityState === 'visible',
    portalOpenMinutes: Math.round((Date.now() - startedAt) / 60000),
    softphone: useSipStore.getState().registrationStatus,
    extensionNumber: useSipStore.getState().sipExtension ?? null,
    callScreen: useCallStore.getState().callStatus,
    micPermission: mic,
    audioInputs: inputs,
    audioOutputs: outputs,
    extensionInstalled: ext.installed,
    extensionVersion: ext.version,
    screenShare: useScreenShareStore.getState().status,
    teamChat: useChatStore.getState().status,
    callQuality: health?.onCall ? `${health.grade}${health.mos != null ? ` (${health.mos.toFixed(1)})` : ''}${health.micSilent ? ', mic silent' : ''}` : 'not on a call',
  }
}

/** Ask the softphone (it owns the SIP connection) to do a fix, and wait for its answer. */
function softphone(id: string, kind: 'reregister' | 'clear-call'): Promise<{ ok: boolean; detail: string }> {
  return new Promise((resolve) => {
    const timer = setTimeout(() => { window.removeEventListener('cc:softphone-remote-done', on); resolve({ ok: false, detail: 'The softphone didn’t answer.' }) }, 12000)
    const on = (e: Event) => {
      const d = (e as CustomEvent<{ id: string; ok: boolean; detail: string }>).detail
      if (d.id !== id) return
      clearTimeout(timer)
      window.removeEventListener('cc:softphone-remote-done', on)
      resolve({ ok: d.ok, detail: d.detail })
    }
    window.addEventListener('cc:softphone-remote-done', on)
    window.dispatchEvent(new CustomEvent('cc:softphone-remote', { detail: { id, kind } }))
  })
}

export default function RemoteFixesAgent() {
  const [notice, setNotice] = useState<string | null>(null)

  useEffect(() => {
    const on = (e: Event) => {
      const { id, action, by } = (e as CustomEvent<{ id: string; action: string; by: string }>).detail
      setNotice(`${by} ${LABEL[action] ?? 'ran a fix on your portal'}.`)
      void (async () => {
        try {
          switch (action) {
            case 'diagnostics':
              await report(id, true, JSON.stringify(await diagnostics()))
              break
            case 'extension': {
              connectExtension()
              await new Promise((r) => setTimeout(r, 2000))
              const ext = useExtensionStore.getState()
              await report(id, ext.installed, ext.installed ? `The extension answered (version ${ext.version ?? 'unknown'}).` : 'No answer from the extension — it isn’t installed or isn’t allowed on this page (e.g. incognito).')
              break
            }
            case 'reregister':
            case 'clear-call': {
              const r = await softphone(id, action)
              await report(id, r.ok, r.detail)
              break
            }
            case 'refresh':
              await report(id, true, 'Reloading the portal now.')
              setTimeout(() => window.location.reload(), 1200)
              break
          }
        } catch (err) {
          await report(id, false, err instanceof Error ? err.message : 'The fix failed.')
        }
      })()
    }
    window.addEventListener('cc:remote-action', on)
    return () => window.removeEventListener('cc:remote-action', on)
  }, [])

  useEffect(() => {
    if (!notice) return
    const t = setTimeout(() => setNotice(null), 6000)
    return () => clearTimeout(t)
  }, [notice])

  if (!notice) return null
  return (
    <div className="flex items-center gap-2 px-4 py-1.5 bg-sky-900/70 border-b border-sky-800 text-xs text-sky-100">
      <WrenchIcon size={13} />{notice}
    </div>
  )
}
