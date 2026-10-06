import { useEffect, useState } from 'react'
import { exportKeysApi, exportsApi, newTarget, type ConnectionTest, type DeliveryTarget, type DeliveryType, type ExportDefinition } from '../../../api/exports'
import { listAdminCredentials } from '../../../api/adminCredentials'

// Delivery targets (S180, session 2): where an export's files go — SFTP, FTPS, email — each optionally PGP / zip encrypted.
// Passwords and keys live in Credentials (Key Vault); a target only names them. Test connection signs in and shows the
// server's fingerprint; pinning it is what makes the server trusted (an SFTP target won't send until its key is pinned).

const input = 'w-full bg-gray-900 border border-gray-700 rounded px-2 py-1.5 text-sm text-gray-100 focus:outline-none focus:border-indigo-500'
const label = 'block text-xs text-gray-400 mb-1'
const TYPE_LABEL: Record<DeliveryType, string> = { sftp: 'SFTP', ftps: 'FTPS', email: 'Email' }

function CredentialInput({ value, onChange, names, placeholder }: {
  value: string | null | undefined; onChange: (v: string | null) => void; names: string[]; placeholder: string
}) {
  return (
    <>
      <input className={input} list="export-credential-names" value={value ?? ''} placeholder={placeholder}
        onChange={(e) => onChange(e.target.value.trim() || null)} />
      {names.length === 0 && <span className="text-[11px] text-gray-500">Name of a saved credential (Admin → Credentials)</span>}
    </>
  )
}

