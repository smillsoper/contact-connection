import { useEffect, useMemo, useState } from 'react'
import AdminShell from '../../components/admin/AdminShell'
import { api } from '../../api/client'
import { chatAdminApi, type AdminChatChannel, type SaveChatChannel } from '../../api/chat'

/**
 * Team Chat configuration (S183, permission chat.manage) — the one place channels are created and configured:
 * privacy, who may post, who is assigned (people and roles) and whether they can leave, and retiring.
 */

interface Person { id: string; name: string; email: string; roleId: string | null }
interface RoleOpt { id: string; name: string }

const EMPTY: SaveChatChannel = {
  name: '', description: null, isPrivate: false, postingRestricted: false, posterIds: [], posterRoleIds: [], membershipLocked: false,
  assignedRoleIds: [], assignedIds: [],
}

export default function AdminChatPage() {
  const [channels, setChannels] = useState<AdminChatChannel[] | null>(null)
  const [enabled, setEnabled] = useState(true)
  const [people, setPeople] = useState<Person[]>([])
  const [roles, setRoles] = useState<RoleOpt[]>([])
  const [editing, setEditing] = useState<{ id: string | null; form: SaveChatChannel } | null>(null)
  const [error, setError] = useState<string | null>(null)

  function load() {
    chatAdminApi.list().then((r) => { setChannels(r.channels); setEnabled(r.enabled) }).catch((e: Error) => setError(e.message))
  }
  useEffect(() => {
    load()
    api.get<{ people: Person[]; roles: RoleOpt[] }>('/api/v1/chat/admin/directory')
      .then((d) => { setPeople(d.people); setRoles(d.roles) }).catch(() => {})
  }, [])

  const active = (channels ?? []).filter((c) => !c.retired)
  const retired = (channels ?? []).filter((c) => c.retired)

  async function retire(c: AdminChatChannel, on: boolean) {
    try { await (on ? chatAdminApi.retire(c.id) : chatAdminApi.unretire(c.id)); load() }
    catch (e) { setError(e instanceof Error ? e.message : 'Failed.') }
  }

  return (
    <AdminShell>
      <div className="p-6 max-w-5xl">
        <div className="flex items-start justify-between mb-6 gap-4">
          <div>
            <h1 className="text-white text-xl font-semibold">Team Chat</h1>
            <p className="text-gray-500 text-sm mt-0.5">
              Channels and who's in them. Anyone can message anyone directly; channels are set up here.
              Each agent's own supervisors are set on the Agents page.
            </p>
          </div>
          <button onClick={() => setEditing({ id: null, form: { ...EMPTY } })}
            className="bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg px-4 py-2 text-sm font-medium shrink-0">New channel</button>
        </div>
        {!enabled && (
          <p className="mb-4 rounded-lg border border-amber-800 bg-amber-950/30 px-4 py-2 text-sm text-amber-300">
            Team chat isn't switched on for your account yet — you can set channels up now; people see them once ContactConnection
            support turns chat on.
          </p>
        )}
        {error && <p className="mb-4 text-sm text-red-400">{error}</p>}

        {editing && (
          <ChannelEditor key={editing.id ?? 'new'} id={editing.id} initial={editing.form} people={people} roles={roles}
            onClose={() => setEditing(null)} onSaved={() => { setEditing(null); load() }} />
        )}

        {!channels && <p className="text-gray-400 text-sm">Loading…</p>}
        {channels?.length === 0 && <p className="text-gray-500 text-sm">No channels yet.</p>}
        {active.length > 0 && <ChannelTable list={active} people={people} roles={roles}
          onEdit={(c) => setEditing({ id: c.id, form: toForm(c) })} onRetire={(c) => void retire(c, true)} />}
        {retired.length > 0 && (
          <>
            <h2 className="text-gray-400 text-sm font-medium mt-8 mb-2">Retired — history readable, nobody can post</h2>
            <ChannelTable list={retired} people={people} roles={roles}
              onEdit={(c) => setEditing({ id: c.id, form: toForm(c) })} onRetire={(c) => void retire(c, false)} retiredList />
          </>
        )}
      </div>
    </AdminShell>
  )
}

const toForm = (c: AdminChatChannel): SaveChatChannel => ({
  name: c.name, description: c.description, isPrivate: c.isPrivate, postingRestricted: c.postingRestricted, posterIds: c.posterIds,
  posterRoleIds: c.posterRoleIds ?? [],
  membershipLocked: c.membershipLocked, assignedRoleIds: c.assignedRoleIds, assignedIds: c.assignedIds,
})

