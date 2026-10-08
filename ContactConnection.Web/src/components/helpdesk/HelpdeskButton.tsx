import { useEffect, useMemo, useState } from 'react'
import { createPortal } from 'react-dom'
import { api } from '../../api/client'
import { CloseIcon, HelpDeskIcon, SearchIcon } from '../icons/Icons'
import { HelpdeskAttachments, HelpdeskBody, type Helpdesk } from './HelpdeskTopicView'

/**
 * The agent's help desk (S184): a bright button beside Caller history that opens every active help desk for the call's
 * campaign — one tab per help desk, a searchable topic list, and the topic itself. Links open in a new window.
 */
export default function HelpdeskButton({ callRecordId }: { callRecordId: string | null }) {
  const [desks, setDesks] = useState<Helpdesk[] | null>(null)
  const [open, setOpen] = useState(false)
  const [tab, setTab] = useState(0)
  const [topicId, setTopicId] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [error, setError] = useState<string | null>(null)

  const load = (id: string) => api.get<Helpdesk[]>(`/api/v1/helpdesks/for-call/${id}`)
    .then((d) => { setDesks(d); setError(null) })
    .catch((e) => setError(e instanceof Error ? e.message : 'Could not load the help desk'))

  useEffect(() => {
    setDesks(null); setOpen(false); setTab(0); setTopicId(null); setQuery('')
    if (callRecordId) load(callRecordId)
  }, [callRecordId])

  const desk = desks?.[Math.min(tab, (desks?.length ?? 1) - 1)] ?? null
  const topics = useMemo(() => {
    if (!desk) return []
    const q = query.trim().toLowerCase()
    if (!q) return desk.topics
    return desk.topics.filter((t) => t.title.toLowerCase().includes(q)
      || new DOMParser().parseFromString(t.html, 'text/html').body.textContent?.toLowerCase().includes(q))
  }, [desk, query])
  const topic = topics.find((t) => t.id === topicId) ?? topics[0] ?? null

  if (!callRecordId || !desks || desks.length === 0) return null

  return (
    <>
      <button onClick={() => { setOpen(true); load(callRecordId) }} title="Help desk for this campaign"
        className="mb-1 shrink-0 flex items-center gap-1.5 text-xs font-semibold px-2.5 py-1 rounded-md border border-yellow-400 text-gray-900 bg-yellow-300 hover:bg-yellow-200 shadow-[0_0_10px_rgba(250,204,21,0.35)] transition-colors">
        <HelpDeskIcon size={14} />
        Help desk
      </button>

      {open && createPortal(
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-3 sm:p-6" onClick={() => setOpen(false)}>
          <div className="bg-gray-900 border border-gray-800 rounded-xl shadow-xl w-full max-w-5xl h-[88vh] flex flex-col" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between gap-4 px-5 pt-4 pb-2">
              <h3 className="flex items-center gap-2 text-base font-semibold text-white">
                <span className="text-yellow-300"><HelpDeskIcon size={18} /></span>Help desk
              </h3>
              <button className="text-gray-400 hover:text-white" onClick={() => setOpen(false)} title="Close"><CloseIcon size={18} /></button>
            </div>
            {desks.length > 1 && (
              <div className="flex gap-0 px-5 border-b border-gray-800 overflow-x-auto shrink-0">
                {desks.map((d, i) => (
                  <button key={d.id} onClick={() => { setTab(i); setTopicId(null); setQuery('') }}
                    className={`px-4 py-2 text-sm whitespace-nowrap border-b-2 transition-colors ${i === tab
                      ? 'border-yellow-300 text-white font-medium' : 'border-transparent text-gray-400 hover:text-gray-200'}`}>
                    {d.name}
                  </button>
                ))}
              </div>
            )}
            {desks.length === 1 && <div className="px-5 pb-2 text-sm text-gray-300 border-b border-gray-800">{desk?.name}</div>}
            {error && <p className="text-sm text-red-400 px-5 pt-2">{error}</p>}
            {desk && (
              <div className="flex flex-1 min-h-0 flex-col sm:flex-row">
                <div className="sm:w-64 shrink-0 border-b sm:border-b-0 sm:border-r border-gray-800 flex flex-col min-h-0 max-h-48 sm:max-h-none">
                  <div className="p-2">
                    <label className="flex items-center gap-1.5 bg-gray-800 border border-gray-700 rounded-md px-2 py-1 focus-within:border-yellow-400">
                      <SearchIcon size={13} className="text-gray-500" />
                      <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search topics"
                        className="bg-transparent text-xs text-white w-full focus:outline-none" />
                    </label>
                  </div>
                  <div className="overflow-y-auto flex-1 px-1.5 pb-2">
                    {topics.length === 0 && <p className="text-xs text-gray-500 px-2 py-3">{desk.topics.length ? 'No topics match.' : 'No topics yet.'}</p>}
                    {topics.map((t) => (
                      <button key={t.id} onClick={() => setTopicId(t.id)}
                        className={`w-full text-left text-sm rounded-md px-2.5 py-1.5 mb-0.5 ${t.id === topic?.id
                          ? 'bg-yellow-300/15 text-yellow-100' : 'text-gray-300 hover:bg-gray-800'}`}>
                        {t.title}
                      </button>
                    ))}
                  </div>
                </div>
                <div className="flex-1 min-w-0 overflow-y-auto px-6 py-4">
                  {desk.description && !topic && <p className="text-sm text-gray-400">{desk.description}</p>}
                  {topic && (
                    <>
                      <h2 className="text-lg font-semibold text-white mb-3">{topic.title}</h2>
                      <HelpdeskBody html={topic.html} />
                      <HelpdeskAttachments files={topic.attachments} />
                    </>
                  )}
                </div>
              </div>
            )}
          </div>
        </div>, document.body,
      )}
    </>
  )
}
