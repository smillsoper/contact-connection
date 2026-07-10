import { useEffect, useRef, useState } from 'react'
import type { Node } from '@xyflow/react'
import type { TelNodeData, TelephonyNodeType, TimeWindow, TelVariableAssignment } from '../../types/telephony-designer'
import { TELEPHONY_NODE_META } from '../../types/telephony-designer'
import { TIMEZONE_GROUPS } from '../../utils/timezones'
import { audioFilesApi, BUILTIN_AUDIO_GROUPS, BUILTIN_AUDIO_OPTIONS, type AudioFileRecord } from '../../api/audioFiles'

const DAY_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

const HOLIDAY_OPTIONS: { key: string; label: string }[] = [
  { key: 'new_years', label: "New Year's Day (Jan 1)" },
  { key: 'mlk_day', label: 'MLK Day (3rd Mon Jan)' },
  { key: 'presidents_day', label: 'Presidents Day (3rd Mon Feb)' },
  { key: 'memorial_day', label: 'Memorial Day (Last Mon May)' },
  { key: 'juneteenth', label: 'Juneteenth (Jun 19)' },
  { key: 'independence_day', label: 'Independence Day (Jul 4)' },
  { key: 'labor_day', label: 'Labor Day (1st Mon Sep)' },
  { key: 'columbus_day', label: 'Columbus Day (2nd Mon Oct)' },
  { key: 'veterans_day', label: 'Veterans Day (Nov 11)' },
  { key: 'thanksgiving', label: 'Thanksgiving (4th Thu Nov)' },
  { key: 'christmas_eve', label: 'Christmas Eve (Dec 24)' },
  { key: 'christmas', label: 'Christmas Day (Dec 25)' },
  { key: 'new_years_eve', label: "New Year's Eve (Dec 31)" },
]

interface Props {
  node: Node<TelNodeData>
  entryNodeId: string | null
  onChange: (id: string, data: Partial<TelNodeData>) => void
  onSetEntry: (id: string) => void
  onDelete: (id: string) => void
}

