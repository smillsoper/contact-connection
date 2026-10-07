import { useEffect, useState } from 'react'
import { useAuthStore } from '../stores/authStore'
import { ExtensionExplainer, ExtensionInstallButton, useExtensionStatus } from './ExtensionInfo'

/**
 * First sign-in setup step (S183): when the agent portal opens without the ContactConnection Agent extension, ask once
 * to install it. "Later" snoozes it for a day on this browser; the slim bar under the header keeps reminding meanwhile.
 * It closes by itself the moment the extension says hello.
 */
const SNOOZE_HOURS = 24

export default function ExtensionSetupPrompt() {
  const agentId = useAuthStore((s) => s.agentId)
  const { installed, outdated } = useExtensionStatus()
  const [ready, setReady] = useState(false)
  const [dismissed, setDismissed] = useState(false)
  const key = `cc.extension.later.${agentId ?? 'anon'}`

  // Give the extension a moment to answer before deciding it isn't there.
  useEffect(() => { const t = setTimeout(() => setReady(true), 2500); return () => clearTimeout(t) }, [])

  const snoozed = (() => {
    try { const v = Number(localStorage.getItem(key)); return v > 0 && Date.now() - v < SNOOZE_HOURS * 3600_000 } catch { return false }
  })()

  if (!ready || dismissed || snoozed || (installed && !outdated)) return null

  function later() {
    try { localStorage.setItem(key, String(Date.now())) } catch { /* private window — just close */ }
    setDismissed(true)
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-4">
      <div className="w-full max-w-xl bg-gray-900 border border-gray-700 rounded-xl shadow-2xl p-6">
        <p className="text-xs uppercase tracking-wide text-indigo-300 mb-1">Set up your workstation</p>
        <h2 className="text-white text-lg font-semibold mb-3">{outdated ? 'Update the ContactConnection Agent extension' : 'Install the ContactConnection Agent extension'}</h2>
        <ExtensionExplainer />
        <div className="mt-5 pt-4 border-t border-gray-800 flex flex-wrap items-center justify-between gap-3">
          <ExtensionInstallButton compact />
          <button onClick={later} className="text-sm text-gray-500 hover:text-white">Later</button>
        </div>
      </div>
    </div>
  )
}
