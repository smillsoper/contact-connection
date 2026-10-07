import { useEditor, EditorContent } from '@tiptap/react'
import { Extension } from '@tiptap/core'
import StarterKit from '@tiptap/starter-kit'
import { TextStyle } from '@tiptap/extension-text-style'
import { Color } from '@tiptap/extension-color'
import Highlight from '@tiptap/extension-highlight'
import Underline from '@tiptap/extension-underline'
import FontFamily from '@tiptap/extension-font-family'
import Image from '@tiptap/extension-image'
import { useState, useRef, forwardRef, useImperativeHandle } from 'react'

export interface RichTextEditorHandle {
  insert: (text: string) => void
}

// ── Custom FontSize extension ──────────────────────────────────────────────

export const FontSize = Extension.create({
  name: 'fontSize',
  addGlobalAttributes() {
    return [{
      types: ['textStyle'],
      attributes: {
        fontSize: {
          default: null,
          parseHTML: (el: HTMLElement) => el.style.fontSize || null,
          renderHTML: (attrs: Record<string, unknown>) => {
            if (!attrs.fontSize) return {}
            return { style: `font-size: ${attrs.fontSize}` }
          },
        },
      },
    }]
  },
  addCommands() {
    return {
      setFontSize: (size: string) => ({ chain }: any) =>
        chain().setMark('textStyle', { fontSize: size }).run(),
      unsetFontSize: () => ({ chain }: any) =>
        chain().setMark('textStyle', { fontSize: null }).run(),
    }
  },
})

// ── Constants ──────────────────────────────────────────────────────────────

export const FONT_FAMILIES = [
  { label: 'Default',          value: '' },
  { label: 'Arial',            value: 'Arial, sans-serif' },
  { label: 'Georgia',          value: 'Georgia, serif' },
  { label: 'Verdana',          value: 'Verdana, sans-serif' },
  { label: 'Times New Roman',  value: "'Times New Roman', serif" },
  { label: 'Courier New',      value: "'Courier New', monospace" },
]

export const FONT_SIZES = ['10px', '11px', '12px', '13px', '14px', '16px', '18px', '20px', '24px', '28px']

export const TEXT_COLORS = [
  { label: 'Default',  value: '' },
  { label: 'Red',      value: '#dc2626' },
  { label: 'Blue',     value: '#2563eb' },
  { label: 'Green',    value: '#16a34a' },
  { label: 'Orange',   value: '#ea580c' },
  { label: 'Purple',   value: '#7c3aed' },
  { label: 'Gray',     value: '#6b7280' },
]

export const HIGHLIGHT_COLORS = [
  { label: 'None',    value: '' },
  { label: 'Yellow',  value: '#fef08a' },
  { label: 'Green',   value: '#bbf7d0' },
  { label: 'Blue',    value: '#bfdbfe' },
  { label: 'Red',     value: '#fecaca' },
  { label: 'Orange',  value: '#fed7aa' },
]

// ── Toolbar button ─────────────────────────────────────────────────────────

export function Btn({
  active, onClick, title, children, dark,
}: {
  active?: boolean
  onClick: () => void
  title: string
  children: React.ReactNode
  dark?: boolean
}) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={`
        w-6 h-6 flex items-center justify-center rounded text-[11px] font-medium
        transition-colors select-none shrink-0
        ${dark
          ? active ? 'bg-gray-600 text-white' : 'text-gray-300 hover:bg-gray-700 hover:text-white'
          : active ? 'bg-gray-200 text-gray-900' : 'text-gray-600 hover:bg-gray-100 hover:text-gray-900'
        }
      `}
    >
      {children}
    </button>
  )
}

export function Divider({ dark }: { dark?: boolean }) {
  return <div className={`w-px h-4 mx-0.5 shrink-0 ${dark ? 'bg-gray-600' : 'bg-gray-300'}`} />
}

// ── Color picker popover ───────────────────────────────────────────────────

