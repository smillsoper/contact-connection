import { useEffect, useMemo, useRef, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import { api } from '../../api/client'
import HelpdeskEditor, { type HelpdeskEditorHandle } from '../../components/helpdesk/HelpdeskEditor'
import { HelpdeskAttachments, HelpdeskBody, type Helpdesk, type HelpdeskFile, type HelpdeskTopic } from '../../components/helpdesk/HelpdeskTopicView'
import { uploadHelpdeskAttachment } from '../../lib/helpdeskFiles'
import { formatBytes } from '../../lib/chatImages'
import { ChevronDownIcon, ChevronUpIcon, CloseIcon, DeleteIcon, EditIcon, FileIcon, HelpDeskIcon, PaperclipIcon } from '../../components/icons/Icons'

/**
 * Help Desks (S184, permission helpdesk.manage): reference topics agents open on a call. Each help desk has a name, the
 * campaigns whose agents see it, and topics — formatted text, embedded images, attached files and links. When a
 * campaign has several help desks the agent gets a tab for each, titled with its name.
 */

interface Summary { id: string; name: string; description: string | null; campaignIds: string[]; isActive: boolean; topicCount: number }
interface CampaignOpt { id: string; name: string; client: string; status: string }

const base = '/api/v1/admin/helpdesks'
const input = 'w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-indigo-500'

export default function AdminHelpdesksPage() {
  const [list, setList] = useState<Summary[] | null>(null)
  const [campaigns, setCampaigns] = useState<CampaignOpt[]>([])
  const [selected, setSelected] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = () => api.get<Summary[]>(base).then(setList).catch((e: Error) => setError(e.message))
  useEffect(() => {
    load()
    api.get<CampaignOpt[]>(`${base}/campaigns`).then(setCampaigns).catch(() => {})
  }, [])

  const campaignName = (id: string) => campaigns.find((c) => c.id === id)?.name ?? 'Removed campaign'

  return (
    <AdminShell>
      <div className="p-6 max-w-6xl">
        <div className="flex items-start justify-between mb-6 gap-4">
          <div>
            <h1 className="text-white text-xl font-semibold flex items-center gap-2"><span className="text-yellow-300"><HelpDeskIcon size={22} /></span>Help Desks</h1>
            <p className="text-gray-500 text-sm mt-0.5">
              Reference topics agents open on a call with the yellow Help desk button. Assign a help desk to campaigns; when a
              campaign has more than one, agents get a tab for each.
            </p>
          </div>
          <button onClick={() => { setCreating(true); setSelected(null) }}
            className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium shrink-0">New help desk</button>
        </div>
        {error && <p className="mb-4 text-sm text-red-400">{error}</p>}

        <div className="flex flex-col lg:flex-row gap-5">
          <div className="lg:w-72 shrink-0 space-y-2">
            {!list && <p className="text-gray-400 text-sm">Loading…</p>}
            {list?.length === 0 && !creating && <p className="text-gray-500 text-sm">No help desks yet.</p>}
            {list?.map((h) => (
              <button key={h.id} onClick={() => { setSelected(h.id); setCreating(false) }}
                className={`w-full text-left rounded-xl border px-4 py-3 transition-colors ${selected === h.id
                  ? 'border-indigo-500 bg-indigo-950/30' : 'border-gray-800 bg-gray-900 hover:border-gray-700'}`}>
                <div className="flex items-center gap-2">
                  <span className="text-white text-sm font-medium truncate flex-1">{h.name}</span>
                  {!h.isActive && <span className="text-[10px] text-gray-400 border border-gray-700 rounded px-1">off</span>}
                </div>
                <p className="text-xs text-gray-500 mt-0.5">
                  {h.topicCount} topic{h.topicCount === 1 ? '' : 's'} · {h.campaignIds.length === 0 ? 'no campaigns'
                    : h.campaignIds.length <= 2 ? h.campaignIds.map(campaignName).join(', ') : `${h.campaignIds.length} campaigns`}
                </p>
              </button>
            ))}
          </div>

          <div className="flex-1 min-w-0">
            {creating && (
              <DeskSettings campaigns={campaigns} onCancel={() => setCreating(false)}
                onSaved={(h) => { setCreating(false); setSelected(h.id); load() }} />
            )}
            {selected && !creating && (
              <DeskDetail key={selected} id={selected} campaigns={campaigns} onChanged={load}
                onDeleted={() => { setSelected(null); load() }} />
            )}
            {!selected && !creating && list && list.length > 0 && (
              <p className="text-gray-500 text-sm mt-2">Pick a help desk to edit its topics.</p>
            )}
          </div>
        </div>
      </div>
    </AdminShell>
  )
}

// ── Name, campaigns, on/off ──────────────────────────────────────────────────

function DeskSettings({ desk, campaigns, onSaved, onCancel }: {
  desk?: Helpdesk; campaigns: CampaignOpt[]; onSaved: (h: Helpdesk) => void; onCancel?: () => void
}) {
  const [name, setName] = useState(desk?.name ?? '')
  const [description, setDescription] = useState(desk?.description ?? '')
  const [ids, setIds] = useState<string[]>(desk?.campaignIds ?? [])
  const [active, setActive] = useState(desk?.isActive ?? true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const byClient = useMemo(() => {
    const m = new Map<string, CampaignOpt[]>()
    for (const c of campaigns) m.set(c.client || 'No client', [...(m.get(c.client || 'No client') ?? []), c])
    return [...m.entries()]
  }, [campaigns])

  async function save() {
    setBusy(true); setError(null); setSaved(false)
    try {
      const body = { name, description: description || null, campaignIds: ids, isActive: active }
      const h = desk ? await api.put<Helpdesk>(`${base}/${desk.id}`, body) : await api.post<Helpdesk>(base, body)
      onSaved(h)
      if (desk) { setSaved(true); setTimeout(() => setSaved(false), 2500) }
    } catch (e) { setError(e instanceof Error ? e.message : 'Save failed.') }
    finally { setBusy(false) }
  }

  return (
    <div className="bg-gray-900 rounded-xl border border-gray-800 p-5 space-y-4">
      <div className="grid sm:grid-cols-2 gap-4">
        <label className="block">
          <span className="text-xs text-gray-400">Name — the agent's tab title</span>
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={80} className={`${input} mt-1`} placeholder="e.g. NeuroQ product help" />
        </label>
        <label className="block">
          <span className="text-xs text-gray-400">Description (optional)</span>
          <input value={description} onChange={(e) => setDescription(e.target.value)} maxLength={300} className={`${input} mt-1`} />
        </label>
      </div>
      <div>
        <span className="text-xs text-gray-400">Campaigns whose agents see it</span>
        {campaigns.length === 0 && <p className="text-xs text-gray-500 mt-1">No campaigns yet.</p>}
        <div className="mt-1 grid sm:grid-cols-2 gap-x-6 gap-y-3 max-h-60 overflow-y-auto pr-1">
          {byClient.map(([client, cs]) => (
            <div key={client}>
              <div className="text-[11px] uppercase tracking-wide text-gray-500 mb-1">{client}</div>
              {cs.map((c) => (
                <label key={c.id} className="flex items-center gap-2 text-sm text-gray-200 py-0.5 cursor-pointer">
                  <input type="checkbox" checked={ids.includes(c.id)} className="accent-indigo-500"
                    onChange={(e) => setIds((cur) => (e.target.checked ? [...cur, c.id] : cur.filter((x) => x !== c.id)))} />
                  <span className="truncate">{c.name}</span>
                  {c.status !== 'active' && <span className="text-[10px] text-gray-500">{c.status}</span>}
                </label>
              ))}
            </div>
          ))}
        </div>
      </div>
      <label className="flex items-center gap-2 text-sm text-gray-200 cursor-pointer w-fit">
        <input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} className="accent-indigo-500" />
        On — agents can open it
      </label>
      {error && <p className="text-sm text-red-400">{error}</p>}
      <div className="flex items-center gap-2">
        <button onClick={() => void save()} disabled={busy}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium">
          {desk ? 'Save settings' : 'Create help desk'}
        </button>
        {onCancel && <button onClick={onCancel} className="text-sm text-gray-400 hover:text-white px-3 py-2">Cancel</button>}
        {saved && <span className="text-xs text-emerald-400">Saved</span>}
      </div>
    </div>
  )
}

// ── One help desk: settings + topics ─────────────────────────────────────────

function DeskDetail({ id, campaigns, onChanged, onDeleted }: {
  id: string; campaigns: CampaignOpt[]; onChanged: () => void; onDeleted: () => void
}) {
  const [desk, setDesk] = useState<Helpdesk | null>(null)
  const [editing, setEditing] = useState<HelpdeskTopic | 'new' | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<string | null>(null)   // 'desk' or a topic id
  const [error, setError] = useState<string | null>(null)

  const load = () => api.get<Helpdesk>(`${base}/${id}`).then(setDesk).catch((e: Error) => setError(e.message))
  useEffect(() => { load() }, [id]) // eslint-disable-line react-hooks/exhaustive-deps

  if (!desk) return <p className="text-gray-400 text-sm">{error ?? 'Loading…'}</p>

  async function move(i: number, dir: -1 | 1) {
    const order = desk!.topics.map((t) => t.id)
    const j = i + dir
    if (j < 0 || j >= order.length) return
    ;[order[i], order[j]] = [order[j], order[i]]
    setDesk({ ...desk!, topics: order.map((tid) => desk!.topics.find((t) => t.id === tid)!) })
    try { await api.put(`${base}/${id}/topic-order`, { topicIds: order }) } catch (e) { setError(e instanceof Error ? e.message : 'Failed.'); load() }
  }

  async function removeTopic(t: HelpdeskTopic) {
    try { await api.delete(`${base}/${id}/topics/${t.id}`); setConfirm(null); load(); onChanged() }
    catch (e) { setError(e instanceof Error ? e.message : 'Delete failed.') }
  }

  async function removeDesk() {
    try { await api.delete(`${base}/${id}`); onDeleted() }
    catch (e) { setError(e instanceof Error ? e.message : 'Delete failed.') }
  }

  return (
    <div className="space-y-5">
      <DeskSettings desk={desk} campaigns={campaigns} onSaved={(h) => { setDesk(h); onChanged() }} />

      <div className="bg-gray-900 rounded-xl border border-gray-800">
        <div className="flex items-center justify-between px-5 py-3 border-b border-gray-800">
          <h2 className="text-white text-sm font-semibold">Topics</h2>
          {!editing && (
            <button onClick={() => setEditing('new')} className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-3 py-1.5 text-xs font-medium">New topic</button>
          )}
        </div>
        {error && <p className="text-sm text-red-400 px-5 pt-3">{error}</p>}
        {editing && (
          <TopicEditor key={editing === 'new' ? 'new' : editing.id} helpdeskId={id} topic={editing === 'new' ? null : editing}
            onCancel={() => setEditing(null)} onSaved={() => { setEditing(null); load(); onChanged() }} />
        )}
        {!editing && desk.topics.length === 0 && <p className="text-sm text-gray-500 px-5 py-4">No topics yet — add the first one.</p>}
        {!editing && desk.topics.map((t, i) => (
          <div key={t.id} className="border-b border-gray-800 last:border-0">
            <div className="flex items-center gap-2 px-5 py-2.5">
              <div className="flex flex-col">
                <button onClick={() => void move(i, -1)} disabled={i === 0} title="Move up" className="text-gray-500 hover:text-white disabled:opacity-20"><ChevronUpIcon size={14} /></button>
                <button onClick={() => void move(i, 1)} disabled={i === desk.topics.length - 1} title="Move down" className="text-gray-500 hover:text-white disabled:opacity-20"><ChevronDownIcon size={14} /></button>
              </div>
              <button onClick={() => setPreview(preview === t.id ? null : t.id)} className="flex-1 min-w-0 text-left">
                <p className="text-sm text-white truncate">{t.title}</p>
                <p className="text-[11px] text-gray-500">
                  {t.attachments.length > 0 && <>{t.attachments.length} file{t.attachments.length === 1 ? '' : 's'} · </>}
                  updated {new Date(t.updatedAt).toLocaleString()}{t.updatedByName ? ` by ${t.updatedByName}` : ''}
                </p>
              </button>
              {confirm === t.id ? (
                <span className="flex items-center gap-2 text-xs">
                  <span className="text-gray-300">Delete this topic?</span>
                  <button onClick={() => void removeTopic(t)} className="text-red-400 hover:text-red-300 font-medium">Delete</button>
                  <button onClick={() => setConfirm(null)} className="text-gray-400 hover:text-white">Keep</button>
                </span>
              ) : (
                <>
                  <button onClick={() => setEditing(t)} title="Edit" className="p-1.5 text-gray-400 hover:text-white"><EditIcon size={15} /></button>
                  <button onClick={() => setConfirm(t.id)} title="Delete" className="p-1.5 text-gray-400 hover:text-red-400"><DeleteIcon size={15} /></button>
                </>
              )}
            </div>
            {preview === t.id && (
              <div className="px-12 pb-4">
                <p className="text-[11px] uppercase tracking-wide text-gray-500 mb-2">As agents see it</p>
                <div className="border border-gray-800 rounded-lg p-4 bg-gray-950/40">
                  <HelpdeskBody html={t.html} />
                  <HelpdeskAttachments files={t.attachments} />
                </div>
              </div>
            )}
          </div>
        ))}
      </div>

      <div className="flex items-center gap-3 text-sm">
        {confirm === 'desk' ? (
          <>
            <span className="text-gray-300">Delete “{desk.name}” and all of its topics and files? This can't be undone.</span>
            <button onClick={() => void removeDesk()} className="text-red-400 hover:text-red-300 font-medium">Delete help desk</button>
            <button onClick={() => setConfirm(null)} className="text-gray-400 hover:text-white">Keep it</button>
          </>
        ) : (
          <button onClick={() => setConfirm('desk')} className="text-gray-500 hover:text-red-400 flex items-center gap-1.5">
            <DeleteIcon size={14} />Delete this help desk
          </button>
        )}
      </div>
    </div>
  )
}

// ── Writing a topic ──────────────────────────────────────────────────────────

function TopicEditor({ helpdeskId, topic, onSaved, onCancel }: {
  helpdeskId: string; topic: HelpdeskTopic | null; onSaved: () => void; onCancel: () => void
}) {
  const [title, setTitle] = useState(topic?.title ?? '')
  const [files, setFiles] = useState<HelpdeskFile[]>(topic?.attachments ?? [])
  const [uploading, setUploading] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const editorRef = useRef<HelpdeskEditorHandle>(null)
  const attachRef = useRef<HTMLInputElement>(null)

  async function attach(chosen: File[]) {
    for (const f of chosen.slice(0, 20)) {
      setUploading((n) => n + 1); setError(null)
      try {
        const r = await uploadHelpdeskAttachment(helpdeskId, f)
        setFiles((cur) => (cur.length >= 20 ? cur : [...cur, r]))
      } catch (e) { setError(e instanceof Error ? e.message : 'Upload failed.') }
      finally { setUploading((n) => n - 1) }
    }
  }

  async function save() {
    if (editorRef.current?.isUploading() || uploading > 0) { setError('Wait for the uploads to finish.'); return }
    setBusy(true); setError(null)
    try {
      const body = { title, html: editorRef.current?.getHtml() ?? '', attachmentIds: files.map((f) => f.id) }
      if (topic) await api.put(`${base}/${helpdeskId}/topics/${topic.id}`, body)
      else await api.post(`${base}/${helpdeskId}/topics`, body)
      onSaved()
    } catch (e) { setError(e instanceof Error ? e.message : 'Save failed.') }
    finally { setBusy(false) }
  }

  return (
    <div className="p-5 space-y-3">
      <input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={150} placeholder="Topic title — e.g. Return policy"
        className={`${input} text-base font-medium`} autoFocus={!topic} />
      <HelpdeskEditor ref={editorRef} helpdeskId={helpdeskId} initialHtml={topic?.html ?? ''} />
      <div>
        <div className="flex items-center gap-2">
          <button onClick={() => attachRef.current?.click()}
            className="flex items-center gap-1.5 text-xs text-gray-200 border border-gray-700 hover:border-gray-500 rounded-md px-2.5 py-1.5">
            <PaperclipIcon size={14} />Attach files
          </button>
          <span className="text-[11px] text-gray-500">{uploading > 0 ? 'Uploading…' : 'Up to 25 MB each — agents download them.'}</span>
        </div>
        {files.length > 0 && (
          <div className="flex flex-wrap gap-2 mt-2">
            {files.map((f) => (
              <span key={f.id} className="flex items-center gap-1.5 text-xs bg-gray-800 border border-gray-700 rounded-md px-2 py-1 text-gray-200 max-w-[260px]">
                <FileIcon size={13} className="text-amber-300" /><span className="truncate">{f.name}</span>
                <span className="text-gray-500 shrink-0">{formatBytes(f.sizeBytes)}</span>
                <button onClick={() => setFiles((cur) => cur.filter((x) => x.id !== f.id))} title="Remove" className="text-gray-500 hover:text-white"><CloseIcon size={12} /></button>
              </span>
            ))}
          </div>
        )}
      </div>
      {error && <p className="text-sm text-red-400">{error}</p>}
      <div className="flex items-center gap-2">
        <button onClick={() => void save()} disabled={busy}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium">Save topic</button>
        <button onClick={onCancel} className="text-sm text-gray-400 hover:text-white px-3 py-2">Cancel</button>
      </div>
      <input ref={attachRef} type="file" multiple className="hidden"
        onChange={(e) => { const f = Array.from(e.target.files ?? []); e.target.value = ''; if (f.length) void attach(f) }} />
    </div>
  )
}