export default function TelephonyNodePropertiesPanel({
  node,
  entryNodeId,
  onChange,
  onSetEntry,
  onDelete,
}: Props) {
  const type = node.type as TelephonyNodeType
  const data = node.data
  const meta = TELEPHONY_NODE_META[type]
  const isEntry = entryNodeId === node.id
  const isEventNode = meta.handles === 'source-only'

  const set = (field: string, value: unknown) => onChange(node.id, { [field]: value })

  return (
    <div className="w-72 bg-gray-900 border-l border-gray-700 flex flex-col p-4 gap-3 overflow-y-auto shrink-0 text-sm">
      {/* Header */}
      <div>
        <div className="flex items-center gap-2 mb-1">
          <div className="w-3 h-3 rounded-full" style={{ background: meta.color }} />
          <span className="font-semibold text-gray-100">{meta.label}</span>
        </div>
        <p className="text-xs text-gray-400">{meta.description}</p>
      </div>

      {/* Label */}
      <div>
        <label className="block text-xs text-gray-400 mb-1">Label</label>
        <input
          className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm focus:outline-none focus:border-blue-500"
          value={(data.label as string) ?? ''}
          onChange={(e) => set('label', e.target.value)}
        />
      </div>

      {/* Node-type-specific fields */}
      {type === 'tf_reject' && (
        <div>
          <label className="block text-xs text-gray-400 mb-1">Rejection Cause</label>
          <select
            className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm focus:outline-none focus:border-blue-500"
            value={(data.cause as string) ?? 'busy'}
            onChange={(e) => set('cause', e.target.value)}
          >
            <option value="busy">Busy (SIP 486)</option>
            <option value="unavailable">Unavailable (SIP 480)</option>
            <option value="declined">Declined (SIP 603)</option>
          </select>
        </div>
      )}

      {type === 'tf_check_agent_availability' && (
        <div>
          <label className="block text-xs text-gray-400 mb-1">Campaign ID override (optional)</label>
          <input
            className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm focus:outline-none focus:border-blue-500"
            placeholder="Leave blank to use call's campaign"
            value={(data.campaignId as string) ?? ''}
            onChange={(e) => set('campaignId', e.target.value)}
          />
        </div>
      )}

      {type === 'tf_route_to_queue' && (
        <div>
          <label className="block text-xs text-gray-400 mb-1">Direct to extension (optional)</label>
          <input
            className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm focus:outline-none focus:border-blue-500"
            placeholder="e.g. 1001 — leave blank for queue"
            value={(data.agentExtension as string) ?? ''}
            onChange={(e) => set('agentExtension', e.target.value)}
          />
        </div>
      )}

      {type === 'tf_branch' && (
        <div>
          <label className="block text-xs text-gray-400 mb-1">Condition</label>
          <input
            className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm font-mono focus:outline-none focus:border-blue-500"
            placeholder='e.g. {{flow.status}} == "vip"'
            value={(data.condition as string) ?? ''}
            onChange={(e) => set('condition', e.target.value)}
          />
          <p className="text-xs text-gray-500 mt-1">Operators: ==  !=  &gt;  &lt;  &gt;=  &lt;=  contains</p>
        </div>
      )}

      {type === 'tf_time_of_day' && (
        <TimeOfDayEditor
          timezone={(data.timezone as string) ?? 'America/Chicago'}
          windows={(data.windows as TimeWindow[]) ?? []}
          onChange={(tz, ws) => onChange(node.id, { timezone: tz, windows: ws })}
        />
      )}

      {type === 'tf_check_block_list' && (
        <div>
          <label className="block text-xs text-gray-400 mb-1">Check variable (optional)</label>
          <input
            className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm font-mono focus:outline-none focus:border-blue-500"
            placeholder="Leave blank to use call ANI"
            value={(data.checkVariable as string) ?? ''}
            onChange={(e) => set('checkVariable', e.target.value)}
          />
          <p className="text-xs text-gray-500 mt-1">Enter a variable name to check instead of ANI. Useful for X-Original-ANI header values.</p>
        </div>
      )}

      {type === 'tf_set_variable' && (
        <SetVariableEditor
          assignments={(data.assignments as TelVariableAssignment[]) ?? []}
          onChange={(a) => set('assignments', a)}
        />
      )}

      {type === 'tf_get_sip_header' && (
        <>
          <div>
            <label className="block text-xs text-gray-400 mb-1">SIP header name</label>
            <input
              className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm font-mono focus:outline-none focus:border-teal-500"
              placeholder="e.g. X-Original-ANI"
              value={(data.headerName as string) ?? ''}
              onChange={(e) => set('headerName', e.target.value)}
            />
          </div>
          <div>
            <label className="block text-xs text-gray-400 mb-1">Store into variable</label>
            <input
              className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm font-mono focus:outline-none focus:border-teal-500"
              placeholder="e.g. original_ani"
              value={(data.variableName as string) ?? ''}
              onChange={(e) => set('variableName', e.target.value)}
            />
          </div>
        </>
      )}

      {type === 'tf_set_sip_header' && (
        <>
          <div>
            <label className="block text-xs text-gray-400 mb-1">SIP header name</label>
            <input
              className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm font-mono focus:outline-none focus:border-cyan-500"
              placeholder="e.g. X-Campaign-ID"
              value={(data.sipHeaderName as string) ?? ''}
              onChange={(e) => set('sipHeaderName', e.target.value)}
            />
            <p className="text-xs text-gray-500 mt-1">Will be sent as <span className="font-mono">X-{(data.sipHeaderName as string) || 'HeaderName'}</span></p>
          </div>
          <div>
            <label className="block text-xs text-gray-400 mb-1">Value</label>
            <input
              className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm font-mono focus:outline-none focus:border-cyan-500"
              placeholder='e.g. {{caller.ani}} or literal'
              value={(data.sipHeaderValue as string) ?? ''}
              onChange={(e) => set('sipHeaderValue', e.target.value)}
            />
            <p className="text-xs text-gray-500 mt-1">Supports <span className="font-mono">{'{{caller.ani}}'}</span>, <span className="font-mono">{'{{call.did}}'}</span>, or any stored variable.</p>
          </div>
        </>
      )}

      {type === 'tf_set_caller_id' && (
        <>
          <div>
            <label className="block text-xs text-gray-400 mb-1">Caller ID value</label>
            <input
              className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm font-mono focus:outline-none focus:border-sky-500"
              placeholder='+15035551234 or {{caller.ani}}'
              value={(data.callerIdValue as string) ?? ''}
              onChange={(e) => set('callerIdValue', e.target.value)}
            />
            <p className="text-xs text-gray-500 mt-1">
              Literal E.164 number or a <span className="font-mono text-sky-400">{'{{variable}}'}</span>. Click to insert:
            </p>
          </div>
          <CallerIdVariableReference onInsert={(v) => set('callerIdValue', v)} />
        </>
      )}

      {type === 'tf_cancel_dial' && (
        <>
          <div>
            <label className="block text-xs text-gray-400 mb-1">Agent message</label>
            <textarea
              rows={4}
              className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm resize-y focus:outline-none focus:border-orange-500"
              placeholder="e.g. This number is only available during business hours (Mon–Fri 8am–5pm CT)."
              value={(data.cancelMessage as string) ?? ''}
              onChange={(e) => set('cancelMessage', e.target.value)}
            />
            <p className="text-xs text-gray-500 mt-1">
              Displayed to the agent when the dial is cancelled. Supports <span className="font-mono text-orange-400">{'{{variables}}'}</span> — click to insert:
            </p>
          </div>
          <CancelDialVariableReference onInsert={(v) => {
            const current = (data.cancelMessage as string) ?? ''
            set('cancelMessage', current + v)
          }} />
        </>
      )}

      {type === 'tf_on_custom_event' && (
        <div>
          <label className="block text-xs text-gray-400 mb-1">Event name</label>
          <input
            className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm font-mono focus:outline-none focus:border-amber-500"
            placeholder="e.g. disposition_set"
            value={(data.eventName as string) ?? ''}
            onChange={(e) => set('eventName', e.target.value)}
          />
          <p className="text-xs text-gray-500 mt-1.5 leading-snug">
            Fire this branch via <span className="font-mono text-amber-400">FireEventAsync(uuid, "custom:{'{eventName}'}")</span>.
            Use this for cross-talk between script flows and the telephony flow.
          </p>
        </div>
      )}

      {isEventNode && type !== 'tf_on_custom_event' && (
        <p className="text-xs text-gray-500 leading-snug bg-gray-800 border border-gray-700 rounded p-2">
          This branch fires automatically when the <strong className="text-gray-300">{meta.label}</strong> event
          occurs. Drop action nodes below this to define what happens at that moment in the call.
        </p>
      )}

      {type === 'tf_dtmf' && (
        <DtmfNodeEditor data={data} onChange={(patch) => onChange(node.id, patch)} />
      )}

      {type === 'tf_play' && (
        <PlayNodeEditor data={data} onChange={(patch) => onChange(node.id, patch)} />
      )}

      {type === 'tf_whisper' && (
        <WhisperNodeEditor data={data} onChange={(patch) => onChange(node.id, patch)} />
      )}

      {type === 'tf_script_pop' && (
        <div className="flex flex-col gap-2">
          <div>
            <label className="block text-xs text-gray-400 mb-1">CRM flow ID override (optional)</label>
            <input
              className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm font-mono focus:outline-none focus:border-cyan-500"
              placeholder="Leave blank to use campaign's default flow"
              value={(data.flowId as string) ?? ''}
              onChange={(e) => set('flowId', e.target.value)}
            />
          </div>
          <p className="text-xs text-gray-500 leading-snug">
            Starts the CRM script flow on the answering agent's screen.
            Resolution: node override → campaign's assigned Script Flow.
          </p>
        </div>
      )}

      {/* Entry / Delete footer */}
      <div className="flex gap-2 pt-2 border-t border-gray-700 mt-auto">
        {/* Event nodes are self-contained entry points — no "Set as Entry" needed */}
        {!isEventNode && !isEntry && (
          <button
            onClick={() => onSetEntry(node.id)}
            className="flex-1 text-xs bg-gray-700 hover:bg-gray-600 text-gray-200 rounded py-1.5"
          >
            Set as Entry
          </button>
        )}
        {!isEventNode && isEntry && (
          <span className="flex-1 text-xs text-center text-green-400 py-1.5">Entry node ✓</span>
        )}
        {isEventNode && (
          <span className="flex-1 text-xs text-center text-violet-400 py-1.5">Event entry ✓</span>
        )}
        <button
          onClick={() => onDelete(node.id)}
          className="flex-1 text-xs bg-red-900 hover:bg-red-800 text-red-200 rounded py-1.5"
        >
          Delete
        </button>
      </div>
    </div>
  )
}