export function ColorPicker({
  colors, onSelect, current, label, dark,
}: {
  colors: { label: string; value: string }[]
  onSelect: (value: string) => void
  current: string
  label: string
  dark?: boolean
}) {
  const [open, setOpen] = useState(false)

  return (
    <div className="relative shrink-0">
      <button
        type="button"
        title={label}
        onClick={() => setOpen((v) => !v)}
        className={`w-6 h-6 flex flex-col items-center justify-center rounded transition-colors ${dark ? 'hover:bg-gray-700' : 'hover:bg-gray-100'}`}
      >
        <span className={`text-[11px] font-bold leading-none ${dark ? 'text-gray-300' : 'text-gray-700'}`}>{label[0]}</span>
        <span className="w-4 h-0.5 mt-0.5 rounded" style={{ backgroundColor: current || '#6b7280' }} />
      </button>

      {open && (
        <>
          <div className="fixed inset-0 z-10" onClick={() => setOpen(false)} />
          <div className={`absolute left-0 top-7 z-20 rounded-lg shadow-lg p-2 flex flex-col gap-1.5 ${dark ? 'bg-gray-800 border border-gray-700' : 'bg-white border border-gray-200'}`}>
            <p className={`text-[10px] font-medium mb-0.5 ${dark ? 'text-gray-400' : 'text-gray-500'}`}>{label}</p>
            <div className="flex gap-1.5 flex-wrap w-40">
              {colors.map((c) => (
                <button
                  key={c.value || 'none'}
                  type="button"
                  title={c.label}
                  onClick={() => { onSelect(c.value); setOpen(false) }}
                  className="w-6 h-6 rounded border-2 transition-all hover:scale-110"
                  style={{
                    backgroundColor: c.value || 'transparent',
                    borderColor: current === c.value ? '#6366f1' : (dark ? '#4b5563' : '#d1d5db'),
                    backgroundImage: !c.value
                      ? 'repeating-linear-gradient(45deg,#d1d5db,#d1d5db 2px,transparent 2px,transparent 8px)'
                      : undefined,
                  }}
                />
              ))}
            </div>
          </div>
        </>
      )}
    </div>
  )
}

// ── Main editor ────────────────────────────────────────────────────────────

interface RichTextEditorProps {
  value: string
  onChange: (html: string) => void
  onExpand?: () => void
  dark?: boolean
}

