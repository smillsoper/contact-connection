import { useState } from 'react'
import type { FlowAncestorVars, FlowVarToken } from '../../utils/flowGraph'

interface StaticField { key: string }

interface StaticNamespace {
  ns: string
  fields: StaticField[]
}

const STATIC_NAMESPACES: StaticNamespace[] = [
  {
    ns: 'call_record',
    fields: [
      { key: 'id' }, { key: 'status' }, { key: 'call_source' }, { key: 'record_type' },
      { key: 'phone_number' }, { key: 'dnis' }, { key: 'account_number' }, { key: 'list_id' },
      { key: 'campaign_id' }, { key: 'disposition' }, { key: 'notes' },
      { key: 'call_started_at' }, { key: 'call_ended_at' }, { key: 'handle_time_seconds' },
    ],
  },
  {
    ns: 'caller',
    fields: [
      { key: 'name' }, { key: 'first_name' }, { key: 'last_name' },
      { key: 'phone' }, { key: 'email' }, { key: 'account_number' },
      { key: 'billing_address' }, { key: 'shipping_address' },
    ],
  },
  {
    ns: 'agent',
    fields: [
      { key: 'id' }, { key: 'name' }, { key: 'first_name' }, { key: 'last_name' },
      { key: 'email' }, { key: 'extension' }, { key: 'role' },
    ],
  },
  {
    ns: 'tenant',
    fields: [
      { key: 'id' }, { key: 'name' }, { key: 'subdomain' }, { key: 'timezone' }, { key: 'plan_tier' },
    ],
  },
]

interface Props {
  onInsert: (token: string) => void
  /** When provided, the input / api / flow sections show real flow data instead of placeholders. */
  flowVars?: FlowAncestorVars
  /** Dark-themed variant for use inside the dark properties panel. */
  dark?: boolean
}