function TimeOfDayEditor({
  timezone,
  windows,
  onChange,
}: {
  timezone: string
  windows: TimeWindow[]
  onChange: (tz: string, ws: TimeWindow[]) => void
}) {
  const setWindow = (i: number, patch: Partial<TimeWindow>) => {
    onChange(timezone, windows.map((w, idx) => (idx === i ? { ...w, ...patch } : w)))
  }

  const removeWindow = (i: number) =>
    onChange(timezone, windows.filter((_, idx) => idx !== i))

  const addScheduleWindow = () =>
    onChange(timezone, [
      ...windows,
      { name: `window_${windows.length + 1}`, windowType: 'weekly', days: [1, 2, 3, 4, 5], start: '08:00', end: '17:00' },
    ])

  const addHolidayWindow = () =>
    onChange(timezone, [
      ...windows,
      { name: 'christmas', windowType: 'holiday', holiday: 'christmas', start: '00:00', end: '23:59' },
    ])

  const addDateWindow = () => {
    const today = new Date().toISOString().slice(0, 10)
    onChange(timezone, [
      ...windows,
      { name: 'date_override', windowType: 'date', date: today, start: '00:00', end: '23:59' },
    ])
  }

  return (
    <div className="flex flex-col gap-2">
      {/* Timezone dropdown */}
      <div>
        <label className="block text-xs text-gray-400 mb-1">Timezone</label>
        <select
          className="w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm focus:outline-none focus:border-blue-500"
          value={timezone}
          onChange={(e) => onChange(e.target.value, windows)}
        >
          {TIMEZONE_GROUPS.map((grp) => (
            <optgroup key={grp.group} label={grp.group}>
              {grp.options.map((opt) => (
                <option key={opt.value} value={opt.value}>{opt.label}</option>
              ))}
            </optgroup>
          ))}
        </select>
      </div>

      <label className="block text-xs text-gray-400">Schedule Windows</label>

      {windows.map((w, i) => {
        const isHoliday = w.windowType === 'holiday'
        const days = w.days ?? []

        return (
          <div key={i} className="bg-gray-800 border border-gray-700 rounded p-2 flex flex-col gap-1.5">
            {/* Window header */}
            <div className="flex gap-1 items-center">
              {isHoliday ? (
                <>
                  <span className="text-[10px] font-bold bg-amber-900 text-amber-300 px-1.5 py-0.5 rounded shrink-0">
                    HOLIDAY
                  </span>
                  <select
                    className="flex-1 bg-gray-700 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs focus:outline-none min-w-0"
                    value={w.holiday ?? 'christmas'}
                    onChange={(e) => {
                      const key = e.target.value
                      setWindow(i, { holiday: key, name: key })
                    }}
                  >
                    {HOLIDAY_OPTIONS.map((h) => (
                      <option key={h.key} value={h.key}>{h.label}</option>
                    ))}
                  </select>
                </>
              ) : w.windowType === 'date' ? (
                <>
                  <span className="text-[10px] font-bold bg-violet-900 text-violet-300 px-1.5 py-0.5 rounded shrink-0">
                    DATE
                  </span>
                  <input
                    className="flex-1 bg-gray-700 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs focus:outline-none min-w-0"
                    placeholder="Transition name"
                    value={w.name}
                    onChange={(e) => setWindow(i, { name: e.target.value })}
                  />
                </>
              ) : (
                <>
                  <span className="text-[10px] font-bold bg-gray-700 text-gray-400 px-1.5 py-0.5 rounded shrink-0">
                    SCHEDULE
                  </span>
                  <input
                    className="flex-1 bg-gray-700 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs focus:outline-none min-w-0"
                    placeholder="Window name (used as transition key)"
                    value={w.name}
                    onChange={(e) => setWindow(i, { name: e.target.value })}
                  />
                </>
              )}
              <button onClick={() => removeWindow(i)} className="text-red-400 hover:text-red-300 text-xs px-1 shrink-0">✕</button>
            </div>

            {/* Date picker — date override only */}
            {w.windowType === 'date' && (
              <input
                className="w-full bg-gray-700 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs"
                type="date"
                value={w.date ?? ''}
                onChange={(e) => setWindow(i, { date: e.target.value })}
              />
            )}

            {/* Time range */}
            <div className="flex gap-1 items-center">
              <input
                className="flex-1 bg-gray-700 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs"
                type="time"
                value={w.start}
                onChange={(e) => setWindow(i, { start: e.target.value })}
              />
              <span className="text-gray-500 text-xs">–</span>
              <input
                className="flex-1 bg-gray-700 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs"
                type="time"
                value={w.end === '24:00' ? '23:59' : w.end}
                onChange={(e) => setWindow(i, { end: e.target.value })}
              />
            </div>

            {/* Day toggles — weekly only */}
            {!isHoliday && w.windowType !== 'date' && (
              <div className="flex gap-1 flex-wrap">
                {DAY_NAMES.map((d, dayIdx) => (
                  <button
                    key={d}
                    onClick={() => {
                      const next = days.includes(dayIdx)
                        ? days.filter((x) => x !== dayIdx)
                        : [...days, dayIdx].sort()
                      setWindow(i, { days: next })
                    }}
                    className={`text-[10px] px-1.5 py-0.5 rounded ${
                      days.includes(dayIdx)
                        ? 'bg-amber-700 text-amber-100'
                        : 'bg-gray-700 text-gray-400'
                    }`}
                  >
                    {d}
                  </button>
                ))}
              </div>
            )}
          </div>
        )
      })}

      {/* Add buttons */}
      <div className="flex gap-1.5 mt-1">
        <button
          onClick={addScheduleWindow}
          className="flex-1 text-xs text-amber-400 hover:text-amber-300 border border-amber-700 hover:border-amber-600 rounded py-1"
        >
          + Schedule
        </button>
        <button
          onClick={addHolidayWindow}
          className="flex-1 text-xs text-amber-400 hover:text-amber-300 border border-amber-700 hover:border-amber-600 rounded py-1"
        >
          + Holiday
        </button>
        <button
          onClick={addDateWindow}
          className="flex-1 text-xs text-violet-400 hover:text-violet-300 border border-violet-700 hover:border-violet-600 rounded py-1"
        >
          + Date
        </button>
      </div>
    </div>
  )
}

const CALLER_ID_VARS: { group: string; items: { tag: string; desc: string }[] }[] = [
  {
    group: 'Inbound call',
    items: [
      { tag: '{{caller.ani}}',       desc: "Caller's number (ANI)" },
      { tag: '{{call.dnis}}',        desc: 'Dialed number (DNIS / DID)' },
      { tag: '{{call.campaign_id}}', desc: 'Matched campaign ID' },
      { tag: '{{call.direction}}',   desc: '"inbound" or "outbound"' },
    ],
  },
  {
    group: 'SIP headers (via Get SIP Header node)',
    items: [
      { tag: '{{flow.original_ani}}',   desc: 'Example — X-Original-ANI extracted to flow.original_ani' },
      { tag: '{{flow.forwarded_for}}',  desc: 'Example — X-Forwarded-For extracted to flow.forwarded_for' },
    ],
  },
  {
    group: 'Flow variables (via Set Variable node)',
    items: [
      { tag: '{{flow.variable_name}}', desc: 'Any named variable set earlier in this flow' },
    ],
  },
]

const CANCEL_DIAL_VARS: { group: string; items: { tag: string; desc: string }[] }[] = [
  {
    group: 'Inbound call',
    items: [
      { tag: '{{caller.ani}}',       desc: "Original caller's number (ANI)" },
      { tag: '{{call.dnis}}',        desc: 'Number the caller dialed (DNIS)' },
      { tag: '{{call.campaign_id}}', desc: 'Campaign ID' },
    ],
  },
  {
    group: 'Time context',
    items: [
      { tag: '{{now.time}}',         desc: 'Current time (HH:mm)' },
      { tag: '{{now.day_name}}',     desc: 'Current day (e.g. Monday)' },
      { tag: '{{now.timezone}}',     desc: 'Flow timezone name' },
    ],
  },
  {
    group: 'Flow variables',
    items: [
      { tag: '{{flow.variable_name}}', desc: 'Any variable stored earlier in this flow' },
    ],
  },
]

