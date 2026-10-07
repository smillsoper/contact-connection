import { useEffect, useState } from 'react'
import { connectExtension, useExtensionStore } from '../lib/extensionBridge'
import { EXTENSION, isEdge, storeUrl, versionAtLeast } from '../config/extension'
import { CheckIcon, ExternalLinkIcon, WarningIcon } from './icons/Icons'

/**
 * What the ContactConnection Agent extension is, whether this browser has it, and how to get it (S183). Used on the
 * /extension page, in the account-setup wizard, and in the agent portal's first-run prompt.
 */

export function useExtensionStatus() {
  const { installed, version } = useExtensionStore()
  useEffect(() => { connectExtension() }, [])
  return { installed, version, outdated: installed && !versionAtLeast(version, EXTENSION.minimumVersion) }
}

/** Install / update button (or the "awaiting approval" note), plus a live "installed" check. */
export function ExtensionInstallButton({ compact = false }: { compact?: boolean }) {
  const { installed, version, outdated } = useExtensionStatus()
  const url = storeUrl()
  const [checking, setChecking] = useState(false)

  if (installed && !outdated)
    return <p className="flex items-center gap-1.5 text-sm text-emerald-400"><CheckIcon size={16} />Installed{version ? ` (version ${version})` : ''}</p>

  return (
    <div className={compact ? '' : 'space-y-2'}>
      {outdated && <p className="flex items-center gap-1.5 text-sm text-amber-300"><WarningIcon size={15} />Version {version} is out of date — update it from the store.</p>}
      <div className="flex flex-wrap items-center gap-3">
        {url ? (
          <a href={url} target="_blank" rel="noreferrer noopener"
            className="inline-flex items-center gap-1.5 bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium">
            {outdated ? 'Update' : 'Add to'} {isEdge() ? 'Edge' : 'Chrome'} <ExternalLinkIcon size={14} />
          </a>
        ) : (
          <span className="text-sm text-amber-300">The extension is awaiting {isEdge() ? 'Edge Add-ons' : 'Chrome Web Store'} approval — your administrator can install it for you in the meantime.</span>
        )}
        <button onClick={() => { setChecking(true); useExtensionStore.setState({ installed: false, version: null }); connectExtension(); setTimeout(() => setChecking(false), 1500) }}
          className="text-sm text-gray-400 hover:text-white underline">{checking ? 'Checking…' : "I've installed it"}</button>
      </div>
    </div>
  )
}

/** The explanation shown wherever we ask for it. */
export function ExtensionExplainer({ forAdmins = false }: { forAdmins?: boolean }) {
  return (
    <div className="space-y-3 text-sm text-gray-300 leading-relaxed">
      <p>The <b className="text-white">ContactConnection Agent</b> extension for Chrome and Edge does two things a web page can't:</p>
      <ul className="list-disc pl-5 space-y-1 text-gray-400">
        <li><b className="text-gray-200">Brings the agent portal forward</b> when a call is offered, auto-connects or a script pops, when a
          supervisor calls, and on a take over — so a call never waits in a background tab.</li>
        <li><b className="text-gray-200">Marks your clicks on recorded calls</b> — only on ContactConnection pages, only on campaigns that
          record agent screens, and only while a call is being recorded (the extension shows a red <b>REC</b> badge then). It never
          records what you type, and it can't see other websites.</li>
      </ul>
      {forAdmins && (
        <div className="rounded-lg border border-gray-700 bg-gray-900/60 px-4 py-3 text-gray-400 space-y-1.5">
          <p className="text-gray-200 font-medium">Install it for everyone</p>
          <p>Push it to every workstation so agents never have to: <b>Google Admin</b> (Chrome → Apps &amp; extensions → Force install),
            <b> Microsoft Intune</b> or <b>Group Policy</b> (ExtensionInstallForcelist) for Chrome and Edge, or bake it into your VDI image.</p>
          <p>Extension ID: <span className="font-mono text-gray-200">{EXTENSION.extensionId || 'issued when the store listing is approved'}</span></p>
        </div>
      )}
    </div>
  )
}
