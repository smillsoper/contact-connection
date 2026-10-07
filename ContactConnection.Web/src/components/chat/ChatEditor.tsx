import { forwardRef, useEffect, useImperativeHandle, useMemo, useRef, useState } from 'react'
import { useEditor, EditorContent, type Editor } from '@tiptap/react'
import { Node, mergeAttributes } from '@tiptap/core'
import StarterKit from '@tiptap/starter-kit'
import { TextStyle } from '@tiptap/extension-text-style'
import { Color } from '@tiptap/extension-color'
import Highlight from '@tiptap/extension-highlight'
import Underline from '@tiptap/extension-underline'
import FontFamily from '@tiptap/extension-font-family'
import Image from '@tiptap/extension-image'
import {
  Btn, ColorPicker, Divider, FontSize, FONT_FAMILIES, FONT_SIZES, TEXT_COLORS, HIGHLIGHT_COLORS,
} from '../designer/RichTextEditor'
import { stateStyle, type ChatUser } from '../../api/chat'
import { loadChatImage, uploadChatImage } from '../../lib/chatImages'
import { AddImageIcon, FormattingIcon } from './ChatIcons'

/**
 * The chat composer (S183): the script editor's formatting (font, size, bold / italic / underline / strike, colour,
 * highlight, lists) without the dynamic-value tags, @mentions as chips, and images pasted or picked — uploaded first and
 * referenced by id. Enter sends, Shift+Enter starts a new line. The server sanitizes whatever this produces.
 */

/** @Name chip — saved as <span data-mention="id">@Name</span>. */
const Mention = Node.create({
  name: 'mention',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: false,
  addAttributes() {
    return {
      id: { default: null, parseHTML: (el: HTMLElement) => el.getAttribute('data-mention'), renderHTML: (a: { id: string | null }) => ({ 'data-mention': a.id }) },
      label: { default: '', parseHTML: (el: HTMLElement) => (el.textContent ?? '').replace(/^@/, ''), renderHTML: () => ({}) },
    }
  },
  parseHTML() { return [{ tag: 'span[data-mention]' }] },
  renderHTML({ node, HTMLAttributes }) { return ['span', mergeAttributes(HTMLAttributes), `@${node.attrs.label}`] },
  renderText({ node }) { return `@${node.attrs.label}` },
})

/** Images carry the uploaded file's id; src is only the local display copy (the server drops it). */
const ChatImage = Image.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      chatFile: {
        default: null,
        parseHTML: (el: HTMLElement) => el.getAttribute('data-chat-file'),
        renderHTML: (a: { chatFile: string | null }) => (a.chatFile ? { 'data-chat-file': a.chatFile } : {}),
      },
    }
  },
}).configure({ inline: false, allowBase64: false })

export interface ChatEditorHandle {
  clear: () => void
  focus: () => void
}

interface Props {
  users: Record<string, ChatUser>
  meId?: string
  initialHtml?: string
  placeholder: string
  onSubmit: (html: string) => Promise<void> | void
  onTyping?: () => void
  onCancel?: () => void
  submitLabel?: string
}