function CancelDialVariableReference({ onInsert }: { onInsert: (tag: string) => void }) {
  return (
    <div className="bg-gray-800 border border-gray-700 rounded p-2 flex flex-col gap-0.5">
      {CANCEL_DIAL_VARS.map((group) => (
        <div key={group.group}>
          <p className="text-[10px] font-semibold text-gray-500 uppercase tracking-wide mt-1 mb-0.5 first:mt-0">
            {group.group}
          </p>
          {group.items.map(({ tag, desc }) => (
            <button
              key={tag}
              type="button"
              onClick={() => onInsert(tag)}
              title={`Insert ${tag}`}
              className="w-full flex items-start gap-2 text-left hover:bg-gray-700 rounded px-1.5 py-0.5"
            >
              <span className="font-mono text-[10px] text-orange-400 shrink-0 leading-4">{tag}</span>
              <span className="text-[10px] text-gray-500 leading-4">{desc}</span>
            </button>
          ))}
        </div>
      ))}
    </div>
  )
}

function CallerIdVariableReference({ onInsert }: { onInsert: (tag: string) => void }) {
  return (
    <div className="bg-gray-800 border border-gray-700 rounded p-2 flex flex-col gap-0.5">
      {CALLER_ID_VARS.map((group) => (
        <div key={group.group}>
          <p className="text-[10px] font-semibold text-gray-500 uppercase tracking-wide mt-1 mb-0.5 first:mt-0">
            {group.group}
          </p>
          {group.items.map(({ tag, desc }) => (
            <button
              key={tag}
              type="button"
              onClick={() => onInsert(tag)}
              title={`Insert ${tag}`}
              className="w-full flex items-start gap-2 text-left hover:bg-gray-700 rounded px-1.5 py-0.5"
            >
              <span className="font-mono text-[10px] text-sky-400 shrink-0 leading-4">{tag}</span>
              <span className="text-[10px] text-gray-500 leading-4">{desc}</span>
            </button>
          ))}
        </div>
      ))}
    </div>
  )
}

function DtmfNodeEditor({
  data,
  onChange,
}: {
  data: TelNodeData
  onChange: (patch: Partial<TelNodeData>) => void
}) {
  const inputCls = 'w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm focus:outline-none focus:border-yellow-500'
  const labelCls = 'block text-xs text-gray-400 mb-1'

  return (
    <div className="flex flex-col gap-3">
      <div>
        <label className={labelCls}>DTMF Digits</label>
        <input
          className={`${inputCls} font-mono tracking-widest`}
          placeholder="e.g. 1234# or {{flow.account}}"
          value={(data.digits as string) ?? ''}
          onChange={(e) => onChange({ digits: e.target.value })}
        />
        <p className="text-[10px] text-gray-500 mt-1 leading-snug">
          Valid chars: <span className="font-mono text-yellow-400">0–9  *  #  A–D</span>
          {' · '}
          <span className="font-mono text-yellow-400">w</span> = 500 ms pause
          {' · '}
          <span className="font-mono text-yellow-400">W</span> = 1 s pause
          {' · '}
          Supports <span className="font-mono text-yellow-400">{'{{variable}}'}</span>
        </p>
      </div>

      <div className="grid grid-cols-2 gap-2">
        <div>
          <label className={labelCls}>Tone Duration (ms)</label>
          <input
            type="number"
            min={50}
            max={5000}
            step={50}
            className={inputCls}
            value={(data.durationMs as number) ?? 100}
            onChange={(e) => onChange({ durationMs: parseInt(e.target.value) || 100 })}
          />
        </div>
        <div>
          <label className={labelCls}>Inter-Digit Gap (ms)</label>
          <input
            type="number"
            min={0}
            max={2000}
            step={10}
            className={inputCls}
            value={(data.interDigitGapMs as number) ?? 50}
            onChange={(e) => onChange({ interDigitGapMs: parseInt(e.target.value) || 0 })}
          />
        </div>
      </div>
      <p className="text-[10px] text-gray-500 -mt-1">
        Tone: how long each digit plays. Gap: silence between digits. Increase either if the destination misses digits.
      </p>

      <label className="flex items-center gap-2 cursor-pointer">
        <input
          type="checkbox"
          className="accent-yellow-500"
          checked={(data.waitForCompletion as boolean) ?? true}
          onChange={(e) => onChange({ waitForCompletion: e.target.checked })}
        />
        <span className="text-xs text-gray-300">Wait for tones to finish before continuing</span>
      </label>

      <div className="bg-gray-800 border border-gray-700 rounded p-2 text-xs text-gray-400 leading-relaxed">
        <strong className="text-gray-300 block mb-1">Exit handle: <span className="text-yellow-400">Default</span></strong>
        {(data.waitForCompletion as boolean) ?? true ? (
          <span>Flow pauses until all tones finish, then continues. Duration is calculated from digit count × tone duration + pause chars.</span>
        ) : (
          <span>FreeSWITCH queues the tones asynchronously — the flow continues immediately.</span>
        )}
        <p className="text-[10px] text-gray-600 mt-1">Invalid characters and unresolved variables are stripped.</p>
      </div>
    </div>
  )
}

const TTS_VOICES = [
  { value: 'kal', label: 'Male (kal)' },
  { value: 'slt', label: 'Female (slt)' },
  { value: 'awb', label: 'Male Scottish (awb)' },
  { value: 'rms', label: 'Male Alt (rms)' },
]

type RecordPhase = 'idle' | 'requesting' | 'recording' | 'review'

function getBestMimeType(): string {
  const types = ['audio/webm;codecs=opus', 'audio/webm', 'audio/ogg;codecs=opus', 'audio/ogg', 'audio/mp4']
  return types.find((t) => MediaRecorder.isTypeSupported(t)) ?? ''
}

function mimeToExt(mime: string): string {
  if (mime.includes('ogg')) return '.ogg'
  if (mime.includes('mp4')) return '.mp4'
  return '.webm'
}

function fmtTime(s: number) {
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
}

