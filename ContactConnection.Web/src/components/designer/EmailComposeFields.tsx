import { useRef } from 'react'
import RichTextEditor, { type RichTextEditorHandle } from './RichTextEditor'

/**
 * To / Cc / Bcc / From name / Reply-to / Subject / rich-text Body — the email composer shared by
 * the telephony voicemail node's delivery block (prefix "deliveryEmail") and the send-email nodes
 * of both designers (prefix "email"). Fields are stored on the node as `${prefix}To`,
 * `${prefix}Cc`, …, `${prefix}Subject`, `${prefix}BodyHtml`; every one accepts {{variable}} tags,
 * resolved when the email is sent.
 */
export default function EmailComposeFields({
  data, onChange, prefix, vars, inputCls, labelCls, fromNamePlaceholder = 'Contact Center',
}: {
  data: Record<string, unknown>
  onChange: (patch: Record<string, unknown>) => void
  prefix: string
  /** Variable chips offered above the body. */
  vars: string[]
  inputCls: string
  labelCls: string
  fromNamePlaceholder?: string
}) {
  const bodyRef = useRef<RichTextEditorHandle>(null)
  const k = (name: string) => `${prefix}${name}`
  const str = (name: string) => (data[k(name)] as string) ?? ''
  const set = (name: string, value: string) => onChange({ [k(name)]: value })

  return (
    <div className="flex flex-col gap-2">
      <div>
        <label className={labelCls}>To</label>
        <input className={`${inputCls} font-mono text-xs`} placeholder="ops@client.com, {{flow.queue_email}}"
          value={str('To')} onChange={(e) => set('To', e.target.value)} />
      </div>
      <div className="grid grid-cols-2 gap-2">
        <div>
          <label className={labelCls}>Cc</label>
          <input className={`${inputCls} font-mono text-xs`} value={str('Cc')} onChange={(e) => set('Cc', e.target.value)} />
        </div>
        <div>
          <label className={labelCls}>Bcc</label>
          <input className={`${inputCls} font-mono text-xs`} value={str('Bcc')} onChange={(e) => set('Bcc', e.target.value)} />
        </div>
      </div>
      <div className="grid grid-cols-2 gap-2">
        <div>
          <label className={labelCls}>From name</label>
          <input className={inputCls} placeholder={fromNamePlaceholder}
            value={str('FromName')} onChange={(e) => set('FromName', e.target.value)} />
        </div>
        <div>
          <label className={labelCls}>Reply-to</label>
          <input className={`${inputCls} font-mono text-xs`} placeholder="team@client.com"
            value={str('ReplyTo')} onChange={(e) => set('ReplyTo', e.target.value)} />
        </div>
      </div>
      <p className="text-[10px] text-gray-500 leading-snug -mt-1">
        The sending address stays the platform sender (Resend needs a verified domain); the
        From name and Reply-to are yours to set.
      </p>
      <div>
        <label className={labelCls}>Subject</label>
        <input className={`${inputCls} text-xs`} value={str('Subject')} onChange={(e) => set('Subject', e.target.value)} />
      </div>
      <div>
        <label className={labelCls}>Body</label>
        <div className="flex flex-wrap gap-1 mb-1">
          {vars.map((v) => (
            <button key={v} type="button"
              onClick={() => bodyRef.current?.insert(v)}
              className="text-[10px] font-mono bg-gray-800 border border-gray-600 rounded px-1.5 py-0.5 text-purple-300 hover:border-purple-500">
              {v}
            </button>
          ))}
        </div>
        <RichTextEditor
          ref={bodyRef}
          dark
          value={str('BodyHtml')}
          onChange={(html) => set('BodyHtml', html)}
        />
        <p className="text-[10px] text-gray-500 mt-1 leading-snug">
          Same <span className="font-mono">{'{{variable}}'}</span> tags as the script editor —
          <span className="font-mono"> {'{{caller.*}}'}</span>, <span className="font-mono">{'{{call_record.*}}'}</span>,
          <span className="font-mono"> {'{{flow.*}}'}</span> — resolved when the email is sent.
        </p>
      </div>
    </div>
  )
}
