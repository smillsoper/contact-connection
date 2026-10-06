import { useEffect, useRef, useState } from 'react'
import QRCode from 'qrcode'
import { clientPortalApi } from '../../api/clientPortal'
import type { ClientProfile } from '../../stores/clientAuthStore'

/** Self-service two-step sign-in for a signed-in client user (S181): turn on (scan + confirm) or off (confirm with a code). */
export default function ClientTwoStep({ profile, onChange, startOpen }: {
  profile: ClientProfile
  onChange: (p: ClientProfile) => void
  startOpen?: boolean
}) {
  const [qr, setQr] = useState<string | null>(null)
  const [secret, setSecret] = useState<string | null>(null)
  const [mode, setMode] = useState<'idle' | 'enable' | 'disable'>('idle')
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const autoStarted = useRef(false)

  async function start() {
    setError(null); setCode('')
    try {
      const d = await clientPortalApi.mfaStart()
      setSecret(d.secret)
      setQr(await QRCode.toDataURL(d.otpAuthUri, { width: 180, margin: 2 }))
      setMode('enable')
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not start setup') }
  }

  // Opened from the set-password page's "set up two-step sign-in now" — go straight to the QR code.
  useEffect(() => {
    if (!startOpen || autoStarted.current || profile.mfaEnabled) return
    autoStarted.current = true
    void start()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [startOpen])

  async function confirm() {
    setBusy(true); setError(null)
    try {
      const p = mode === 'enable' ? await clientPortalApi.mfaEnable(code) : await clientPortalApi.mfaDisable(code)
      onChange(p)
      setMode('idle'); setQr(null); setSecret(null); setCode('')
    } catch (e) { setError(e instanceof Error ? e.message : 'Invalid code') } finally { setBusy(false) }
  }

  return (
    <div className="border-t border-gray-800 pt-4 mt-4">
      <div className="flex items-center justify-between gap-2">
        <div>
          <div className="text-xs text-gray-300">Two-step sign-in</div>
          <div className="text-[11px] text-gray-500">
            {profile.mfaEnabled ? 'On — you enter a code from your authenticator app when you sign in.' : 'Off — recommended: a code from an authenticator app as well as your password.'}
          </div>
        </div>
        {mode === 'idle' && !profile.mfaEnabled && (
          <button className="text-xs bg-indigo-600 hover:bg-indigo-500 text-white px-3 py-1 rounded shrink-0" onClick={start}>Turn on</button>
        )}
        {mode === 'idle' && profile.mfaEnabled && profile.mfaRequirement !== 'on' && (
          <button className="text-xs border border-gray-700 text-gray-300 px-3 py-1 rounded shrink-0" onClick={() => { setMode('disable'); setCode(''); setError(null) }}>Turn off</button>
        )}
        {mode === 'idle' && profile.mfaEnabled && profile.mfaRequirement === 'on' && (
          <span className="text-[11px] text-gray-500 shrink-0">Required</span>
        )}
      </div>
      {mode !== 'idle' && (
        <div className="mt-3 space-y-2">
          {mode === 'enable' && qr && (
            <div className="flex flex-col items-center gap-1">
              <img src={qr} alt="Authenticator QR code" className="rounded bg-white" />
              {secret && <p className="text-[11px] text-gray-500 break-all text-center">Or enter: <span className="font-mono text-gray-300">{secret}</span></p>}
            </div>
          )}
          <p className="text-[11px] text-gray-400">
            {mode === 'enable' ? 'Scan it with an authenticator app (Google Authenticator, Microsoft Authenticator, Authy…), then enter the 6-digit code.' : 'Enter a current code from your authenticator app to turn it off.'}
          </p>
          <div className="flex gap-2">
            <input className="flex-1 bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-white text-center tracking-widest"
              inputMode="numeric" maxLength={6} placeholder="123456" value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} />
            <button className="text-xs bg-indigo-600 hover:bg-indigo-500 text-white px-3 rounded disabled:opacity-50" disabled={busy || code.length !== 6} onClick={confirm}>
              {mode === 'enable' ? 'Turn on' : 'Turn off'}
            </button>
            <button className="text-xs text-gray-400 px-2" onClick={() => { setMode('idle'); setError(null) }}>Cancel</button>
          </div>
        </div>
      )}
      {error && <p className="text-xs text-red-400 mt-2">{error}</p>}
    </div>
  )
}