function PlayNodeEditor({
  data,
  onChange,
}: {
  data: TelNodeData
  onChange: (patch: Partial<TelNodeData>) => void
}) {
  const [audioFiles, setAudioFiles] = useState<AudioFileRecord[]>([])
  const [uploading, setUploading] = useState(false)
  const [uploadError, setUploadError] = useState('')

  // Preview state
  const [previewBlobUrl, setPreviewBlobUrl] = useState('')
  const [previewLoading, setPreviewLoading] = useState(false)
  const lastPreviewedId = useRef('')

  // Recorder state
  const [recordPhase, setRecordPhase] = useState<RecordPhase>('idle')
  const [recordSeconds, setRecordSeconds] = useState(0)
  const [recordBlobUrl, setRecordBlobUrl] = useState('')
  const [recordMimeType, setRecordMimeType] = useState('')
  const [recordName, setRecordName] = useState('New Recording')
  const [savingRecording, setSavingRecording] = useState(false)
  const [saveError, setSaveError] = useState('')
  const mediaRecorderRef = useRef<MediaRecorder | null>(null)
  const chunksRef = useRef<Blob[]>([])
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const recordBlobRef = useRef<Blob | null>(null)

  useEffect(() => {
    audioFilesApi.list().then(setAudioFiles).catch(() => setAudioFiles([]))
  }, [])

  // Stop recorder and free tracks on unmount
  useEffect(() => {
    return () => {
      if (timerRef.current) clearInterval(timerRef.current)
      if (mediaRecorderRef.current && mediaRecorderRef.current.state !== 'inactive') {
        mediaRecorderRef.current.stop()
      }
      streamRef.current?.getTracks().forEach((t) => t.stop())
    }
  }, [])

  const audioSource = (data.audioSource as string) ?? 'file'
  const autoRestart = (data.autoRestart as boolean) ?? false
  const selectedFileId = (data.audioFileId as string) ?? ''

  // Whether selected file is a tenant-uploaded file (UUID) vs built-in or stream
  const isUploadedFile =
    selectedFileId.length > 0 &&
    !selectedFileId.startsWith('local_stream://') &&
    !selectedFileId.startsWith('silence_stream://') &&
    !selectedFileId.startsWith('tone_stream://') &&
    !selectedFileId.startsWith('__builtin:')

  // Clear preview when a different file is selected
  useEffect(() => {
    if (lastPreviewedId.current !== selectedFileId) {
      lastPreviewedId.current = selectedFileId
      if (previewBlobUrl) {
        URL.revokeObjectURL(previewBlobUrl)
        setPreviewBlobUrl('')
      }
    }
  }, [selectedFileId, previewBlobUrl])

  async function handleLoadPreview() {
    if (!isUploadedFile) return
    setPreviewLoading(true)
    try {
      if (previewBlobUrl) URL.revokeObjectURL(previewBlobUrl)
      const url = await audioFilesApi.fetchBlobUrl(selectedFileId)
      setPreviewBlobUrl(url)
    } catch { /* silent */ }
    finally { setPreviewLoading(false) }
  }

  async function handleUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    setUploading(true)
    setUploadError('')
    try {
      const uploaded = await audioFilesApi.upload(file)
      setAudioFiles((prev) => [...prev, uploaded])
      onChange({ audioFileId: uploaded.id })
    } catch (err) {
      setUploadError(err instanceof Error ? err.message : 'Upload failed')
    } finally {
      setUploading(false)
      e.target.value = ''
    }
  }

  async function startRecording() {
    setRecordPhase('requesting')
    setSaveError('')
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
      streamRef.current = stream
      chunksRef.current = []

      const mimeType = getBestMimeType()
      setRecordMimeType(mimeType)

      const mr = new MediaRecorder(stream, mimeType ? { mimeType } : undefined)
      mediaRecorderRef.current = mr

      mr.ondataavailable = (e) => { if (e.data.size > 0) chunksRef.current.push(e.data) }
      mr.onstop = () => {
        const blob = new Blob(chunksRef.current, { type: mimeType || 'audio/webm' })
        recordBlobRef.current = blob
        const url = URL.createObjectURL(blob)
        setRecordBlobUrl(url)
        setRecordPhase('review')
        stream.getTracks().forEach((t) => t.stop())
        streamRef.current = null
      }

      mr.start(100)
      setRecordSeconds(0)
      setRecordPhase('recording')
      timerRef.current = setInterval(() => setRecordSeconds((s) => s + 1), 1000)
    } catch {
      setRecordPhase('idle')
    }
  }

  function stopRecording() {
    if (timerRef.current) { clearInterval(timerRef.current); timerRef.current = null }
    mediaRecorderRef.current?.stop()
  }

  function discardRecording() {
    if (recordBlobUrl) URL.revokeObjectURL(recordBlobUrl)
    setRecordBlobUrl('')
    recordBlobRef.current = null
    setRecordPhase('idle')
    setRecordSeconds(0)
    setRecordName('New Recording')
    setSaveError('')
  }

  async function saveRecording() {
    if (!recordBlobRef.current) return
    setSavingRecording(true)
    setSaveError('')
    try {
      const ext = mimeToExt(recordMimeType)
      const file = new File([recordBlobRef.current], `recording${ext}`, { type: recordMimeType || 'audio/webm' })
      const uploaded = await audioFilesApi.upload(file, recordName.trim() || 'New Recording')
      setAudioFiles((prev) => [...prev, uploaded])
      onChange({ audioFileId: uploaded.id })
      discardRecording()
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : 'Save failed')
    } finally {
      setSavingRecording(false)
    }
  }

  const inputCls = 'w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm focus:outline-none focus:border-teal-500'
  const labelCls = 'block text-xs text-gray-400 mb-1'

  return (
    <div className="flex flex-col gap-3">
      {/* Audio source toggle */}
      <div>
        <label className={labelCls}>Audio Source</label>
        <div className="flex gap-1">
          {(['file', 'tts'] as const).map((src) => (
            <button
              key={src}
              onClick={() => onChange({ audioSource: src })}
              className={`flex-1 text-xs rounded py-1.5 border transition-colors ${
                audioSource === src
                  ? 'bg-teal-700 border-teal-600 text-white'
                  : 'bg-gray-800 border-gray-600 text-gray-400 hover:text-gray-200'
              }`}
            >
              {src === 'file' ? 'Audio File' : 'Text to Speech (TTS)'}
            </button>
          ))}
        </div>
      </div>

      {audioSource === 'file' && (
        <>
          {/* File selector */}
          <div>
            <label className={labelCls}>Audio File</label>
            <select
              className={inputCls}
              value={selectedFileId}
              onChange={(e) => onChange({ audioFileId: e.target.value })}
            >
              <option value="">— Select audio —</option>
              {BUILTIN_AUDIO_GROUPS.map((grp) => (
                <optgroup key={grp.group} label={grp.group}>
                  {grp.options.map((b) => (
                    <option key={b.value} value={b.value}>{b.label}</option>
                  ))}
                </optgroup>
              ))}
              {audioFiles.length > 0 && (
                <optgroup label="Uploaded Files">
                  {audioFiles.map((f) => (
                    <option key={f.id} value={f.id}>{f.name}</option>
                  ))}
                </optgroup>
              )}
            </select>

            {/* Preview — only for uploaded files */}
            {isUploadedFile && (
              <div className="mt-2">
                {previewBlobUrl ? (
                  <audio
                    controls
                    src={previewBlobUrl}
                    className="w-full"
                    style={{ filter: 'invert(0.88) hue-rotate(180deg) brightness(0.85)' }}
                  />
                ) : (
                  <button
                    onClick={handleLoadPreview}
                    disabled={previewLoading}
                    className="text-xs text-teal-400 hover:text-teal-300 disabled:opacity-50"
                  >
                    {previewLoading ? 'Loading…' : '▶ Preview selected file'}
                  </button>
                )}
              </div>
            )}
          </div>

          {/* Upload + Record row */}
          {recordPhase === 'idle' && (
            <div className="flex gap-1.5">
              <label className="flex-1 cursor-pointer">
                <input
                  type="file"
                  accept=".wav,.mp3,.ogg,.webm,.mp4,audio/*"
                  className="hidden"
                  onChange={handleUpload}
                  disabled={uploading}
                />
                <span className="block text-center text-xs bg-gray-700 hover:bg-gray-600 text-gray-200 border border-gray-600 rounded px-2 py-1.5 transition-colors">
                  {uploading ? 'Uploading…' : '↑ Upload'}
                </span>
              </label>
              <button
                onClick={startRecording}
                className="flex-1 text-xs bg-gray-700 hover:bg-gray-600 text-gray-200 border border-gray-600 rounded px-2 py-1.5 transition-colors"
              >
                ● Record
              </button>
            </div>
          )}

          {uploadError && <p className="text-xs text-red-400">{uploadError}</p>}

          {/* Requesting mic */}
          {recordPhase === 'requesting' && (
            <p className="text-xs text-gray-400 italic">Requesting microphone access…</p>
          )}

          {/* Recording in progress */}
          {recordPhase === 'recording' && (
            <div className="bg-gray-800 border border-red-800 rounded p-2 flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-red-500 animate-pulse shrink-0" />
              <span className="text-xs text-red-300 font-mono flex-1">{fmtTime(recordSeconds)}</span>
              <button
                onClick={stopRecording}
                className="text-xs bg-red-900 hover:bg-red-800 text-red-200 rounded px-2 py-1"
              >
                ■ Stop
              </button>
            </div>
          )}

          {/* Review recording */}
          {recordPhase === 'review' && recordBlobUrl && (
            <div className="bg-gray-800 border border-gray-700 rounded p-2 flex flex-col gap-2">
              <audio
                controls
                src={recordBlobUrl}
                className="w-full"
                style={{ filter: 'invert(0.88) hue-rotate(180deg) brightness(0.85)' }}
              />
              <input
                className="w-full bg-gray-700 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs focus:outline-none focus:border-teal-500"
                value={recordName}
                onChange={(e) => setRecordName(e.target.value)}
                placeholder="Recording name…"
              />
              {saveError && <p className="text-xs text-red-400">{saveError}</p>}
              <div className="flex gap-1.5">
                <button
                  onClick={saveRecording}
                  disabled={savingRecording}
                  className="flex-1 text-xs bg-teal-700 hover:bg-teal-600 text-white rounded py-1.5 disabled:opacity-50"
                >
                  {savingRecording ? 'Saving…' : '✓ Save & Select'}
                </button>
                <button
                  onClick={discardRecording}
                  disabled={savingRecording}
                  className="text-xs bg-gray-700 hover:bg-gray-600 text-gray-300 rounded px-3 py-1.5 disabled:opacity-50"
                >
                  Discard
                </button>
              </div>
            </div>
          )}
        </>
      )}

      {audioSource === 'tts' && (
        <>
          <div>
            <label className={labelCls}>TTS Text</label>
            <textarea
              rows={3}
              className={`${inputCls} resize-y`}
              placeholder="Text to speak…"
              value={(data.ttsText as string) ?? ''}
              onChange={(e) => onChange({ ttsText: e.target.value })}
            />
            <p className="text-xs text-gray-500 mt-1">
              Requires <span className="font-mono text-teal-400">freeswitch-mod-flite</span> in the FreeSWITCH container.
            </p>
          </div>
          <div>
            <label className={labelCls}>Voice</label>
            <select
              className={inputCls}
              value={(data.ttsVoice as string) ?? 'kal'}
              onChange={(e) => onChange({ ttsVoice: e.target.value })}
            >
              {TTS_VOICES.map((v) => (
                <option key={v.value} value={v.value}>{v.label}</option>
              ))}
            </select>
          </div>
        </>
      )}

      {/* Playback controls */}
      <div className="grid grid-cols-2 gap-2">
        <div>
          <label className={labelCls}>Duration (sec, 0=full)</label>
          <input
            type="number"
            min={0}
            className={inputCls}
            value={(data.durationSeconds as number) ?? 0}
            onChange={(e) => onChange({ durationSeconds: parseInt(e.target.value) || 0 })}
          />
        </div>
        <div>
          <label className={labelCls}>Start Offset (sec)</label>
          <input
            type="number"
            min={0}
            className={inputCls}
            value={(data.startOffsetSeconds as number) ?? 0}
            onChange={(e) => onChange({ startOffsetSeconds: parseInt(e.target.value) || 0 })}
          />
        </div>
      </div>

      {/* Flags */}
      <div className="flex flex-col gap-1.5">
        {audioSource !== 'tts' && (
          <label className="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              className="accent-teal-500"
              checked={(data.autoRestart as boolean) ?? false}
              onChange={(e) => onChange({ autoRestart: e.target.checked })}
            />
            <span className="text-xs text-gray-300">Auto-restart (loop indefinitely)</span>
          </label>
        )}
        <label className="flex items-center gap-2 cursor-pointer">
          <input
            type="checkbox"
            className="accent-teal-500"
            checked={(data.rememberPosition as boolean) ?? false}
            onChange={(e) => onChange({ rememberPosition: e.target.checked })}
          />
          <span className="text-xs text-gray-300">Remember position (resume from last offset)</span>
        </label>
      </div>

      {/* Periodic announcement playlist — only meaningful when looping */}
      {autoRestart && audioSource !== 'tts' && (
        <PeriodicAnnouncementEditor
          announcements={(data.periodicAnnouncements as Array<{ fileId: string }>) ?? []}
          intervalSeconds={(data.periodicAnnouncementIntervalSeconds as number) ?? 30}
          audioFiles={audioFiles}
          onChange={(announcements, intervalSeconds) =>
            onChange({ periodicAnnouncements: announcements, periodicAnnouncementIntervalSeconds: intervalSeconds })
          }
        />
      )}

      {/* Exit handle info */}
      <div className="bg-gray-800 border border-gray-700 rounded p-2 text-xs text-gray-400 leading-relaxed">
        <strong className="text-gray-300 block mb-1">Exit handles:</strong>
        {audioSource === 'tts' ? (
          <span><span className="text-teal-400">TTS Finished</span> — fires when speech ends</span>
        ) : autoRestart ? (
          <span>No exit (loops forever until bridged or hung up)</span>
        ) : (
          <span><span className="text-teal-400">End Of Play Stream</span> — fires when file ends</span>
        )}
        {(data.durationSeconds as number) > 0 && (
          <div><span className="text-teal-400">Duration Reached</span> — fires at {data.durationSeconds as number}s</div>
        )}
      </div>
    </div>
  )
}