function TargetForm({ defId, t, set, names, remove }: {
  defId: string; t: DeliveryTarget; set: (t: DeliveryTarget) => void; names: string[]; remove: () => void
}) {
  const [test, setTest] = useState<ConnectionTest | null>(null)
  const [testing, setTesting] = useState(false)
  const patch = (p: Partial<DeliveryTarget>) => set({ ...t, ...p })
  const server = t.type !== 'email'
  const pinned = t.type === 'sftp' ? t.hostKeyFingerprint : t.certificateFingerprint

  async function runTest() {
    setTesting(true); setTest(null)
    try { setTest(await exportsApi.testTarget(defId, t)) }
    catch (e) { setTest({ success: false, message: e instanceof Error ? e.message : 'Test failed.', fingerprint: null, matchesPinned: null }) }
    finally { setTesting(false) }
  }

  return (
    <div className="border border-gray-700 rounded p-3 mb-3">
      <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
        <div className="flex items-center gap-2">
          <span className="px-2 py-0.5 rounded bg-gray-700 text-xs text-gray-200">{TYPE_LABEL[t.type]}</span>
          <label className="flex items-center gap-1.5 text-sm text-gray-300">
            <input type="checkbox" checked={t.enabled} onChange={(e) => patch({ enabled: e.target.checked })} /> On
          </label>
        </div>
        <div className="flex gap-2">
          <button className="px-2.5 py-1 rounded text-sm bg-gray-700 hover:bg-gray-600 text-white disabled:opacity-50" disabled={testing} onClick={runTest}>
            {testing ? 'Testing…' : 'Test connection'}
          </button>
          <button className="px-2.5 py-1 rounded text-sm text-red-300 hover:bg-red-800/60" onClick={remove}>Remove</button>
        </div>
      </div>
      <div className="grid sm:grid-cols-4 gap-3">
        <div className="sm:col-span-2">
          <label className={label}>Name</label>
          <input className={input} value={t.name} onChange={(e) => patch({ name: e.target.value })} placeholder="e.g. Cannella SFTP" />
        </div>
        {server && (
          <>
            <div className="sm:col-span-1">
              <label className={label}>Host</label>
              <input className={input} value={t.host ?? ''} onChange={(e) => patch({ host: e.target.value.trim() || null })} />
            </div>
            <div>
              <label className={label}>Port</label>
              <input type="number" className={input} value={t.port ?? ''} placeholder={t.type === 'sftp' ? '22' : t.ftpsImplicit ? '990' : '21'}
                onChange={(e) => patch({ port: e.target.value ? Number(e.target.value) : null })} />
            </div>
            <div>
              <label className={label}>User name</label>
              <input className={input} value={t.username ?? ''} onChange={(e) => patch({ username: e.target.value.trim() || null })} />
            </div>
            <div>
              <label className={label}>Password (stored credential)</label>
              <CredentialInput value={t.passwordCredential} names={names} placeholder="credential name" onChange={(v) => patch({ passwordCredential: v })} />
            </div>
            {t.type === 'sftp' && (
              <div>
                <label className={label}>Private key (stored credential)</label>
                <CredentialInput value={t.privateKeyCredential} names={names} placeholder="optional" onChange={(v) => patch({ privateKeyCredential: v })} />
              </div>
            )}
            <div>
              <label className={label}>Remote folder</label>
              <input className={input} value={t.remoteDirectory ?? ''} placeholder="/incoming" onChange={(e) => patch({ remoteDirectory: e.target.value || null })} />
            </div>
            {t.type === 'ftps' && (
              <label className="flex items-end gap-2 text-sm text-gray-300 pb-1.5">
                <input type="checkbox" checked={!!t.ftpsImplicit} onChange={(e) => patch({ ftpsImplicit: e.target.checked })} /> Implicit TLS
              </label>
            )}
            <div className="sm:col-span-4">
              <label className={label}>
                {t.type === 'sftp' ? 'Pinned host key (SHA-256) — required to send' : 'Pinned certificate (SHA-256) — optional; blank = the certificate must be valid'}
              </label>
              <input className={`${input} font-mono`} value={pinned ?? ''}
                onChange={(e) => patch(t.type === 'sftp' ? { hostKeyFingerprint: e.target.value.trim() || null } : { certificateFingerprint: e.target.value.trim() || null })} />
            </div>
          </>
        )}
        {t.type === 'email' && (
          <>
            <div className="sm:col-span-2">
              <label className={label}>To (comma-separated)</label>
              <input className={input} value={t.emailTo.join(', ')}
                onChange={(e) => patch({ emailTo: e.target.value.split(',').map((x) => x.trim()).filter(Boolean) })} />
            </div>
            <div className="sm:col-span-4">
              <label className={label}>Subject (Liquid, optional)</label>
              <input className={`${input} font-mono`} value={t.emailSubject ?? ''} placeholder="{{ export.name }} — {{ file_name }}"
                onChange={(e) => patch({ emailSubject: e.target.value || null })} />
            </div>
          </>
        )}
        <div>
          <label className={label}>Encryption</label>
          <select className={input} value={t.encryption} onChange={(e) => patch({ encryption: e.target.value as DeliveryTarget['encryption'] })}>
            <option value="none">None</option>
            <option value="pgp">PGP (vendor's public key)</option>
            <option value="zip">Password zip (AES-256)</option>
          </select>
        </div>
        {t.encryption === 'zip' && (
          <div>
            <label className={label}>Zip password (stored credential)</label>
            <CredentialInput value={t.zipPasswordCredential} names={names} placeholder="credential name" onChange={(v) => patch({ zipPasswordCredential: v })} />
          </div>
        )}
        {t.encryption === 'pgp' && (
          <div className="sm:col-span-4">
            <label className={label}>Vendor's PGP public key</label>
            <textarea className={`${input} font-mono text-xs`} rows={4} value={t.pgpPublicKey ?? ''}
              placeholder="-----BEGIN PGP PUBLIC KEY BLOCK-----" onChange={(e) => patch({ pgpPublicKey: e.target.value || null })} />
          </div>
        )}
      </div>
      {test && (
        <div className={`mt-3 text-sm rounded p-2 ${test.success ? 'bg-emerald-900/30 text-emerald-200' : 'bg-red-900/30 text-red-200'}`}>
          {test.message}
          {test.fingerprint && (
            <div className="mt-1 text-xs text-gray-300">
              Server fingerprint: <code className="text-gray-100">{test.fingerprint}</code>
              {test.matchesPinned === true && <span className="ml-2 text-emerald-300">matches the pinned one</span>}
              {test.matchesPinned === false && <span className="ml-2 text-red-300">DOES NOT match the pinned one — confirm with the vendor before changing it</span>}
              {test.matchesPinned !== true && (
                <button className="ml-2 px-2 py-0.5 rounded bg-sky-700 hover:bg-sky-600 text-white"
                  onClick={() => patch(t.type === 'sftp' ? { hostKeyFingerprint: test.fingerprint } : { certificateFingerprint: test.fingerprint })}>
                  Pin this {t.type === 'sftp' ? 'key' : 'certificate'}
                </button>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  )
}

export default function ExportDeliveryCard({ def, onSaved }: { def: ExportDefinition; onSaved: (d: ExportDefinition) => void }) {
  const [targets, setTargets] = useState<DeliveryTarget[]>(def.deliveryTargets)
  const [dirty, setDirty] = useState(false)
  const [names, setNames] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => { setTargets(def.deliveryTargets); setDirty(false) }, [def.id, def.deliveryTargets])
  useEffect(() => {
    // Saved credentials (needs the credentials permission) plus the SSH keys generated here (Keys, on Data Exports).
    Promise.all([
      listAdminCredentials().then((c) => c.map((x) => x.keyName)).catch(() => [] as string[]),
      exportKeysApi.list().then((k) => k.filter((x) => x.type === 'ssh' && !x.revokedAt).map((x) => x.privateKeyCredential)).catch(() => [] as string[]),
    ]).then(([a, b]) => setNames([...new Set([...b, ...a])]))
  }, [])

  const update = (next: DeliveryTarget[]) => { setTargets(next); setDirty(true) }

  async function save() {
    setSaving(true); setError(null)
    try { onSaved(await exportsApi.saveTargets(def.id, targets)); setDirty(false) }
    catch (e) { setError(e instanceof Error ? e.message : 'Save failed.') }
    finally { setSaving(false) }
  }

  return (
    <div className="bg-gray-800/60 border border-gray-700 rounded-lg p-4 mb-4">
      <datalist id="export-credential-names">{names.map((n) => <option key={n} value={n} />)}</datalist>
      <div className="flex flex-wrap items-center justify-between gap-3 mb-3">
        <h2 className="text-sm font-semibold text-gray-100">Delivery</h2>
        <div className="flex flex-wrap gap-2">
          {(['sftp', 'ftps', 'email'] as const).map((type) => (
            <button key={type} className="px-2.5 py-1 rounded text-sm bg-gray-700 hover:bg-gray-600 text-white"
              onClick={() => update([...targets, newTarget(type)])}>+ {TYPE_LABEL[type]}</button>
          ))}
          <button className="px-3 py-1 rounded text-sm bg-indigo-600 hover:bg-indigo-500 text-white disabled:opacity-50"
            disabled={!dirty || saving} onClick={save}>Save delivery</button>
        </div>
      </div>
      {error && <p className="text-red-400 text-sm mb-2">{error}</p>}
      {targets.length === 0
        ? <p className="text-sm text-gray-500">No delivery targets — files are only downloaded. Add the vendor's server once they release it.</p>
        : targets.map((t, i) => (
          <TargetForm key={t.id} defId={def.id} t={t} names={names}
            set={(nt) => update(targets.map((x, j) => j === i ? nt : x))}
            remove={() => update(targets.filter((_, j) => j !== i))} />
        ))}
      <p className="text-xs text-gray-500">Test files are never sent automatically — use Send on a test file. Real files go out only once the vendor has approved the export.</p>
    </div>
  )
}