function ChannelTable({ list, people, roles, onEdit, onRetire, retiredList }: {
  list: AdminChatChannel[]; people: Person[]; roles: RoleOpt[]
  onEdit: (c: AdminChatChannel) => void; onRetire: (c: AdminChatChannel) => void; retiredList?: boolean
}) {
  const name = (id: string) => people.find((p) => p.id === id)?.name ?? 'Unknown'
  const role = (id: string) => roles.find((r) => r.id === id)?.name ?? 'Unknown role'
  return (
    <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-gray-800 text-gray-400 text-left">
            <th className="px-4 py-3 font-medium">Channel</th>
            <th className="px-4 py-3 font-medium">Who's in it</th>
            <th className="px-4 py-3 font-medium">Posting</th>
            <th className="px-4 py-3 font-medium">Members</th>
            <th className="px-4 py-3"></th>
          </tr>
        </thead>
        <tbody>
          {list.map((c) => (
            <tr key={c.id} className="border-b border-gray-800 last:border-0 align-top">
              <td className="px-4 py-3">
                <p className="text-white"># {c.name} {c.isPrivate && <span className="text-[10px] text-gray-400 border border-gray-700 rounded px-1 ml-1">private</span>}</p>
                {c.description && <p className="text-xs text-gray-500">{c.description}</p>}
              </td>
              <td className="px-4 py-3 text-xs text-gray-400">
                {c.assignedRoleIds.length > 0 && <p>Roles: {c.assignedRoleIds.map(role).join(', ')}</p>}
                {c.assignedIds.length > 0 && <p>People: {c.assignedIds.length <= 4 ? c.assignedIds.map(name).join(', ') : `${c.assignedIds.length} assigned`}</p>}
                {c.assignedRoleIds.length === 0 && c.assignedIds.length === 0 && <p>{c.isPrivate ? 'Nobody assigned' : 'Anyone can join'}</p>}
                {c.membershipLocked && <p className="text-amber-300/90">📌 Assigned members can't leave</p>}
              </td>
              <td className="px-4 py-3 text-xs">
                {c.retired ? <span className="text-gray-500">Retired</span>
                  : c.postingRestricted ? <span className="text-amber-300">📣 {
                      [...(c.posterRoleIds ?? []).map((r) => `${role(r)} (role)`), ...c.posterIds.map(name)].join(', ') || 'Chat managers only'}</span>
                  : <span className="text-gray-400">Everyone</span>}
              </td>
              <td className="px-4 py-3 text-xs text-gray-400">{c.memberCount}</td>
              <td className="px-4 py-3 text-right whitespace-nowrap">
                <button onClick={() => onEdit(c)} className="text-indigo-400 hover:text-indigo-300 text-xs font-medium mr-3">Edit</button>
                <button onClick={() => onRetire(c)} className={`text-xs font-medium ${retiredList ? 'text-emerald-400 hover:text-emerald-300' : 'text-gray-400 hover:text-red-300'}`}>
                  {retiredList ? 'Un-retire' : 'Retire'}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function ChannelEditor({ id, initial, people, roles, onClose, onSaved }: {
  id: string | null; initial: SaveChatChannel; people: Person[]; roles: RoleOpt[]; onClose: () => void; onSaved: () => void
}) {
  const [f, setF] = useState<SaveChatChannel>(initial)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const set = (patch: Partial<SaveChatChannel>) => setF((x) => ({ ...x, ...patch }))
  const inputCls = 'w-full bg-gray-800 text-white rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-indigo-500'

  async function save() {
    setSaving(true); setError(null)
    try {
      const body = { ...f, description: f.description?.trim() || null }
      if (id) await chatAdminApi.update(id, body); else await chatAdminApi.create(body)
      onSaved()
    } catch (e) { setError(e instanceof Error ? e.message : 'Save failed.') }
    finally { setSaving(false) }
  }

  return (
    <div className="mb-6 bg-gray-900 border border-gray-800 rounded-xl p-5">
      <p className="text-gray-200 text-sm font-medium mb-4">{id ? 'Edit channel' : 'New channel'}</p>
      <div className="grid md:grid-cols-2 gap-4">
        <div>
          <label className="block text-xs text-gray-400 mb-1">Name</label>
          <input value={f.name} onChange={(e) => set({ name: e.target.value })} placeholder="floor-announcements" className={inputCls} />
        </div>
        <div>
          <label className="block text-xs text-gray-400 mb-1">Description</label>
          <input value={f.description ?? ''} onChange={(e) => set({ description: e.target.value })} placeholder="What it's for (optional)" className={inputCls} />
        </div>
      </div>

      <div className="mt-5 space-y-4">
        <Check on={f.isPrivate} set={(v) => set({ isPrivate: v })} label="Private"
          hint="Only the people and roles assigned below can see it. Public channels can be browsed and joined by anyone." />

        <div>
          <p className="text-xs text-gray-400 mb-1">Assigned roles — everyone holding one is in the channel</p>
          <Chips options={roles} chosen={f.assignedRoleIds} onChange={(v) => set({ assignedRoleIds: v })} empty="No roles assigned" />
        </div>
        <div>
          <p className="text-xs text-gray-400 mb-1">Assigned people</p>
          <PeoplePicker people={people} chosen={f.assignedIds} onChange={(v) => set({ assignedIds: v })} />
        </div>
        <Check on={f.membershipLocked} set={(v) => set({ membershipLocked: v })} label="Lock membership"
          hint="Assigned people and role holders can't leave this channel." />

        <Check on={f.postingRestricted} set={(v) => set({ postingRestricted: v })} label="Restrict posting"
          hint="Only the roles and people chosen here can post (chat managers always can). Everyone else reads and reacts." />
        {f.postingRestricted && (
          <div className="pl-12 space-y-3">
            <div>
              <p className="text-xs text-gray-400 mb-1">Roles that can post — whoever holds the role, now or later</p>
              <Chips options={roles} chosen={f.posterRoleIds} onChange={(v) => set({ posterRoleIds: v })} empty="No roles yet" />
            </div>
            <div>
              <p className="text-xs text-gray-400 mb-1">People who can post</p>
              <PeoplePicker people={people} chosen={f.posterIds} onChange={(v) => set({ posterIds: v })} />
            </div>
          </div>
        )}
        <p className="text-xs text-gray-500">To stop all posting but keep the history readable, save and then <b>Retire</b> the channel.</p>
      </div>

      <div className="flex items-center gap-3 mt-6 pt-4 border-t border-gray-800">
        <button onClick={() => void save()} disabled={saving || !f.name.trim()}
          className="bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg px-5 py-2 text-sm font-medium">
          {saving ? 'Saving…' : id ? 'Save channel' : 'Create channel'}
        </button>
        <button onClick={onClose} className="text-gray-400 hover:text-white text-sm">Cancel</button>
        {error && <span className="text-red-400 text-sm">{error}</span>}
      </div>
    </div>
  )
}

function Check({ on, set, label, hint }: { on: boolean; set: (v: boolean) => void; label: string; hint: string }) {
  return (
    <div className="flex items-start gap-3">
      <button type="button" onClick={() => set(!on)}
        className={`relative inline-flex h-5 w-10 shrink-0 items-center rounded-full transition-colors mt-0.5 ${on ? 'bg-indigo-600' : 'bg-gray-700'}`}>
        <span className={`inline-block h-4 w-4 rounded-full bg-white shadow transition-transform ${on ? 'translate-x-5' : 'translate-x-1'}`} />
      </button>
      <div>
        <span className="text-sm text-gray-300 font-medium">{label}</span>
        <p className="text-xs text-gray-500 mt-0.5 leading-snug">{hint}</p>
      </div>
    </div>
  )
}

function Chips({ options, chosen, onChange, empty }: { options: RoleOpt[]; chosen: string[]; onChange: (v: string[]) => void; empty: string }) {
  if (options.length === 0) return <p className="text-xs text-gray-600">{empty}</p>
  return (
    <div className="flex flex-wrap gap-1.5">
      {options.map((o) => {
        const on = chosen.includes(o.id)
        return (
          <button key={o.id} type="button" onClick={() => onChange(on ? chosen.filter((x) => x !== o.id) : [...chosen, o.id])}
            className={`text-xs rounded-full px-2.5 py-1 border ${on ? 'border-indigo-500 bg-indigo-950/60 text-indigo-200' : 'border-gray-700 text-gray-400 hover:text-gray-200'}`}>
            {o.name}
          </button>
        )
      })}
    </div>
  )
}

/** Search-and-tick list of people, with the chosen ones as removable chips. */
export function PeoplePicker({ people, chosen, onChange }: { people: Person[] | { id: string; name: string; email?: string }[]; chosen: string[]; onChange: (v: string[]) => void }) {
  const [q, setQ] = useState('')
  const list = useMemo(() => people.filter((p) => !chosen.includes(p.id)
    && (p.name.toLowerCase().includes(q.toLowerCase()) || (p.email ?? '').toLowerCase().includes(q.toLowerCase()))).slice(0, 8), [people, chosen, q])
  const nameOf = (id: string) => people.find((p) => p.id === id)?.name ?? 'Unknown'
  return (
    <div>
      {chosen.length > 0 && (
        <div className="flex flex-wrap gap-1.5 mb-1.5">
          {chosen.map((id) => (
            <button key={id} type="button" onClick={() => onChange(chosen.filter((x) => x !== id))}
              className="text-xs rounded-full px-2.5 py-1 border border-indigo-500 bg-indigo-950/60 text-indigo-200">{nameOf(id)} ✕</button>
          ))}
        </div>
      )}
      <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Add people…"
        className="w-full max-w-sm bg-gray-800 text-white rounded-lg px-3 py-1.5 text-sm outline-none focus:ring-2 focus:ring-indigo-500" />
      {q && list.length > 0 && (
        <div className="max-w-sm mt-1 bg-gray-900 border border-gray-700 rounded-lg overflow-hidden">
          {list.map((p) => (
            <button key={p.id} type="button" onClick={() => { onChange([...chosen, p.id]); setQ('') }}
              className="w-full text-left px-3 py-1.5 text-sm text-gray-300 hover:bg-gray-800">
              {p.name} {p.email && <span className="text-xs text-gray-500">{p.email}</span>}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