const ChatEditor = forwardRef<ChatEditorHandle, Props>(function ChatEditor(
  { users, meId, initialHtml, placeholder, onSubmit, onTyping, onCancel, submitLabel = 'Send' }, ref,
) {
  const fileRef = useRef<HTMLInputElement>(null)
  const [mention, setMention] = useState<{ query: string; from: number } | null>(null)
  const [highlight, setHighlight] = useState(0)
  const [uploading, setUploading] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [toolbar, setToolbar] = useState(!!initialHtml)
  const [empty, setEmpty] = useState(!initialHtml)

  // Latest values for the editor's (stable) key handler.
  const live = useRef({ mention, highlight, list: [] as ChatUser[], submit: () => {}, pick: (_u: ChatUser) => {} })

  const list = useMemo(() => mention === null ? [] : Object.values(users)
    .filter((u) => u.id !== meId && u.name.toLowerCase().includes(mention.query.toLowerCase())).slice(0, 6),
  [mention, users, meId])

  async function addImages(files: File[], editor: Editor) {
    for (const file of files.slice(0, 10)) {
      setUploading((n) => n + 1); setError(null)
      try {
        const { id, url } = await uploadChatImage(file)
        editor.chain().focus().insertContent({ type: 'image', attrs: { src: url, chatFile: id } }).run()
      } catch (e) { setError(e instanceof Error ? e.message : 'Image upload failed.') }
      finally { setUploading((n) => n - 1) }
    }
  }

  const editor = useEditor({
    extensions: [
      StarterKit.configure({ heading: false, horizontalRule: false, codeBlock: false }),
      TextStyle, Color, Highlight.configure({ multicolor: true }), Underline, FontFamily, FontSize, ChatImage, Mention,
    ],
    content: initialHtml ?? '',
    onUpdate: ({ editor }) => { detectMention(editor); setEmpty(editor.isEmpty); onTyping?.() },
    onCreate: ({ editor }) => setEmpty(editor.isEmpty),
    onSelectionUpdate: ({ editor }) => detectMention(editor),
    editorProps: {
      attributes: { class: 'chat-editor-surface' },
      handlePaste(_view, event) {
        const files = Array.from(event.clipboardData?.files ?? []).filter((f) => f.type.startsWith('image/'))
        if (files.length === 0 || !editorRef.current) return false
        event.preventDefault()
        void addImages(files, editorRef.current)
        return true
      },
      handleDrop(_view, event) {
        const files = Array.from((event as DragEvent).dataTransfer?.files ?? []).filter((f) => f.type.startsWith('image/'))
        if (files.length === 0 || !editorRef.current) return false
        event.preventDefault()
        void addImages(files, editorRef.current)
        return true
      },
      handleKeyDown(_view, event) {
        const l = live.current
        if (l.mention && l.list.length > 0) {
          if (event.key === 'ArrowDown') { setHighlight((h) => (h + 1) % l.list.length); return true }
          if (event.key === 'ArrowUp') { setHighlight((h) => (h - 1 + l.list.length) % l.list.length); return true }
          if (event.key === 'Enter' || event.key === 'Tab') { l.pick(l.list[l.highlight] ?? l.list[0]); return true }
          if (event.key === 'Escape') { setMention(null); return true }
        }
        if (event.key === 'Escape' && onCancel) { onCancel(); return true }
        if (event.key === 'Enter' && !event.shiftKey) { l.submit(); return true }
        return false
      },
    },
  })
  const editorRef = useRef<Editor | null>(null)
  editorRef.current = editor

  function detectMention(ed: Editor) {
    const { from, empty } = ed.state.selection
    if (!empty) { setMention(null); return }
    const before = ed.state.doc.textBetween(Math.max(0, from - 40), from, '\n', '￼')
    const m = /(?:^|\s)@([^\s@￼]{0,30})$/.exec(before)
    setMention(m ? { query: m[1], from: from - m[1].length - 1 } : null)
    setHighlight(0)
  }

  function pick(u: ChatUser) {
    if (!editor || !mention) return
    const to = editor.state.selection.from
    editor.chain().focus().deleteRange({ from: mention.from, to })
      .insertContent([{ type: 'mention', attrs: { id: u.id, label: u.name } }, { type: 'text', text: ' ' }]).run()
    setMention(null)
  }

  function hasContent(ed: Editor) {
    let images = false
    ed.state.doc.descendants((n) => { if (n.type.name === 'image' || n.type.name === 'mention') images = true })
    return images || ed.getText().trim().length > 0
  }

  async function submit() {
    if (!editor || busy || uploading > 0 || !hasContent(editor)) return
    setBusy(true); setError(null)
    try {
      await onSubmit(editor.getHTML())
    } catch (e) { setError(e instanceof Error ? e.message : 'Send failed.') }
    finally { setBusy(false) }
  }

  live.current = { mention, highlight, list, submit: () => void submit(), pick }

  useImperativeHandle(ref, () => ({
    clear: () => editor?.commands.clearContent(true),
    focus: () => editor?.commands.focus('end'),
  }), [editor])

  // Editing a message: show its images (they arrive as ids without a src).
  useEffect(() => {
    if (!editor || !initialHtml) return
    editor.state.doc.descendants((node, pos) => {
      if (node.type.name !== 'image' || !node.attrs.chatFile || node.attrs.src) return
      loadChatImage(node.attrs.chatFile).then((url) => {
        if (editor.isDestroyed) return
        const current = editor.state.doc.nodeAt(pos)
        if (current?.type.name === 'image') editor.view.dispatch(editor.state.tr.setNodeMarkup(pos, undefined, { ...current.attrs, src: url }))
      }).catch(() => {})
    })
  }, [editor, initialHtml])

  if (!editor) return null
  const color = editor.getAttributes('textStyle').color ?? ''
  const mark = editor.getAttributes('highlight').color ?? ''
  const family = editor.getAttributes('textStyle').fontFamily ?? ''
  const size = editor.getAttributes('textStyle').fontSize ?? ''
  const selectCls = 'h-6 text-[11px] border rounded px-1 focus:outline-none shrink-0 bg-gray-700 border-gray-600 text-gray-200'

  return (
    <div className="relative">
      {list.length > 0 && (
        <div className="absolute bottom-full left-0 right-0 mb-1 bg-gray-900 border border-gray-700 rounded shadow-lg z-20">
          {list.map((u, i) => (
            <button key={u.id} onMouseDown={(e) => { e.preventDefault(); pick(u) }}
              className={`w-full text-left px-2 py-1 text-xs flex items-center gap-2 ${i === highlight ? 'bg-indigo-900/50 text-white' : 'text-gray-300'}`}>
              <span className={`inline-block w-2 h-2 rounded-full ${stateStyle(u.state).dot}`} />{u.name}
            </button>
          ))}
        </div>
      )}
      <div className="border border-gray-700 rounded focus-within:border-indigo-500 bg-gray-800/80">
        {toolbar && (
          <div className="flex items-center gap-0.5 px-1.5 py-1 border-b border-gray-700 flex-wrap">
            <select value={family} onChange={(e) => (e.target.value ? editor.chain().focus().setFontFamily(e.target.value).run() : editor.chain().focus().unsetFontFamily().run())}
              className={selectCls} style={{ maxWidth: 84 }} title="Font">
              {FONT_FAMILIES.map((f) => <option key={f.label} value={f.value}>{f.label}</option>)}
            </select>
            <select value={size} onChange={(e) => (e.target.value
              ? (editor.chain().focus() as unknown as { setFontSize: (s: string) => { run: () => void } }).setFontSize(e.target.value).run()
              : (editor.chain().focus() as unknown as { unsetFontSize: () => { run: () => void } }).unsetFontSize().run())}
              className={selectCls} style={{ maxWidth: 52 }} title="Size">
              <option value="">Size</option>
              {FONT_SIZES.map((s) => <option key={s} value={s}>{s.replace('px', '')}</option>)}
            </select>
            <Divider dark />
            <Btn dark active={editor.isActive('bold')} onClick={() => editor.chain().focus().toggleBold().run()} title="Bold"><span className="font-bold">B</span></Btn>
            <Btn dark active={editor.isActive('italic')} onClick={() => editor.chain().focus().toggleItalic().run()} title="Italic"><span className="italic">I</span></Btn>
            <Btn dark active={editor.isActive('underline')} onClick={() => editor.chain().focus().toggleUnderline().run()} title="Underline"><span className="underline">U</span></Btn>
            <Btn dark active={editor.isActive('strike')} onClick={() => editor.chain().focus().toggleStrike().run()} title="Strikethrough"><span className="line-through">S</span></Btn>
            <Divider dark />
            <ColorPicker dark label="Text color" colors={TEXT_COLORS} current={color}
              onSelect={(c) => (c ? editor.chain().focus().setColor(c).run() : editor.chain().focus().unsetColor().run())} />
            <ColorPicker dark label="Highlight" colors={HIGHLIGHT_COLORS} current={mark}
              onSelect={(c) => (c ? editor.chain().focus().setHighlight({ color: c }).run() : editor.chain().focus().unsetHighlight().run())} />
            <Divider dark />
            <Btn dark active={editor.isActive('bulletList')} onClick={() => editor.chain().focus().toggleBulletList().run()} title="Bullet list">•</Btn>
            <Btn dark active={editor.isActive('orderedList')} onClick={() => editor.chain().focus().toggleOrderedList().run()} title="Numbered list">1.</Btn>
            <Divider dark />
            <Btn dark onClick={() => editor.chain().focus().clearNodes().unsetAllMarks().run()} title="Clear formatting">✕</Btn>
          </div>
        )}
        <div className="relative">
          {empty && <span className="absolute left-2 top-1.5 text-xs text-gray-500 pointer-events-none select-none">{placeholder}</span>}
          <EditorContent editor={editor} className="chat-editor px-2 py-1.5 text-xs text-white max-h-48 overflow-y-auto" />
        </div>
        <div className="flex items-center gap-1 px-1.5 pb-1">
          <button type="button" onClick={() => setToolbar((v) => !v)} title="Formatting"
            className={`p-1 rounded ${toolbar ? 'bg-gray-700 text-white' : 'text-gray-400 hover:text-white'}`}><FormattingIcon size={16} /></button>
          <button type="button" onClick={() => fileRef.current?.click()} title="Add an image (or paste one)"
            className="p-1 rounded text-gray-400 hover:text-white"><AddImageIcon size={16} /></button>
          <span className="text-[10px] text-gray-600 truncate flex-1">
            {uploading > 0 ? 'Uploading image…' : 'Enter to send · Shift+Enter new line · @ to mention · paste images'}
          </span>
          {onCancel && <button onClick={onCancel} className="text-[11px] text-gray-400 hover:text-white px-1">Cancel</button>}
          <button onClick={() => void submit()} disabled={busy || uploading > 0}
            className="text-[11px] bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white rounded px-2 py-0.5">{submitLabel}</button>
        </div>
      </div>
      {error && <p className="text-[11px] text-red-400 mt-1">{error}</p>}
      <input ref={fileRef} type="file" accept="image/png,image/jpeg,image/gif,image/webp" multiple className="hidden"
        onChange={(e) => { const f = Array.from(e.target.files ?? []); e.target.value = ''; if (f.length) void addImages(f, editor) }} />
    </div>
  )
})

export default ChatEditor