const RichTextEditor = forwardRef<RichTextEditorHandle, RichTextEditorProps>(
  function RichTextEditor({ value, onChange, onExpand, dark }, ref) {
    const fileInputRef = useRef<HTMLInputElement>(null)

    const editor = useEditor({
      extensions: [
        StarterKit,
        TextStyle,
        Color,
        Highlight.configure({ multicolor: true }),
        Underline,
        FontFamily,
        FontSize,
        Image.configure({ inline: false, allowBase64: true }),
      ],
      content: value,
      onUpdate: ({ editor }) => onChange(editor.getHTML()),
      editorProps: {
        handlePaste(view, event) {
          const items = event.clipboardData?.items
          if (!items) return false
          for (const item of Array.from(items)) {
            if (!item.type.startsWith('image/')) continue
            const file = item.getAsFile()
            if (!file) continue
            const reader = new FileReader()
            reader.onload = () => {
              view.dispatch(
                view.state.tr.replaceSelectionWith(
                  view.state.schema.nodes['image'].create({ src: reader.result as string }),
                ),
              )
            }
            reader.readAsDataURL(file)
            event.preventDefault()
            return true
          }
          return false
        },
      },
    })

    useImperativeHandle(ref, () => ({
      insert: (text: string) => {
        editor?.chain().focus().insertContent(text).run()
      },
    }), [editor])

    if (!editor) return null

    const activeTextColor   = editor.getAttributes('textStyle').color      ?? ''
    const activeHighlight   = editor.getAttributes('highlight').color      ?? ''
    const activeFontFamily  = editor.getAttributes('textStyle').fontFamily  ?? ''
    const activeFontSize    = editor.getAttributes('textStyle').fontSize    ?? ''

    function applyFontFamily(v: string) {
      v
        ? editor.chain().focus().setFontFamily(v).run()
        : editor.chain().focus().unsetFontFamily().run()
    }

    function applyFontSize(v: string) {
      v
        ? (editor.chain().focus() as any).setFontSize(v).run()
        : (editor.chain().focus() as any).unsetFontSize().run()
    }

    function handleImageFile(file: File) {
      const reader = new FileReader()
      reader.onload = () => {
        editor.chain().focus().setImage({ src: reader.result as string }).run()
      }
      reader.readAsDataURL(file)
    }

    const selectCls = dark
      ? 'h-6 text-[11px] border rounded px-1 focus:outline-none shrink-0 bg-gray-700 border-gray-600 text-gray-200'
      : 'h-6 text-[11px] border border-gray-300 rounded px-1 bg-white text-gray-700 focus:outline-none shrink-0'

    return (
      <div className={`border rounded overflow-hidden transition-colors ${dark ? 'border-gray-700 focus-within:border-sky-500' : 'border-gray-300 focus-within:border-blue-400'}`}>
        {/* Toolbar */}
        <div className={`flex items-center gap-0.5 px-1.5 py-1 border-b flex-wrap ${dark ? 'bg-gray-800 border-gray-700' : 'bg-gray-50 border-gray-200'}`}>

          {/* Font family */}
          <select
            value={activeFontFamily}
            onChange={(e) => applyFontFamily(e.target.value)}
            className={selectCls}
            style={{ maxWidth: 92 }}
          >
            {FONT_FAMILIES.map((f) => (
              <option key={f.label} value={f.value}>{f.label}</option>
            ))}
          </select>

          {/* Font size */}
          <select
            value={activeFontSize}
            onChange={(e) => applyFontSize(e.target.value)}
            className={selectCls}
            style={{ maxWidth: 58 }}
          >
            <option value="">Size</option>
            {FONT_SIZES.map((s) => (
              <option key={s} value={s}>{s.replace('px', '')}</option>
            ))}
          </select>

          <Divider dark={dark} />

          {/* Bold / Italic / Underline */}
          <Btn dark={dark} active={editor.isActive('bold')}      onClick={() => editor.chain().focus().toggleBold().run()}      title="Bold">
            <span className="font-bold">B</span>
          </Btn>
          <Btn dark={dark} active={editor.isActive('italic')}    onClick={() => editor.chain().focus().toggleItalic().run()}    title="Italic">
            <span className="italic">I</span>
          </Btn>
          <Btn dark={dark} active={editor.isActive('underline')} onClick={() => editor.chain().focus().toggleUnderline().run()} title="Underline">
            <span className="underline">U</span>
          </Btn>

          <Divider dark={dark} />

          {/* Color pickers */}
          <ColorPicker
            dark={dark}
            label="Text color"
            colors={TEXT_COLORS}
            current={activeTextColor}
            onSelect={(c) => c
              ? editor.chain().focus().setColor(c).run()
              : editor.chain().focus().unsetColor().run()}
          />
          <ColorPicker
            dark={dark}
            label="Highlight"
            colors={HIGHLIGHT_COLORS}
            current={activeHighlight}
            onSelect={(c) => c
              ? editor.chain().focus().setHighlight({ color: c }).run()
              : editor.chain().focus().unsetHighlight().run()}
          />

          <Divider dark={dark} />

          {/* Lists */}
          <Btn dark={dark} active={editor.isActive('bulletList')}  onClick={() => editor.chain().focus().toggleBulletList().run()}  title="Bullet list">•</Btn>
          <Btn dark={dark} active={editor.isActive('orderedList')} onClick={() => editor.chain().focus().toggleOrderedList().run()} title="Numbered list">1.</Btn>

          <Divider dark={dark} />

          {/* Insert image */}
          <Btn dark={dark} onClick={() => fileInputRef.current?.click()} title="Insert image">
            <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
            </svg>
          </Btn>

          <Divider dark={dark} />

          {/* Clear formatting */}
          <Btn dark={dark} onClick={() => editor.chain().focus().clearNodes().unsetAllMarks().run()} title="Clear formatting">✕</Btn>

          {/* Expand button — right-aligned */}
          {onExpand && (
            <>
              <div className="flex-1" />
              <Btn dark={dark} onClick={onExpand} title="Expand editor">
                <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                    d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4" />
                </svg>
              </Btn>
            </>
          )}
        </div>

        {/* Editor surface */}
        <EditorContent
          editor={editor}
          className={`script-editor${dark ? ' script-editor-dark' : ''} px-3 py-2 text-sm min-h-28 focus:outline-none${dark ? '' : ' text-gray-800'}`}
        />

        {/* Helper */}
        <div className={`px-2 pb-1.5 text-[10px] ${dark ? 'text-gray-500 bg-gray-800' : 'text-gray-400'}`}>
          Use <span className="font-mono">{'{{namespace.field}}'}</span> for dynamic values — formatting applies to surrounding text. Images can be pasted directly.
        </div>

        {/* Hidden file picker for image insert */}
        <input
          ref={fileInputRef}
          type="file"
          accept="image/*"
          className="hidden"
          onChange={(e) => {
            const file = e.target.files?.[0]
            if (file) handleImageFile(file)
            e.target.value = ''
          }}
        />
      </div>
    )
  }
)

export default RichTextEditor