export default function VariablePanel({ onInsert, flowVars, dark }: Props) {
  const [expanded, setExpanded] = useState<Set<string>>(new Set(['call_record', 'caller']))

  function toggle(ns: string) {
    setExpanded((prev) => {
      const next = new Set(prev)
      next.has(ns) ? next.delete(ns) : next.add(ns)
      return next
    })
  }

  // Theme tokens
  const bg       = dark ? 'bg-gray-900'  : 'bg-gray-50'
  const border   = dark ? 'border-gray-800' : 'border-gray-100'
  const hdr      = dark ? 'text-gray-300' : 'text-gray-700'
  const subText  = dark ? 'text-gray-500' : 'text-gray-400'
  const hoverRow = dark ? 'hover:bg-gray-800' : 'hover:bg-gray-50'
  const tokenCls = dark
    ? 'text-indigo-400 hover:bg-gray-800 hover:text-indigo-300'
    : 'text-indigo-600 hover:bg-indigo-50 hover:text-indigo-900'
  const emptyTxt = dark ? 'text-gray-600' : 'text-gray-400'
  const dynBadge = dark
    ? 'bg-violet-900/60 text-violet-300'
    : 'bg-amber-100 text-amber-700'

  function Section({ ns, label, children, badge }: {
    ns: string; label: string; children: React.ReactNode; badge?: string
  }) {
    const isOpen = expanded.has(ns)
    return (
      <div className={`border-b ${border} last:border-0`}>
        <button
          type="button"
          onClick={() => toggle(ns)}
          className={`w-full flex items-center gap-1.5 px-3 py-2 ${hoverRow} transition-colors text-left`}
        >
          <svg
            className={`w-3 h-3 ${subText} shrink-0 transition-transform duration-150 ${isOpen ? 'rotate-90' : ''}`}
            fill="none" stroke="currentColor" viewBox="0 0 24 24"
          >
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M9 5l7 7-7 7" />
          </svg>
          <span className={`text-[11px] font-semibold font-mono ${hdr}`}>{label}</span>
          {badge && (
            <span className={`ml-auto text-[9px] px-1.5 py-0.5 rounded font-medium shrink-0 ${dynBadge}`}>
              {badge}
            </span>
          )}
        </button>
        {isOpen && <div className="pb-1.5">{children}</div>}
      </div>
    )
  }

  function Token({ token, display, note }: { token: string; display: string; note?: string }) {
    return (
      <button
        type="button"
        onClick={() => onInsert(token)}
        title={`Insert ${token}`}
        className={`w-full text-left px-7 py-0.5 text-[11px] font-mono transition-colors ${tokenCls}`}
      >
        {display}
        {note && <span className={`ml-1.5 text-[9px] font-sans not-italic ${subText}`}>{note}</span>}
      </button>
    )
  }

  return (
    <div className={`flex flex-col h-full ${bg}`}>
      {/* Header */}
      <div className={`px-3 py-2.5 border-b ${border} shrink-0`}>
        <p className={`text-xs font-semibold ${hdr}`}>Available Variables</p>
        <p className={`text-[10px] ${subText} mt-0.5`}>Click any field to insert at cursor</p>
      </div>

      <div className="flex-1 overflow-y-auto">

        {/* ── Static namespaces ── */}
        {STATIC_NAMESPACES.map((ns) => (
          <Section key={ns.ns} ns={ns.ns} label={ns.ns}>
            {ns.fields.map((f) => (
              <Token key={f.key} token={`{{${ns.ns}.${f.key}}}`} display={f.key} />
            ))}
          </Section>
        ))}

        {/* ── input namespace ── */}
        <Section ns="input" label="input" badge={flowVars ? 'flow' : 'dynamic'}>
          {flowVars ? (
            flowVars.inputs.length > 0 ? (
              flowVars.inputs.map((inp) => (
                <Token
                  key={inp.id}
                  token={`{{input.${inp.id}}}`}
                  display={inp.id}
                  note={inp.label}
                />
              ))
            ) : (
              <p className={`px-7 py-1 text-[10px] italic ${emptyTxt}`}>
                No input nodes before this node.
              </p>
            )
          ) : (
            <>
              <p className={`px-7 pb-1 text-[10px] italic leading-snug ${dark ? 'text-amber-500' : 'text-amber-600'}`}>
                Replace [node_id] with the ID of an Input node.
              </p>
              <Token token="{{input.[node_id]}}" display="[node_id]" />
            </>
          )}
        </Section>

        {/* ── api namespace ── */}
        <Section ns="api" label="api" badge={flowVars ? 'flow' : 'dynamic'}>
          {flowVars ? (
            flowVars.apis.length > 0 ? (
              flowVars.apis.map((api) => (
                <Token
                  key={api.id}
                  token={`{{api.${api.id}.field}}`}
                  display={`${api.id}.field`}
                  note={api.label}
                />
              ))
            ) : (
              <p className={`px-7 py-1 text-[10px] italic ${emptyTxt}`}>
                No API call nodes before this node.
              </p>
            )
          ) : (
            <>
              <p className={`px-7 pb-1 text-[10px] italic leading-snug ${dark ? 'text-amber-500' : 'text-amber-600'}`}>
                Replace [node_id] with the ID of an API Call node.
              </p>
              <Token token="{{api.[node_id].field}}" display="[node_id].field" />
            </>
          )}
        </Section>

        {/* ── flow namespace ── */}
        <Section ns="flow" label="flow" badge={flowVars ? 'flow' : 'dynamic'}>
          {flowVars ? (
            flowVars.flowVars.length > 0 ? (
              flowVars.flowVars.map((v: FlowVarToken) =>
                v.isObject ? (
                  <div key={v.key}>
                    {/* Object header — non-clickable; accessing the root would return raw JSON */}
                    <div className={`px-7 py-0.5 flex items-center gap-1.5 text-[11px] font-mono ${subText}`}>
                      {v.key}
                      <span className={`text-[9px] px-1 py-px rounded font-sans font-medium ${dynBadge}`}>obj</span>
                      {v.label && <span className={`text-[9px] font-sans not-italic ml-0.5 ${subText}`}>{v.label}</span>}
                    </div>
                    {v.properties?.map((p) => (
                      <Token
                        key={p.key}
                        token={`{{flow.${v.key}.${p.key}}}`}
                        display={`  .${p.key}`}
                        note={p.label}
                      />
                    ))}
                  </div>
                ) : (
                  <Token
                    key={v.key}
                    token={`{{flow.${v.key}}}`}
                    display={v.key}
                    note={v.label}
                  />
                )
              )
            ) : (
              <p className={`px-7 py-1 text-[10px] italic ${emptyTxt}`}>
                No flow variables set before this node.
              </p>
            )
          ) : (
            <>
              <p className={`px-7 pb-1 text-[10px] italic leading-snug ${dark ? 'text-amber-500' : 'text-amber-600'}`}>
                Replace [variable_name] with a name set via Set Variable.
              </p>
              <Token token="{{flow.[variable_name]}}" display="[variable_name]" />
            </>
          )}
        </Section>

      </div>
    </div>
  )
}
