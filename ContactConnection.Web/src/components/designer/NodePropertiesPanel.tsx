import { useEffect, useMemo, useRef, useState } from 'react'
import type { Node, Edge } from '@xyflow/react'
import type { NodeData, ContactConnectionNodeType } from '../../types/designer'
import ScriptContentEditor from './ScriptContentEditor'
import VariablePanel from './VariablePanel'
import { computeAncestorVars } from '../../utils/flowGraph'
import { flowsApi } from '../../api/flows'
import type { FlowSummary } from '../../api/flows'

const PRESET_MASKS = [
  { label: 'None', value: '' },
  { label: 'Phone — (555) 555-5555', value: '(000) 000-0000' },
  { label: 'Date — MM/DD/YYYY', value: '00/00/0000' },
  { label: 'SSN — 000-00-0000', value: '000-00-0000' },
  { label: 'ZIP Code — 5 digit', value: '00000' },
  { label: 'ZIP+4 — 00000-0000', value: '00000-0000' },
  { label: 'ZIP — US or Canada', value: '__zip_ca__' },
  { label: 'EIN — 00-0000000', value: '00-0000000' },
  { label: 'Credit Card', value: '0000 0000 0000 0000' },
  { label: 'Time — HH:MM', value: '00:00' },
  { label: 'Custom…', value: '__custom__' },
]

interface Props {
  node: Node<NodeData>
  isEntry: boolean
  onUpdate: (id: string, data: Partial<NodeData>) => void
  onSetEntry: (id: string) => void
  onDelete: (id: string) => void
  onClose: () => void
  /** Flow graph context — enables flow-aware variable panels. */
  nodes: Node<NodeData>[]
  edges: Edge[]
  entryNodeId: string | null
}