function PeriodicAnnouncementEditor({
  announcements,
  intervalSeconds,
  audioFiles,
  onChange,
}: {
  announcements: Array<{ fileId: string }>
  intervalSeconds: number
  audioFiles: AudioFileRecord[]
  onChange: (announcements: Array<{ fileId: string }>, intervalSeconds: number) => void
}) {
  const setItem = (i: number, fileId: string) => {
    const next = announcements.map((a, idx) => (idx === i ? { fileId } : a))
    onChange(next, intervalSeconds)
  }

  const removeItem = (i: number) => {
    onChange(announcements.filter((_, idx) => idx !== i), intervalSeconds)
  }

  const moveUp = (i: number) => {
    if (i === 0) return
    const next = [...announcements]
    ;[next[i - 1], next[i]] = [next[i], next[i - 1]]
    onChange(next, intervalSeconds)
  }

  const moveDown = (i: number) => {
    if (i === announcements.length - 1) return
    const next = [...announcements]
    ;[next[i], next[i + 1]] = [next[i + 1], next[i]]
    onChange(next, intervalSeconds)
  }

  const addItem = () => onChange([...announcements, { fileId: '' }], intervalSeconds)

  // Only WAV files work as periodic announcements — streaming/looping tones never fire PLAYBACK_STOP
  const announcementOptions = BUILTIN_AUDIO_OPTIONS.filter((b) => b.value.startsWith('__builtin:'))

  return (
    <div className="border-t border-gray-700 pt-3 flex flex-col gap-2">
      <p className="text-xs text-gray-400 font-medium">Periodic Announcements</p>

      <div className="flex items-center gap-2">
        <label className="text-xs text-gray-400 shrink-0">Every</label>
        <input
          type="number"
          min={5}
          className="flex-1 bg-gray-800 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs focus:outline-none focus:border-teal-500"
          value={intervalSeconds}
          onChange={(e) => onChange(announcements, parseInt(e.target.value) || 30)}
        />
        <label className="text-xs text-gray-400 shrink-0">seconds, play next:</label>
      </div>

      {announcements.length === 0 && (
        <p className="text-xs text-gray-600 italic">No announcements — add one below.</p>
      )}

      {announcements.map((item, i) => (
        <div key={i} className="flex gap-1 items-center">
          <select
            className="flex-1 min-w-0 bg-gray-800 border border-gray-600 rounded px-1.5 py-1 text-gray-100 text-xs focus:outline-none focus:border-teal-500"
            value={item.fileId}
            onChange={(e) => setItem(i, e.target.value)}
          >
            <option value="">— Select —</option>
            <optgroup label="Built-In">
              {announcementOptions.map((b) => (
                <option key={b.value} value={b.value}>{b.label}</option>
              ))}
            </optgroup>
            {audioFiles.length > 0 && (
              <optgroup label="Uploaded">
                {audioFiles.map((f) => (
                  <option key={f.id} value={f.id}>{f.name}</option>
                ))}
              </optgroup>
            )}
          </select>
          <button
            onClick={() => moveUp(i)}
            disabled={i === 0}
            className="text-gray-500 hover:text-gray-300 disabled:opacity-30 text-xs px-1"
            title="Move up"
          >▲</button>
          <button
            onClick={() => moveDown(i)}
            disabled={i === announcements.length - 1}
            className="text-gray-500 hover:text-gray-300 disabled:opacity-30 text-xs px-1"
            title="Move down"
          >▼</button>
          <button
            onClick={() => removeItem(i)}
            className="text-red-500 hover:text-red-400 text-xs px-1"
            title="Remove"
          >✕</button>
        </div>
      ))}

      <button
        onClick={addItem}
        className="text-xs text-teal-400 hover:text-teal-300 border border-teal-800 hover:border-teal-700 rounded py-1"
      >
        + Add Announcement
      </button>

      <p className="text-[10px] text-gray-500 leading-snug">
        Plays in order, cycling back to the first after the last. Each plays after the configured interval elapses.
      </p>
    </div>
  )
}

