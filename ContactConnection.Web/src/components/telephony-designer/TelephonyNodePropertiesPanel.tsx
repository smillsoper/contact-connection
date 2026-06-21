import type { Node } from '@xyflow/react'
import type { TelNodeData, TelephonyNodeType, TimeWindow, TelVariableAssignment } from '../../types/telephony-designer'
import { TELEPHONY_NODE_META } from '../../types/telephony-designer'

const DAY_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

const TIMEZONE_GROUPS: { group: string; options: { label: string; value: string }[] }[] = [
  {
    group: 'United States',
    options: [
      { label: 'Eastern Time (ET)', value: 'America/New_York' },
      { label: 'Central Time (CT)', value: 'America/Chicago' },
      { label: 'Mountain Time (MT)', value: 'America/Denver' },
      { label: 'Pacific Time (PT)', value: 'America/Los_Angeles' },
      { label: 'Alaska Time (AKT)', value: 'America/Anchorage' },
      { label: 'Hawaii–Aleutian (HT)', value: 'Pacific/Honolulu' },
      { label: 'Arizona — no DST', value: 'America/Phoenix' },
      { label: 'Indiana East — no DST', value: 'America/Indiana/Indianapolis' },
    ],
  },
  {
    group: 'Canada',
    options: [
      { label: 'Atlantic Time (AT)', value: 'America/Halifax' },
      { label: 'Newfoundland (NT)', value: 'America/St_Johns' },
    ],
  },
  {
    group: 'Latin America',
    options: [
      { label: 'Mexico City (CT)', value: 'America/Mexico_City' },
      { label: 'São Paulo', value: 'America/Sao_Paulo' },
    ],
  },
  {
    group: 'Europe',
    options: [
      { label: 'London (GMT/BST)', value: 'Europe/London' },
      { label: 'Paris / Berlin (CET/CEST)', value: 'Europe/Paris' },
      { label: 'Helsinki / Athens (EET/EEST)', value: 'Europe/Helsinki' },
      { label: 'Moscow (MSK)', value: 'Europe/Moscow' },
    ],
  },
  {
    group: 'Asia / Pacific',
    options: [
      { label: 'India Standard Time (IST)', value: 'Asia/Kolkata' },
      { label: 'China Standard Time (CST)', value: 'Asia/Shanghai' },
      { label: 'Japan Standard Time (JST)', value: 'Asia/Tokyo' },
      { label: 'Australia Eastern (AEST/AEDT)', value: 'Australia/Sydney' },
      { label: 'New Zealand (NZST/NZDT)', value: 'Pacific/Auckland' },
    ],
  },
]

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

      {/* Entry + Delete */}
      <div className="flex gap-2 pt-2 border-t border-gray-700 mt-auto">
        {!isEntry && (
          <button
            onClick={() => onSetEntry(node.id)}
            className="flex-1 text-xs bg-gray-700 hover:bg-gray-600 text-gray-200 rounded py-1.5"
          >
            Set as Entry
          </button>
        )}
        {isEntry && (
          <span className="flex-1 text-xs text-center text-green-400 py-1.5">Entry node ✓</span>
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
