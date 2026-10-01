import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  AD_TYPES, MEDIA_TYPES, mediaApi,
  type MediaAgency, type MediaAssignment, type MediaAssignmentList, type MediaMarketType,
} from '../../api/media'

// A phone number's media assignments (S171, Media Agency Phase A): what it's attributed to now, its
// history, and adding/editing assignments. National: one at a time — a new one ends the previous one the
// day before. Local: several stations at once, one marked default — used until Phase B attributes each
// call to the station closest to the caller. No markets: that mapping is the agency's job.

const inputCls = 'w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-2 text-white text-sm'

interface FormState {
  marketType: MediaMarketType
  mediaAgencyId: string
  station: string
  mediaType: string
  adType: string
  startDate: string
  endDate: string
  isDefaultLocal: boolean
  fieldValues: Record<string, string>
}

function emptyForm(today: string): FormState {
  return { marketType: 'national', mediaAgencyId: '', station: '', mediaType: '', adType: '', startDate: today, endDate: '', isDefaultLocal: false, fieldValues: {} }
}

function AssignmentForm({ agencies, editing, today, onSave, onCancel }: {
  agencies: MediaAgency[]
  editing: MediaAssignment | null
  today: string
  onSave: (f: FormState) => Promise<void>
  onCancel: () => void
}) {
  const [f, setF] = useState<FormState>(editing ? {
    marketType: editing.marketType, mediaAgencyId: editing.mediaAgencyId, station: editing.station,
    mediaType: editing.mediaType ?? '', adType: editing.adType ?? '',
    startDate: editing.startDate, endDate: editing.endDate ?? '', isDefaultLocal: editing.isDefaultLocal,
    fieldValues: { ...editing.fieldValues },
  } : emptyForm(today))
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const set = (p: Partial<FormState>) => setF((s) => ({ ...s, ...p }))
  const agency = agencies.find((a) => a.id === f.mediaAgencyId)
  const fixed = editing !== null   // type, agency and start date are fixed once created

  async function save() {
    setError(null)
    if (!f.mediaAgencyId) { setError('Choose an agency.'); return }
    if (!f.station.trim()) { setError('Station is required.'); return }
    setSaving(true)
    try { await onSave(f) } catch (e) { setError(e instanceof Error ? e.message : 'Save failed.'); setSaving(false) }
  }

  return (
    <div className="border border-indigo-800 bg-gray-900/60 rounded-lg p-4 space-y-3">
      <p className="text-sm font-medium text-white">{editing ? 'Edit assignment' : 'New assignment'}</p>
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <label className="block">
          <span className="block text-xs text-gray-400 mb-1">Market</span>
          <select value={f.marketType} disabled={fixed} onChange={(e) => set({ marketType: e.target.value as MediaMarketType })} className={`${inputCls} disabled:opacity-60`}>
            <option value="national">National</option>
            <option value="local">Local</option>
          </select>
        </label>
        <label className="block">
          <span className="block text-xs text-gray-400 mb-1">Agency</span>
          <select value={f.mediaAgencyId} disabled={fixed} onChange={(e) => set({ mediaAgencyId: e.target.value })} className={`${inputCls} disabled:opacity-60`}>
            <option value="">Choose…</option>
            {agencies.filter((a) => a.isActive || a.id === f.mediaAgencyId).map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
          </select>
        </label>
        <label className="block">
          <span className="block text-xs text-gray-400 mb-1">Start date</span>
          <input type="date" value={f.startDate} disabled={fixed} onChange={(e) => set({ startDate: e.target.value })} className={`${inputCls} disabled:opacity-60`} />
        </label>
        <label className="block">
          <span className="block text-xs text-gray-400 mb-1">Station</span>
          <input value={f.station} onChange={(e) => set({ station: e.target.value })} className={inputCls} placeholder="e.g. CNN, KDFW" />
        </label>
        <label className="block">
          <span className="block text-xs text-gray-400 mb-1">Media type</span>
          <input list="cc-media-types" value={f.mediaType} onChange={(e) => set({ mediaType: e.target.value })} className={inputCls} />
        </label>
        <label className="block">
          <span className="block text-xs text-gray-400 mb-1">Ad type</span>
          <input list="cc-ad-types" value={f.adType} onChange={(e) => set({ adType: e.target.value })} className={inputCls} />
        </label>
        {f.marketType === 'local' && (
          <label className="block">
            <span className="block text-xs text-gray-400 mb-1">End date (optional)</span>
            <input type="date" value={f.endDate} onChange={(e) => set({ endDate: e.target.value })} className={inputCls} />
          </label>
        )}
      </div>
      <datalist id="cc-media-types">{MEDIA_TYPES.map((m) => <option key={m} value={m} />)}</datalist>
      <datalist id="cc-ad-types">{AD_TYPES.map((m) => <option key={m} value={m} />)}</datalist>

      {f.marketType === 'local' && (
        <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
          <input type="checkbox" checked={f.isDefaultLocal} onChange={(e) => set({ isDefaultLocal: e.target.checked })} className="accent-indigo-600" />
          Default local station (used for calls until each call is matched to the station closest to the caller)
        </label>
      )}

      {agency && agency.fields.length > 0 && (
        <div>
          <p className="text-xs text-gray-400 mb-1">{agency.name} fields</p>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            {agency.fields.map((fd) => (
              <label key={fd.name} className="block">
                <span className="block text-[11px] text-gray-500 font-mono">{fd.name}{fd.required ? ' *' : ''}</span>
                <input value={f.fieldValues[fd.name] ?? ''}
                  onChange={(e) => set({ fieldValues: { ...f.fieldValues, [fd.name]: e.target.value } })}
                  className="w-full bg-gray-800 border border-gray-600 rounded-lg px-3 py-1.5 text-white text-sm" />
              </label>
            ))}
          </div>
        </div>
      )}

      {f.marketType === 'national' && !editing && (
        <p className="text-[11px] text-gray-500">A new National assignment ends the current one the day before its start date.</p>
      )}
      {error && <p className="text-red-400 text-sm">{error}</p>}
      <div className="flex justify-end gap-2">
        <button onClick={onCancel} className="px-3 py-1.5 text-sm text-gray-300 hover:text-white">Cancel</button>
        <button onClick={save} disabled={saving} className="px-4 py-1.5 bg-indigo-600 hover:bg-indigo-500 text-white text-sm rounded-lg disabled:opacity-50">
          {saving ? 'Saving…' : 'Save'}
        </button>
      </div>
    </div>
  )
}

export default function MediaAssignmentsModal({ phoneNumberId, number, clientNumber, onClose }: {
  phoneNumberId: string
  number: string
  clientNumber?: string | null
  onClose: () => void
}) {
  const [data, setData] = useState<MediaAssignmentList | null>(null)
  const [agencies, setAgencies] = useState<MediaAgency[]>([])
  const [form, setForm] = useState<MediaAssignment | 'new' | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = () => mediaApi.assignments(phoneNumberId).then(setData).catch((e: Error) => setError(e.message))
  useEffect(() => {
    load()
    mediaApi.agencies().then(setAgencies).catch(() => {})
  }, [phoneNumberId]) // eslint-disable-line react-hooks/exhaustive-deps

  async function save(f: FormState) {
    const input = {
      marketType: f.marketType, mediaAgencyId: f.mediaAgencyId, station: f.station.trim(),
      mediaType: f.mediaType || null, adType: f.adType || null,
      startDate: f.startDate, endDate: f.endDate || null, isDefaultLocal: f.isDefaultLocal, fieldValues: f.fieldValues,
    }
    if (form === 'new') await mediaApi.addAssignment(phoneNumberId, input)
    else if (form) await mediaApi.updateAssignment(form.id, input)
    setForm(null)
    await load()
  }

  async function remove(a: MediaAssignment) {
    setError(null)
    try { await mediaApi.deleteAssignment(a.id); await load() }
    catch (e) { setError(e instanceof Error ? e.message : 'Delete failed.') }
  }

  const current = data?.assignments.find((a) => a.id === data.currentAssignmentId)

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4" onClick={onClose}>
      <div className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-3xl max-h-[90vh] flex flex-col" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-700">
          <div>
            <h2 className="text-lg font-semibold text-white">Media — <span className="font-mono">{clientNumber || number}</span></h2>
            {clientNumber && <p className="text-xs text-gray-500">Delivered on {number}</p>}
          </div>
          <button onClick={onClose} className="text-gray-400 hover:text-white text-xl leading-none">&times;</button>
        </div>
        <div className="overflow-y-auto flex-1 px-6 py-4 space-y-4">
          {agencies.length === 0 && (
            <p className="text-xs text-amber-300">
              No media agencies yet — add one under <Link to="/admin/media-agencies" className="underline">Media Agencies</Link> first.
            </p>
          )}
          <div className="rounded-lg border border-gray-700 px-4 py-3">
            <p className="text-xs text-gray-500 mb-1">Calls today ({data?.today}) are attributed to</p>
            {current ? (
              <p className="text-sm text-white">
                {current.agencyName} · {current.station} · {current.marketType}
                {current.mediaType ? ` · ${current.mediaType}` : ''}{current.adType ? ` · ${current.adType}` : ''}
              </p>
            ) : <p className="text-sm text-gray-400 italic">Nothing — calls on this number aren't attributed.</p>}
          </div>

          {form ? (
            <AssignmentForm agencies={agencies} editing={form === 'new' ? null : form} today={data?.today ?? ''}
              onSave={save} onCancel={() => setForm(null)} />
          ) : (
            <button onClick={() => setForm('new')} disabled={agencies.length === 0}
              className="px-3 py-1.5 text-sm bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded-lg">
              + New assignment
            </button>
          )}
          {error && <p className="text-red-400 text-sm">{error}</p>}

          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-2">History</p>
            {!data ? <p className="text-gray-400 text-sm">Loading…</p> : data.assignments.length === 0 ? (
              <p className="text-gray-500 text-sm italic">No assignments yet.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-xs">
                  <thead>
                    <tr className="text-left text-gray-500 border-b border-gray-800">
                      <th className="py-1 pr-3 font-medium">Dates</th>
                      <th className="py-1 pr-3 font-medium">Market</th>
                      <th className="py-1 pr-3 font-medium">Agency / station</th>
                      <th className="py-1 pr-3 font-medium">Type</th>
                      <th className="py-1 pr-3 font-medium">Fields</th>
                      <th className="py-1" />
                    </tr>
                  </thead>
                  <tbody>
                    {data.assignments.map((a) => (
                      <tr key={a.id} className={`border-b border-gray-800/60 ${a.id === data.currentAssignmentId ? 'bg-emerald-950/30' : ''}`}>
                        <td className="py-1.5 pr-3 text-gray-200 whitespace-nowrap">
                          {a.startDate} → {a.endDate ?? 'open'}
                          {a.id === data.currentAssignmentId && <span className="ml-1 text-emerald-400">● now</span>}
                        </td>
                        <td className="py-1.5 pr-3 text-gray-300">
                          {a.marketType}{a.isDefaultLocal ? ' (default)' : ''}
                        </td>
                        <td className="py-1.5 pr-3 text-gray-200">{a.agencyName} · {a.station}</td>
                        <td className="py-1.5 pr-3 text-gray-300">{[a.mediaType, a.adType].filter(Boolean).join(' / ') || '—'}</td>
                        <td className="py-1.5 pr-3 text-gray-400 font-mono">
                          {Object.entries(a.fieldValues).filter(([, v]) => v).map(([k, v]) => `${k}=${v}`).join(' · ') || '—'}
                        </td>
                        <td className="py-1.5 text-right whitespace-nowrap">
                          <button onClick={() => setForm(a)} className="text-indigo-400 hover:text-indigo-300 mr-2">Edit</button>
                          <button onClick={() => remove(a)} className="text-gray-500 hover:text-red-400" title="Delete — for mistakes. Past calls keep their copy.">Delete</button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