function WhisperNodeEditor({
  data,
  onChange,
}: {
  data: TelNodeData
  onChange: (patch: Partial<TelNodeData>) => void
}) {
  const [audioFiles, setAudioFiles] = useState<AudioFileRecord[]>([])
  const [uploading, setUploading] = useState(false)
  const [uploadError, setUploadError] = useState('')

  const [previewBlobUrl, setPreviewBlobUrl] = useState('')
  const [previewLoading, setPreviewLoading] = useState(false)
  const lastPreviewedId = useRef('')

  const [recordPhase, setRecordPhase] = useState<RecordPhase>('idle')
  const [recordSeconds, setRecordSeconds] = useState(0)
  const [recordBlobUrl, setRecordBlobUrl] = useState('')
  const [recordMimeType, setRecordMimeType] = useState('')
  const [recordName, setRecordName] = useState('New Recording')
  const [savingRecording, setSavingRecording] = useState(false)
  const [saveError, setSaveError] = useState('')
  const mediaRecorderRef = useRef<MediaRecorder | null>(null)
  const chunksRef = useRef<Blob[]>([])
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const recordBlobRef = useRef<Blob | null>(null)

  useEffect(() => {
    audioFilesApi.list().then(setAudioFiles).catch(() => setAudioFiles([]))
  }, [])

  useEffect(() => {
    return () => {
      if (timerRef.current) clearInterval(timerRef.current)
      if (mediaRecorderRef.current && mediaRecorderRef.current.state !== 'inactive')
        mediaRecorderRef.current.stop()
      streamRef.current?.getTracks().forEach((t) => t.stop())
    }
  }, [])

  const selectedFileId = (data.audioFileId as string) ?? ''
  const isUploadedFile =
    selectedFileId.length > 0 &&
    !selectedFileId.startsWith('local_stream://') &&
    !selectedFileId.startsWith('silence_stream://') &&
    !selectedFileId.startsWith('tone_stream://') &&
    !selectedFileId.startsWith('__builtin:')

  useEffect(() => {
    if (lastPreviewedId.current !== selectedFileId) {
      lastPreviewedId.current = selectedFileId
      if (previewBlobUrl) { URL.revokeObjectURL(previewBlobUrl); setPreviewBlobUrl('') }
    }
  }, [selectedFileId, previewBlobUrl])

  async function handleLoadPreview() {
    if (!isUploadedFile) return
    setPreviewLoading(true)
    try {
      if (previewBlobUrl) URL.revokeObjectURL(previewBlobUrl)
      setPreviewBlobUrl(await audioFilesApi.fetchBlobUrl(selectedFileId))
    } catch { /* silent */ }
    finally { setPreviewLoading(false) }
  }

  async function handleUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    setUploading(true); setUploadError('')
    try {
      const uploaded = await audioFilesApi.upload(file)
      setAudioFiles((prev) => [...prev, uploaded])
      onChange({ audioFileId: uploaded.id })
    } catch (err) {
      setUploadError(err instanceof Error ? err.message : 'Upload failed')
    } finally { setUploading(false); e.target.value = '' }
  }

  async function startRecording() {
    setRecordPhase('requesting'); setSaveError('')
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
      streamRef.current = stream
      chunksRef.current = []
      const mimeType = getBestMimeType()
      setRecordMimeType(mimeType)
      const mr = new MediaRecorder(stream, mimeType ? { mimeType } : undefined)
      mediaRecorderRef.current = mr
      mr.ondataavailable = (e) => { if (e.data.size > 0) chunksRef.current.push(e.data) }
      mr.onstop = () => {
        const blob = new Blob(chunksRef.current, { type: mimeType || 'audio/webm' })
        recordBlobRef.current = blob
        setRecordBlobUrl(URL.createObjectURL(blob))
        setRecordPhase('review')
        stream.getTracks().forEach((t) => t.stop())
        streamRef.current = null
      }
      mr.start(100)
      setRecordSeconds(0); setRecordPhase('recording')
      timerRef.current = setInterval(() => setRecordSeconds((s) => s + 1), 1000)
    } catch { setRecordPhase('idle') }
  }

  function stopRecording() {
    if (timerRef.current) { clearInterval(timerRef.current); timerRef.current = null }
    mediaRecorderRef.current?.stop()
  }

  function discardRecording() {
    if (recordBlobUrl) URL.revokeObjectURL(recordBlobUrl)
    setRecordBlobUrl(''); recordBlobRef.current = null
    setRecordPhase('idle'); setRecordSeconds(0); setRecordName('New Recording'); setSaveError('')
  }

  async function saveRecording() {
    if (!recordBlobRef.current) return
    setSavingRecording(true); setSaveError('')
    try {
      const ext = mimeToExt(recordMimeType)
      const file = new File([recordBlobRef.current], `recording${ext}`, { type: recordMimeType || 'audio/webm' })
      const uploaded = await audioFilesApi.upload(file, recordName.trim() || 'New Recording')
      setAudioFiles((prev) => [...prev, uploaded])
      onChange({ audioFileId: uploaded.id })
      discardRecording()
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : 'Save failed')
    } finally { setSavingRecording(false) }
  }

  const inputCls = 'w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm focus:outline-none focus:border-purple-500'
  const labelCls = 'block text-xs text-gray-400 mb-1'

  // Whisper is a one-shot finite audio clip — only WAV files work (looping streams/tones never
  // fire PLAYBACK_STOP so the bridge would never be triggered)
  const whisperBuiltins = BUILTIN_AUDIO_OPTIONS.filter((b) => b.value.startsWith('__builtin:'))

  return (
    <div className="flex flex-col gap-3">
      <div>
        <label className={labelCls}>Audio File (plays on agent's ear only)</label>
        <select
          className={inputCls}
          value={selectedFileId}
          onChange={(e) => onChange({ audioFileId: e.target.value })}
        >
          <option value="">— Select audio —</option>
          {whisperBuiltins.length > 0 && (
            <optgroup label="Built-In Options">
              {whisperBuiltins.map((b) => (
                <option key={b.value} value={b.value}>{b.label}</option>
              ))}
            </optgroup>
          )}
          {audioFiles.length > 0 && (
            <optgroup label="Uploaded Files">
              {audioFiles.map((f) => (
                <option key={f.id} value={f.id}>{f.name}</option>
              ))}
            </optgroup>
          )}
        </select>

        {isUploadedFile && (
          <div className="mt-2">
            {previewBlobUrl ? (
              <audio controls src={previewBlobUrl} className="w-full"
                style={{ filter: 'invert(0.88) hue-rotate(180deg) brightness(0.85)' }} />
            ) : (
              <button onClick={handleLoadPreview} disabled={previewLoading}
                className="text-xs text-purple-400 hover:text-purple-300 disabled:opacity-50">
                {previewLoading ? 'Loading…' : '▶ Preview selected file'}
              </button>
            )}
          </div>
        )}
      </div>

      {recordPhase === 'idle' && (
        <div className="flex gap-1.5">
          <label className="flex-1 cursor-pointer">
            <input type="file" accept=".wav,.mp3,.ogg,.webm,.mp4,audio/*" className="hidden"
              onChange={handleUpload} disabled={uploading} />
            <span className="block text-center text-xs bg-gray-700 hover:bg-gray-600 text-gray-200 border border-gray-600 rounded px-2 py-1.5 transition-colors">
              {uploading ? 'Uploading…' : '↑ Upload'}
            </span>
          </label>
          <button onClick={startRecording}
            className="flex-1 text-xs bg-gray-700 hover:bg-gray-600 text-gray-200 border border-gray-600 rounded px-2 py-1.5 transition-colors">
            ● Record
          </button>
        </div>
      )}

      {uploadError && <p className="text-xs text-red-400">{uploadError}</p>}

      {recordPhase === 'requesting' && (
        <p className="text-xs text-gray-400 italic">Requesting microphone access…</p>
      )}

      {recordPhase === 'recording' && (
        <div className="bg-gray-800 border border-red-800 rounded p-2 flex items-center gap-2">
          <span className="w-2 h-2 rounded-full bg-red-500 animate-pulse shrink-0" />
          <span className="text-xs text-red-300 font-mono flex-1">{fmtTime(recordSeconds)}</span>
          <button onClick={stopRecording}
            className="text-xs bg-red-900 hover:bg-red-800 text-red-200 rounded px-2 py-1">
            ■ Stop
          </button>
        </div>
      )}

      {recordPhase === 'review' && recordBlobUrl && (
        <div className="bg-gray-800 border border-gray-700 rounded p-2 flex flex-col gap-2">
          <audio controls src={recordBlobUrl} className="w-full"
            style={{ filter: 'invert(0.88) hue-rotate(180deg) brightness(0.85)' }} />
          <input className="w-full bg-gray-700 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs focus:outline-none focus:border-purple-500"
            value={recordName} onChange={(e) => setRecordName(e.target.value)} placeholder="Recording name…" />
          {saveError && <p className="text-xs text-red-400">{saveError}</p>}
          <div className="flex gap-1.5">
            <button onClick={saveRecording} disabled={savingRecording}
              className="flex-1 text-xs bg-purple-700 hover:bg-purple-600 text-white rounded py-1.5 disabled:opacity-50">
              {savingRecording ? 'Saving…' : '✓ Save & Select'}
            </button>
            <button onClick={discardRecording} disabled={savingRecording}
              className="text-xs bg-gray-700 hover:bg-gray-600 text-gray-300 rounded px-3 py-1.5 disabled:opacity-50">
              Discard
            </button>
          </div>
        </div>
      )}

      <div className="bg-gray-800 border border-gray-700 rounded p-2 text-xs text-gray-400 leading-relaxed">
        <strong className="text-gray-300 block mb-1">Exit handle:</strong>
        <span><span className="text-purple-400">Default</span> — continues after the whisper plays</span>
        <p className="text-[10px] text-gray-500 mt-1">
          Caller and agent audio are bridged when this branch reaches a <strong className="text-gray-400">tf_end</strong> node.
        </p>
      </div>
    </div>
  )
}