export default function NodePropertiesPanel({
  node, isEntry, onUpdate, onSetEntry, onDelete, onClose,
  nodes, edges,
}: Props) {
  const type = node.type as ContactConnectionNodeType
  const data = node.data

  // Flow-aware variables at this node's position (memoised — recomputes when graph changes)
  const flowVars = useMemo(
    () => computeAncestorVars(node.id, nodes, edges),
    [node.id, nodes, edges],
  )

  // Variable panel toggle for set_variable node
  const [varPanelOpen, setVarPanelOpen] = useState(false)

  // Flow list for execute_flow / transition_to_flow nodes — fetched lazily
  const [availableFlows, setAvailableFlows] = useState<FlowSummary[]>([])
  useEffect(() => {
    if (type !== 'execute_flow' && type !== 'transition_to_flow') return
    flowsApi.listAll().then(setAvailableFlows).catch(console.error)
  }, [type])

  // Address node — active script tab ('main' | field key)
  const [addrScriptTab, setAddrScriptTab] = useState('main')

  // Track the last-focused assignment field so clicking a variable inserts there
  const lastFocused = useRef<{ index: number; side: 'variable' | 'value'; el: HTMLInputElement } | null>(null)

  function handleVarInsert(token: string) {
    const f = lastFocused.current
    if (!f) {
      navigator.clipboard.writeText(token).catch(() => {})
      return
    }
    const el = f.el
    const start = el.selectionStart ?? el.value.length
    const end   = el.selectionEnd   ?? el.value.length
    const newVal = el.value.slice(0, start) + token + el.value.slice(end)
    const assignments = (data.assignments as { variable: string; value: string }[]) ?? []
    const next = [...assignments]
    if (f.side === 'variable') next[f.index] = { ...next[f.index], variable: newVal }
    else                       next[f.index] = { ...next[f.index], value: newVal }
    onUpdate(node.id, { assignments: next })
    // Restore focus + advance cursor after React re-renders
    requestAnimationFrame(() => {
      el.focus()
      const pos = start + token.length
      el.setSelectionRange(pos, pos)
    })
  }

  // Shared flow context for ScriptContentEditor
  const scriptCtx = { nodeId: node.id, nodes, edges, entryNodeId: null as string | null }

  function field(
    key: keyof NodeData,
    label: string,
    element: React.ReactNode,
  ) {
    return (
      <div key={key as string} className="flex flex-col gap-1">
        <label className="text-xs font-medium text-gray-400">{label}</label>
        {element}
      </div>
    )
  }

  function input(key: keyof NodeData, placeholder = '') {
    return (
      <input
        className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
        value={(data[key] as string) ?? ''}
        placeholder={placeholder}
        onChange={(e) => onUpdate(node.id, { [key]: e.target.value })}
      />
    )
  }

  function textarea(key: keyof NodeData, rows = 3, placeholder = '') {
    return (
      <textarea
        className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white placeholder-gray-500 font-mono focus:outline-none focus:border-sky-500 resize-none"
        rows={rows}
        value={(data[key] as string) ?? ''}
        placeholder={placeholder}
        onChange={(e) => onUpdate(node.id, { [key]: e.target.value })}
      />
    )
  }

  function inlineScriptFields() {
    return (
      <>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-gray-400">Script label</label>
          <input
            className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
            value={(data.scriptLabel as string) ?? ''}
            placeholder="Instructions"
            onChange={(e) => onUpdate(node.id, { scriptLabel: e.target.value })}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-gray-400">Script</label>
          <ScriptContentEditor
            key={`${node.id}-script`}
            content={(data.scriptContent as string) ?? ''}
            onUpdate={(html) => onUpdate(node.id, { scriptContent: html })}
            dark
            {...scriptCtx}
          />
        </div>
        <div className="border-t border-gray-800 -mx-4 my-1" />
      </>
    )
  }

  function typeSpecificFields() {
    switch (type) {
      case 'script':
        return (
          <ScriptContentEditor
            key={node.id}
            content={(data.content as string) ?? ''}
            onUpdate={(html) => onUpdate(node.id, { content: html })}
            dark
            {...scriptCtx}
          />
        )

      case 'input': {
        const fieldType = (data.fieldType as string) ?? 'text'
        const inputMask = (data.inputMask as string) ?? ''
        const hasMask = inputMask !== '' && inputMask !== undefined
        return (
          <>
            {inlineScriptFields()}

            {/* Field type */}
            {field(
              'fieldType',
              'Field Type',
              <select
                className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white focus:outline-none focus:border-sky-500"
                value={fieldType}
                onChange={(e) => onUpdate(node.id, { fieldType: e.target.value })}
              >
                {['text', 'select', 'checkbox'].map((t) => (
                  <option key={t} value={t}>{t}</option>
                ))}
              </select>,
            )}

            {/* Select options */}
            {fieldType === 'select' &&
              field('options', 'Options (comma-separated)', textarea('options', 2, 'Option A, Option B, Option C'))}

            {/* Text-specific: mask + min/max */}
            {fieldType === 'text' && (
              <>
                {/* Input mask */}
                {field(
                  'inputMask',
                  'Input Mask',
                  <select
                    className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white focus:outline-none focus:border-sky-500"
                    value={inputMask}
                    onChange={(e) => onUpdate(node.id, { inputMask: e.target.value, customMask: '' })}
                  >
                    {PRESET_MASKS.map((m) => (
                      <option key={m.value} value={m.value}>{m.label}</option>
                    ))}
                  </select>,
                )}

                {/* Custom mask input */}
                {inputMask === '__custom__' && (
                  <div className="flex flex-col gap-1">
                    <label className="text-xs font-medium text-gray-400">Custom mask</label>
                    <input
                      className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white font-mono placeholder-gray-500 focus:outline-none focus:border-sky-500"
                      value={(data.customMask as string) ?? ''}
                      placeholder="(000) 000-0000"
                      onChange={(e) => onUpdate(node.id, { customMask: e.target.value })}
                    />
                    <p className="text-[10px] text-gray-500 leading-snug">
                      0=digit&nbsp; 9=digit/space&nbsp; L=letter&nbsp; ?=letter/space&nbsp; A=alphanum&nbsp; &amp;=any
                    </p>
                  </div>
                )}

                {/* Min / Max characters — disabled when mask is active */}
                <div className="flex gap-2">
                  <div className="flex flex-col gap-1 flex-1">
                    <label className={`text-xs font-medium ${hasMask ? 'text-gray-600' : 'text-gray-400'}`}>
                      Min chars
                    </label>
                    <input
                      type="number"
                      min={0}
                      disabled={hasMask}
                      className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500 disabled:opacity-35 disabled:cursor-not-allowed"
                      value={(data.minChars as number) ?? ''}
                      placeholder="0"
                      onChange={(e) => onUpdate(node.id, { minChars: e.target.value ? Number(e.target.value) : undefined })}
                    />
                  </div>
                  <div className="flex flex-col gap-1 flex-1">
                    <label className={`text-xs font-medium ${hasMask ? 'text-gray-600' : 'text-gray-400'}`}>
                      Max chars
                    </label>
                    <input
                      type="number"
                      min={1}
                      disabled={hasMask}
                      className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500 disabled:opacity-35 disabled:cursor-not-allowed"
                      value={(data.maxChars as number) ?? ''}
                      placeholder="∞"
                      onChange={(e) => onUpdate(node.id, { maxChars: e.target.value ? Number(e.target.value) : undefined })}
                    />
                  </div>
                </div>
                {hasMask && (
                  <p className="text-[10px] text-gray-600 -mt-1">Min / max are set automatically by the mask.</p>
                )}
              </>
            )}

            <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
              <input
                type="checkbox"
                checked={(data.required as boolean) ?? false}
                onChange={(e) => onUpdate(node.id, { required: e.target.checked })}
              />
              Required
            </label>

            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-400">Output variable</label>
              <input
                className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
                value={(data.outputVariable as string) ?? ''}
                placeholder="call_type"
                onChange={(e) => onUpdate(node.id, { outputVariable: e.target.value })}
              />
              <p className="text-[10px] text-gray-500">
                {(data.outputVariable as string)
                  ? <>Available downstream as <span className="font-mono text-emerald-400">{'{{flow.' + (data.outputVariable as string) + '}}'}</span></>
                  : 'Saves the response as a flow variable'}
              </p>
            </div>
          </>
        )
      }

      case 'email':
        return (
          <>
            {inlineScriptFields()}
            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-400">Output variable</label>
              <input
                className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
                value={(data.outputVariable as string) ?? ''}
                placeholder="customer_email"
                onChange={(e) => onUpdate(node.id, { outputVariable: e.target.value })}
              />
              <p className="text-[10px] text-gray-500 leading-snug">
                {(data.outputVariable as string)
                  ? <>Stores an object — access as <span className="font-mono text-cyan-400">{'{{flow.' + (data.outputVariable as string) + '.isDeliverable}}'}</span></>
                  : 'Stores email + validation results as a flow object variable'}
              </p>
            </div>
            <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
              <input
                type="checkbox"
                checked={(data.required as boolean) ?? false}
                onChange={(e) => onUpdate(node.id, { required: e.target.checked })}
              />
              Required
            </label>
            <div className="flex flex-col gap-1.5">
              <p className="text-xs font-medium text-gray-400">Validation</p>
              <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
                <input
                  type="checkbox"
                  checked={(data.checkARecord as boolean) ?? false}
                  onChange={(e) => onUpdate(node.id, { checkARecord: e.target.checked })}
                />
                A / AAAA record check
              </label>
              <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
                <input
                  type="checkbox"
                  checked={(data.checkMX as boolean) ?? true}
                  onChange={(e) => onUpdate(node.id, { checkMX: e.target.checked })}
                />
                MX record check
              </label>
              <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
                <input
                  type="checkbox"
                  checked={(data.checkDisposable as boolean) ?? true}
                  onChange={(e) => onUpdate(node.id, { checkDisposable: e.target.checked })}
                />
                Disposable domain check
              </label>
            </div>
            <p className="text-[10px] text-gray-500 leading-snug">
              Format is always validated. Unchecked validations store null in the output object.
            </p>
          </>
        )

      case 'phone':
        return (
          <>
            {inlineScriptFields()}
            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-400">Output variable</label>
              <input
                className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
                value={(data.outputVariable as string) ?? ''}
                placeholder="customer_phone"
                onChange={(e) => onUpdate(node.id, { outputVariable: e.target.value })}
              />
              <p className="text-[10px] text-gray-500 leading-snug">
                {(data.outputVariable as string)
                  ? <>Stores an object — access as <span className="font-mono text-teal-400">{'{{flow.' + (data.outputVariable as string) + '.isTollFree}}'}</span></>
                  : 'Stores phone number + properties as a flow object variable'}
              </p>
            </div>
            <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
              <input
                type="checkbox"
                checked={(data.required as boolean) ?? false}
                onChange={(e) => onUpdate(node.id, { required: e.target.checked })}
              />
              Required
            </label>
            <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
              <input
                type="checkbox"
                checked={(data.allowInternational as boolean) ?? false}
                onChange={(e) => onUpdate(node.id, { allowInternational: e.target.checked })}
              />
              Allow International
            </label>
            {(data.allowInternational as boolean) && (
              <p className="text-[10px] text-gray-500 leading-snug -mt-1 ml-5">
                Mask becomes <span className="font-mono">+099 (000) 000-0000</span>. Enter country code zero-padded to 3 digits (e.g. 001 for +1, 044 for +44).
              </p>
            )}
            <label className="flex items-center gap-2 text-sm text-gray-500 cursor-not-allowed" title="Coming in a future release">
              <input
                type="checkbox"
                disabled
                checked={(data.dncCheck as boolean) ?? false}
                onChange={(e) => onUpdate(node.id, { dncCheck: e.target.checked })}
              />
              DNC Registry Check
              <span className="text-[10px] text-gray-600 ml-1">(coming soon)</span>
            </label>
            <p className="text-[10px] text-gray-600 leading-snug -mt-1">
              Object properties: value · display_value · isMobile · isTollFree · isInternal · doNotCall
            </p>
          </>
        )

      case 'execute_flow':
      case 'transition_to_flow': {
        const isExecute = type === 'execute_flow'
        const selectedId = (data.targetFlowId as string) ?? ''
        return (
          <>
            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-400">Target flow</label>
              <select
                className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white focus:outline-none focus:border-sky-500"
                value={selectedId}
                onChange={(e) => {
                  const chosen = availableFlows.find((f) => f.id === e.target.value)
                  onUpdate(node.id, {
                    targetFlowId: chosen?.id ?? '',
                    targetFlowName: chosen?.name ?? '',
                  })
                }}
              >
                <option value="">— Select a flow —</option>
                {availableFlows.map((f) => (
                  <option key={f.id} value={f.id}>
                    {f.name}{f.is_active ? '' : ' (draft)'}
                  </option>
                ))}
              </select>
              {selectedId && !availableFlows.find((f) => f.id === selectedId && f.is_active) && selectedId && (
                <p className="text-[10px] text-amber-400">
                  Selected flow is a draft — publish it before running this flow.
                </p>
              )}
            </div>
            <p className="text-[10px] text-gray-500 leading-snug">
              {isExecute
                ? 'All flow variables are shared with the sub-flow. Execution returns here when the sub-flow ends.'
                : 'All flow variables carry over. Execution never returns to this flow.'}
            </p>
          </>
        )
      }

      case 'section':
        return (
          <>
            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-400">Section name</label>
              <input
                className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
                value={(data.name as string) ?? ''}
                placeholder="Billing Information"
                onChange={(e) => onUpdate(node.id, { name: e.target.value })}
              />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-400">Output variable</label>
              <input
                className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
                value={(data.outputVariable as string) ?? ''}
                placeholder="section_billing"
                onChange={(e) => onUpdate(node.id, { outputVariable: e.target.value })}
              />
              <p className="text-[10px] text-gray-500 leading-snug">
                Set <span className="font-mono text-sky-400">
                  {'{{flow.' + ((data.outputVariable as string) || 'var') + '.locked}}'}
                </span> = <span className="font-mono text-sky-400">"true"</span> via set_variable to lock this section.
              </p>
            </div>
            <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
              <input
                type="checkbox"
                checked={(data.allowJumpFromAnywhere as boolean) ?? false}
                onChange={(e) => onUpdate(node.id, { allowJumpFromAnywhere: e.target.checked })}
              />
              Always visible in jump dropdown
            </label>
            <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
              <input
                type="checkbox"
                checked={(data.clearPreviousValues as boolean) ?? false}
                onChange={(e) => onUpdate(node.id, { clearPreviousValues: e.target.checked })}
              />
              Clear section values when jumping here
            </label>
          </>
        )

      case 'address': {
        const reqFields    = (data.requiredFields as string[]) ?? []
        const fieldScripts = (data.fieldScripts  as Record<string, string>) ?? {}

        const ADDR_TABS = [
          { key: 'main',          label: 'Main' },
          { key: 'firstName',     label: 'First Name' },
          { key: 'lastName',      label: 'Last Name' },
          { key: 'middleInitial', label: 'MI' },
          { key: 'company',       label: 'Company' },
          { key: 'address1',      label: 'Addr 1' },
          { key: 'address2',      label: 'Addr 2' },
          { key: 'zip',           label: 'ZIP' },
          { key: 'city',          label: 'City' },
          { key: 'state',         label: 'State' },
          { key: 'country',       label: 'Country' },
        ]

        const REQ_OPTS = [
          { key: 'firstName',     label: 'First Name' },
          { key: 'lastName',      label: 'Last Name' },
          { key: 'middleInitial', label: 'Middle Initial' },
          { key: 'company',       label: 'Company' },
          { key: 'address1',      label: 'Address Line 1' },
          { key: 'address2',      label: 'Address Line 2' },
          { key: 'zip',           label: 'ZIP' },
          { key: 'zip4',          label: 'ZIP+4' },
          { key: 'city',          label: 'City' },
          { key: 'state',         label: 'State' },
          { key: 'country',       label: 'Country' },
        ]

        function toggleReqField(key: string, checked: boolean) {
          const next = checked
            ? [...reqFields, key]
            : reqFields.filter((f) => f !== key)
          onUpdate(node.id, { requiredFields: next })
        }

        return (
          <>
            {/* Script tabs */}
            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-400">Scripts</label>

              {/* Tab strip */}
              <div className="flex flex-wrap gap-1">
                {ADDR_TABS.map((t) => (
                  <button
                    key={t.key}
                    type="button"
                    onClick={() => setAddrScriptTab(t.key)}
                    className={`text-[10px] px-2 py-0.5 rounded transition-colors ${
                      addrScriptTab === t.key
                        ? 'bg-orange-700/60 text-orange-200 border border-orange-600/50'
                        : 'text-gray-400 hover:text-gray-200 border border-gray-700 hover:border-gray-500'
                    }`}
                  >
                    {t.label}
                  </button>
                ))}
              </div>

              {/* Main tab: label + content */}
              {addrScriptTab === 'main' && (
                <div className="flex flex-col gap-2 mt-1">
                  <input
                    className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
                    value={(data.scriptLabel as string) ?? ''}
                    placeholder="Script label (optional)"
                    onChange={(e) => onUpdate(node.id, { scriptLabel: e.target.value })}
                  />
                  <ScriptContentEditor
                    key={`${node.id}-addr-main`}
                    content={(data.scriptContent as string) ?? ''}
                    onUpdate={(html) => onUpdate(node.id, { scriptContent: html })}
                    dark
                    {...scriptCtx}
                  />
                </div>
              )}

              {/* Per-field tab: just script content */}
              {addrScriptTab !== 'main' && (
                <div className="mt-1">
                  <ScriptContentEditor
                    key={`${node.id}-addr-${addrScriptTab}`}
                    content={fieldScripts[addrScriptTab] ?? ''}
                    onUpdate={(html) => onUpdate(node.id, {
                      fieldScripts: { ...fieldScripts, [addrScriptTab]: html },
                    })}
                    dark
                    {...scriptCtx}
                  />
                  <p className="text-[10px] text-gray-500 mt-1 leading-snug">
                    Shown when the agent focuses this field. Leave empty to keep the main script visible.
                  </p>
                </div>
              )}
            </div>

            <div className="border-t border-gray-800 -mx-4 my-1" />

            {/* Output variable */}
            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-400">Output variable</label>
              <input
                className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
                value={(data.outputVariable as string) ?? ''}
                placeholder="billing_address"
                onChange={(e) => onUpdate(node.id, { outputVariable: e.target.value })}
              />
              <p className="text-[10px] text-gray-500 leading-snug">
                {(data.outputVariable as string)
                  ? <>Object — access as <span className="font-mono text-orange-400">{'{{flow.' + (data.outputVariable as string) + '.city}}'}</span></>
                  : 'Stores address object as a flow variable'}
              </p>
            </div>

            {/* Field visibility */}
            <div className="flex flex-col gap-1.5">
              <p className="text-xs font-medium text-gray-400">Optional fields</p>
              <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
                <input
                  type="checkbox"
                  checked={(data.showMiddleInitial as boolean) ?? false}
                  onChange={(e) => onUpdate(node.id, { showMiddleInitial: e.target.checked })}
                />
                Show Middle Initial
              </label>
              <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
                <input
                  type="checkbox"
                  checked={(data.showCompany as boolean) ?? false}
                  onChange={(e) => onUpdate(node.id, { showCompany: e.target.checked })}
                />
                Show Company
              </label>
              <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
                <input
                  type="checkbox"
                  checked={(data.allowInternational as boolean) ?? false}
                  onChange={(e) => onUpdate(node.id, { allowInternational: e.target.checked })}
                />
                Allow International
              </label>
            </div>

            {/* Address Validation */}
            <div className="flex flex-col gap-1.5">
              <p className="text-xs font-medium text-gray-400">Validation</p>
              <label className="flex items-center gap-2 text-sm text-gray-300 cursor-pointer">
                <input
                  type="checkbox"
                  checked={(data.useValidation as boolean) ?? true}
                  onChange={(e) => onUpdate(node.id, { useValidation: e.target.checked })}
                />
                Validate Address via API
              </label>
              <p className="text-[10px] text-gray-500 leading-snug">
                Calls the configured Address Validation API when the agent submits. Requires an active API definition of type "Address Validation".
              </p>
            </div>

            {/* Required fields */}
            <div className="flex flex-col gap-1.5">
              <p className="text-xs font-medium text-gray-400">Required fields</p>
              <div className="grid grid-cols-2 gap-x-3 gap-y-1">
                {REQ_OPTS.map((opt) => (
                  <label key={opt.key} className="flex items-center gap-1.5 text-xs text-gray-300 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={reqFields.includes(opt.key)}
                      onChange={(e) => toggleReqField(opt.key, e.target.checked)}
                    />
                    {opt.label}
                  </label>
                ))}
              </div>
            </div>

            <p className="text-[10px] text-gray-600 leading-snug">
              Address 1 prefix: PO Box, PMB, RR, SR, Hwy · Address 2 prefix: Apt, Ste, Floor, Level, Space, Lot, Unit
            </p>
          </>
        )
      }

      case 'branch':
        return (
          <>
            {field('condition', 'Condition', input('condition', '{{input.node_id}} == value'))}
            <p className="text-[10px] text-gray-500">Operators: == != &gt; &lt; &gt;= &lt;= contains</p>
            <div className="flex justify-between text-xs mt-1">
              <span className="text-green-400 font-medium">→ true transition</span>
              <span className="text-red-400 font-medium">→ false transition</span>
            </div>
          </>
        )

      case 'set_variable': {
        const assignments = (data.assignments as { variable: string; value: string }[]) ?? [{ variable: '', value: '' }]
        return (
          <div className="flex flex-col gap-2">
            {/* Header row */}
            <div className="flex items-center justify-between">
              <label className="text-xs font-medium text-gray-400">Assignments</label>
              <button
                type="button"
                className={`flex items-center gap-1 text-[11px] px-2 py-0.5 rounded transition-colors ${
                  varPanelOpen
                    ? 'bg-violet-900/60 text-violet-300'
                    : 'text-violet-400 hover:text-violet-300 hover:bg-violet-900/30'
                }`}
                onClick={() => setVarPanelOpen((v) => !v)}
              >
                <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 20l4-16m2 16l4-16M6 9h14M4 15h14" />
                </svg>
                Variables
                <svg
                  className={`w-3 h-3 transition-transform duration-150 ${varPanelOpen ? 'rotate-180' : ''}`}
                  fill="none" stroke="currentColor" viewBox="0 0 24 24"
                >
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                </svg>
              </button>
            </div>

            {/* Assignment rows */}
            {assignments.map((a, i) => (
              <div key={i} className="flex gap-1 items-center">
                <input
                  className="flex-1 bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
                  placeholder="{{flow.var}}"
                  value={a.variable}
                  onFocus={(e) => { lastFocused.current = { index: i, side: 'variable', el: e.target } }}
                  onChange={(e) => {
                    const next = [...assignments]
                    next[i] = { ...a, variable: e.target.value }
                    onUpdate(node.id, { assignments: next })
                  }}
                />
                <span className="text-gray-500 text-xs">=</span>
                <input
                  className="flex-1 bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-white placeholder-gray-500 focus:outline-none focus:border-sky-500"
                  placeholder="value or {{token}}"
                  value={a.value}
                  onFocus={(e) => { lastFocused.current = { index: i, side: 'value', el: e.target } }}
                  onChange={(e) => {
                    const next = [...assignments]
                    next[i] = { ...a, value: e.target.value }
                    onUpdate(node.id, { assignments: next })
                  }}
                />
                <button
                  className="text-gray-500 hover:text-red-400 text-xs px-1"
                  onClick={() => onUpdate(node.id, { assignments: assignments.filter((_, j) => j !== i) })}
                >×</button>
              </div>
            ))}

            <button
              className="text-xs text-sky-400 hover:text-sky-300 self-start"
              onClick={() => onUpdate(node.id, { assignments: [...assignments, { variable: '', value: '' }] })}
            >+ Add assignment</button>

            {/* Slide-out variable panel */}
            {varPanelOpen && (
              <div className="mt-1 border border-gray-700 rounded-lg overflow-hidden" style={{ maxHeight: 320 }}>
                <VariablePanel
                  onInsert={handleVarInsert}
                  flowVars={flowVars}
                  dark
                />
              </div>
            )}

            {varPanelOpen && (
              <p className="text-[10px] text-gray-600 -mt-1">
                Click a variable to insert at the focused field, or copy to clipboard.
              </p>
            )}
          </div>
        )
      }

      case 'api_call':
        return (
          <>
            {field(
              'method',
              'Method',
              <select
                className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-sm text-white focus:outline-none focus:border-sky-500"
                value={(data.method as string) ?? 'GET'}
                onChange={(e) => onUpdate(node.id, { method: e.target.value })}
              >
                {['GET', 'POST', 'PUT', 'PATCH', 'DELETE'].map((m) => (
                  <option key={m} value={m}>{m}</option>
                ))}
              </select>,
            )}
            {field('url', 'URL', input('url', 'https://api.example.com/{{flow.id}}'))}
            {field('headers', 'Headers (JSON)', textarea('headers', 2, '{"Authorization": "Bearer {{flow.token}}"}'))}
            {field('body', 'Body (JSON)', textarea('body', 3, '{"key": "{{input.node_id}}"}'))}
            <p className="text-[10px] text-gray-500">Use {'{{namespace.field}}'} in all fields</p>
          </>
        )

      case 'end':
        return field('status', 'Status', input('status', 'complete'))
    }
  }

  return (
    <div className="w-72 bg-gray-900 border-l border-gray-800 flex flex-col overflow-y-auto">
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-800">
        <span className="text-sm font-semibold text-white">Node Properties</span>
        <button
          className="text-gray-500 hover:text-gray-300 text-lg leading-none"
          onClick={onClose}
        >×</button>
      </div>

      {/* Fields */}
      <div className="flex flex-col gap-4 px-4 py-4 flex-1">
        {/* Node ID */}
        <div>
          <p className="text-[10px] text-gray-600 font-mono">ID: {node.id}</p>
        </div>

        {/* Label */}
        {field('label', 'Label', input('label', 'Node label'))}

        {/* Type-specific fields */}
        {typeSpecificFields()}
      </div>

      {/* Footer actions */}
      <div className="border-t border-gray-800 px-4 py-3 flex flex-col gap-2">
        {!isEntry && (
          <button
            className="w-full text-sm text-sky-400 hover:text-sky-300 border border-sky-800 hover:border-sky-600 rounded py-1.5 transition-colors"
            onClick={() => onSetEntry(node.id)}
          >
            Set as Entry Node
          </button>
        )}
        {isEntry && (
          <p className="text-xs text-center text-emerald-400 font-medium">✓ Entry Node</p>
        )}
        <button
          className="w-full text-sm text-red-400 hover:text-red-300 border border-red-900 hover:border-red-700 rounded py-1.5 transition-colors"
          onClick={() => onDelete(node.id)}
        >
          Delete Node
        </button>
      </div>
    </div>
  )
}
