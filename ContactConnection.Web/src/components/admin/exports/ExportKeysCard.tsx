import { useEffect, useState } from 'react'
import { exportKeysApi, type ExportKeyRow } from '../../../api/exports'

// Vendor keys (S180): generate an SSH key pair (give the vendor the public key; an SFTP target signs in with the private
// key) or a PGP key pair (vendors encrypt files to us). Private keys go straight into the credential store and are never
// shown — only public keys and fingerprints.

function when(iso: string) {
  return new Date(iso).toLocaleDateString(undefined, { dateStyle: 'medium' })
}

export default function ExportKeysCard() {
  const [keys, setKeys] = useState<ExportKeyRow[] | null>(null)
  const [name, setName] = useState('')
  const [type, setType] = useState<'ssh' | 'pgp'>('ssh')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [shown, setShown] = useState<string | null>(null)
  const [copied, setCopied] = useState<string | null>(null)

  const load = () => exportKeysApi.list().then(setKeys).catch((e: Error) => setError(e.message))
  useEffect(() => { load() }, [])

  async function generate() {
    setBusy(true); setError(null)
    try {
      const k = await exportKeysApi.generate(name, type)
      setName(''); setShown(k.id); await load()
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not generate the key.') }
    finally { setBusy(false) }
  }

  async function revoke(k: ExportKeyRow) {
    if (!window.confirm(`Revoke "${k.name}"? Its private key is deleted and anything using it stops working.`)) return
    setError(null)
    try { await exportKeysApi.revoke(k.id); await load() }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not revoke the key.') }
  }

  async function copy(k: ExportKeyRow) {
    try { await navigator.clipboard.writeText(k.publicKey); setCopied(k.id); setTimeout(() => setCopied(null), 1500) }
    catch { setShown(k.id) }
  }

  return (
    <div className="bg-gray-800/60 border border-gray-700 rounded-lg p-4 mt-8">
      <h2 className="text-sm font-semibold text-gray-100 mb-1">Vendor keys</h2>
      <p className="text-xs text-gray-400 mb-3">
        <b className="text-gray-300">SSH</b>: give the vendor the public key; choose the key on an SFTP delivery target to sign in with it.{' '}
        <b className="text-gray-300">PGP</b>: give the vendor the public key so they can encrypt files to you.
        Private keys are kept in your credential store and never shown.
      </p>
      <div className="flex flex-wrap items-end gap-2 mb-4">
        <div className="flex-1 min-w-48">
          <label className="block text-xs text-gray-400 mb-1">Name</label>
          <input className="w-full bg-gray-900 border border-gray-700 rounded px-2 py-1.5 text-sm text-gray-100" value={name}
            onChange={(e) => setName(e.target.value)} placeholder="e.g. Cannella SFTP" />
        </div>
        <select className="bg-gray-900 border border-gray-700 rounded px-2 py-1.5 text-sm text-gray-100" value={type} onChange={(e) => setType(e.target.value as 'ssh' | 'pgp')}>
          <option value="ssh">SSH key pair (SFTP sign-in)</option>
          <option value="pgp">PGP key pair (files sent to us)</option>
        </select>
        <button className="px-3 py-1.5 rounded text-sm bg-indigo-600 hover:bg-indigo-500 text-white disabled:opacity-50" disabled={busy || !name.trim()} onClick={generate}>
          {busy ? 'Generating…' : 'Generate'}
        </button>
      </div>
      {error && <p className="text-red-400 text-sm mb-3">{error}</p>}
      {keys === null ? <p className="text-gray-500 text-sm">Loading…</p> : keys.length === 0 ? <p className="text-gray-500 italic text-sm">No keys yet.</p> : (
        <ul className="space-y-2">
          {keys.map((k) => (
            <li key={k.id} className={`border border-gray-700 rounded p-2 ${k.revokedAt ? 'opacity-50' : ''}`}>
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="text-sm text-gray-100">
                  {k.name} <span className="ml-1 px-1.5 rounded bg-gray-700 text-xs text-gray-300">{k.type.toUpperCase()}</span>
                  <span className="block text-xs text-gray-500 font-mono break-all">{k.fingerprint}</span>
                  <span className="block text-xs text-gray-500">
                    {k.revokedAt ? `Revoked ${when(k.revokedAt)} by ${k.revokedByName}` : `Created ${when(k.createdAt)} by ${k.createdByName}`}
                    {k.type === 'ssh' && !k.revokedAt && <> · stored as <code>{k.privateKeyCredential}</code></>}
                  </span>
                </div>
                {!k.revokedAt && (
                  <div className="flex gap-3 text-sm">
                    <button className="text-indigo-400 hover:text-indigo-300" onClick={() => copy(k)}>{copied === k.id ? 'Copied' : 'Copy public key'}</button>
                    <button className="text-gray-400 hover:text-white" onClick={() => setShown(shown === k.id ? null : k.id)}>{shown === k.id ? 'Hide' : 'Show'}</button>
                    <button className="text-red-400 hover:text-red-300" onClick={() => revoke(k)}>Revoke</button>
                  </div>
                )}
              </div>
              {shown === k.id && !k.revokedAt && (
                <textarea readOnly className="mt-2 w-full bg-gray-950 border border-gray-700 rounded p-2 font-mono text-xs text-gray-200" rows={k.type === 'pgp' ? 8 : 3}
                  value={k.publicKey} onFocus={(e) => e.currentTarget.select()} />
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