function SetVariableEditor({
  assignments,
  onChange,
}: {
  assignments: TelVariableAssignment[]
  onChange: (a: TelVariableAssignment[]) => void
}) {
  const setRow = (i: number, patch: Partial<TelVariableAssignment>) =>
    onChange(assignments.map((a, idx) => (idx === i ? { ...a, ...patch } : a)))

  const removeRow = (i: number) => onChange(assignments.filter((_, idx) => idx !== i))

  const addRow = () => onChange([...assignments, { key: '', value: '' }])

  return (
    <div className="flex flex-col gap-1.5">
      <label className="text-xs text-gray-400">Variable Assignments</label>
      {assignments.map((a, i) => (
        <div key={i} className="flex gap-1 items-center">
          <input
            className="flex-1 min-w-0 bg-gray-800 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs font-mono focus:outline-none focus:border-violet-500"
            placeholder="variable"
            value={a.key}
            onChange={(e) => setRow(i, { key: e.target.value })}
          />
          <span className="text-gray-500 text-xs">=</span>
          <input
            className="flex-1 min-w-0 bg-gray-800 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs font-mono focus:outline-none focus:border-violet-500"
            placeholder="value or {{var}}"
            value={a.value}
            onChange={(e) => setRow(i, { value: e.target.value })}
          />
          <button onClick={() => removeRow(i)} className="text-red-400 hover:text-red-300 text-xs px-1 shrink-0">✕</button>
        </div>
      ))}
      <button
        onClick={addRow}
        className="text-xs text-violet-400 hover:text-violet-300 border border-violet-700 hover:border-violet-600 rounded py-1 mt-0.5"
      >
        + Add Assignment
      </button>
    </div>
  )
}
