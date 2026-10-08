import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react'
import { useEditor, EditorContent, type Editor } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import { TextStyle } from '@tiptap/extension-text-style'
import { Color } from '@tiptap/extension-color'
import Highlight from '@tiptap/extension-highlight'
import FontFamily from '@tiptap/extension-font-family'
import Image from '@tiptap/extension-image'
import {
  Btn, ColorPicker, Divider, FontSize, FONT_FAMILIES, FONT_SIZES, TEXT_COLORS, HIGHLIGHT_COLORS,
} from '../designer/RichTextEditor'
import { loadHelpdeskImage, uploadHelpdeskImage } from '../../lib/helpdeskFiles'
import { AddImageIcon, BulletListIcon, ClearFormattingIcon, LinkIcon, NumberedListIcon } from '../icons/Icons'

/**
 * The help desk topic editor (S184): the chat formatting (font, size, bold / italic / underline / strike, colour,
 * highlight, lists) plus headings, quotes, a divider and links; images pasted, dropped or picked are uploaded to the
 * help desk first and referenced by id. The server sanitizes whatever this produces.
 */

/** Images carry the uploaded file's id; src is only the local display copy (the server drops it). */
const HelpdeskImage = Image.extend({
  // Saved topics keep only the id (the server drops src) — the stock image reads only <img src>, so reopening a topic
  // would silently drop its images.
  parseHTML() { return [{ tag: 'img[data-hd-file]' }] },
  addAttributes() {
    return {
      ...this.parent?.(),
      hdFile: {
        default: null,
        parseHTML: (el: HTMLElement) => el.getAttribute('data-hd-file'),
        renderHTML: (a: { hdFile: string | null }) => (a.hdFile ? { 'data-hd-file': a.hdFile } : {}),
      },
    }
  },
}).configure({ inline: false, allowBase64: false })

export interface HelpdeskEditorHandle { getHtml: () => string; isUploading: () => boolean }

interface Props { helpdeskId: string; initialHtml: string }

const HelpdeskEditor = forwardRef<HelpdeskEditorHandle, Props>(function HelpdeskEditor({ helpdeskId, initialHtml }, ref) {
  const fileRef = useRef<HTMLInputElement>(null)
  const [uploading, setUploading] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [link, setLink] = useState<string | null>(null)

  async function addImages(files: File[], editor: Editor) {
    for (const file of files.slice(0, 10)) {
      setUploading((n) => n + 1); setError(null)
      try {
        const { id, url } = await uploadHelpdeskImage(helpdeskId, file)
        editor.chain().focus().insertContent([{ type: 'image', attrs: { src: url, hdFile: id } }, { type: 'paragraph' }]).run()
      } catch (e) { setError(e instanceof Error ? e.message : 'Image upload failed.') }
      finally { setUploading((n) => n - 1) }
    }
  }

  const editor = useEditor({
    extensions: [
      StarterKit.configure({
        heading: { levels: [2, 3, 4] }, codeBlock: false,
        link: { openOnClick: false, autolink: true, defaultProtocol: 'https', protocols: ['http', 'https', 'mailto'] },
      }),
      TextStyle, Color, Highlight.configure({ multicolor: true }), FontFamily, FontSize, HelpdeskImage,
    ],
    content: initialHtml,
    editorProps: {
      attributes: { class: 'helpdesk-rich' },
      handlePaste(_view, event) { return takeImages(Array.from(event.clipboardData?.files ?? []), event) },
      handleDrop(_view, event) { return takeImages(Array.from((event as DragEvent).dataTransfer?.files ?? []), event) },
    },
  })
  const editorRef = useRef<Editor | null>(null)
  editorRef.current = editor

  function takeImages(all: File[], event: Event) {
    const images = all.filter((f) => /^image\/(png|jpeg|gif|webp)$/.test(f.type))
    if (images.length === 0 || !editorRef.current) return false
    event.preventDefault()
    void addImages(images, editorRef.current)
    return true
  }

  useImperativeHandle(ref, () => ({
    getHtml: () => editor?.getHTML() ?? '',
    isUploading: () => uploading > 0,
  }), [editor, uploading])

  // Existing images arrive as ids without a src.
  useEffect(() => {
    if (!editor) return
    editor.state.doc.descendants((node, pos) => {
      if (node.type.name !== 'image' || !node.attrs.hdFile || node.attrs.src) return
      loadHelpdeskImage(node.attrs.hdFile).then((url) => {
        if (editor.isDestroyed) return
        const current = editor.state.doc.nodeAt(pos)
        if (current?.type.name === 'image') editor.view.dispatch(editor.state.tr.setNodeMarkup(pos, undefined, { ...current.attrs, src: url }))
      }).catch(() => {})
    })
  }, [editor])

  if (!editor) return null
  const color = editor.getAttributes('textStyle').color ?? ''
  const mark = editor.getAttributes('highlight').color ?? ''
  const family = editor.getAttributes('textStyle').fontFamily ?? ''
  const size = editor.getAttributes('textStyle').fontSize ?? ''
  const block = editor.isActive('heading', { level: 2 }) ? 'h2' : editor.isActive('heading', { level: 3 }) ? 'h3'
    : editor.isActive('heading', { level: 4 }) ? 'h4' : 'p'
  const selectCls = 'h-7 text-xs border rounded px-1 focus:outline-none shrink-0 bg-gray-700 border-gray-600 text-gray-200'

  function openLink() {
    setLink(editor!.getAttributes('link').href ?? 'https://')
  }
  function applyLink() {
    const href = (link ?? '').trim()
    const chain = editor!.chain().focus().extendMarkRange('link')
    if (!href || href === 'https://') chain.unsetLink().run()
    else {
      const full = /^(https?:|mailto:)/i.test(href) ? href : `https://${href}`
      if (editor!.state.selection.empty && !editor!.isActive('link')) editor!.chain().focus().insertContent({ type: 'text', text: full, marks: [{ type: 'link', attrs: { href: full } }] }).run()
      else chain.setLink({ href: full }).run()
    }
    setLink(null)
  }

  return (
    <div>
      <div className="border border-gray-700 rounded-lg focus-within:border-indigo-500 bg-gray-800/60">
        <div className="flex items-center gap-0.5 px-2 py-1.5 border-b border-gray-700 flex-wrap">
          <select value={block} title="Paragraph or heading" className={selectCls} style={{ maxWidth: 100 }}
            onChange={(e) => {
              const v = e.target.value
              if (v === 'p') editor.chain().focus().setParagraph().run()
              else editor.chain().focus().setHeading({ level: Number(v.slice(1)) as 2 | 3 | 4 }).run()
            }}>
            <option value="p">Text</option>
            <option value="h2">Heading</option>
            <option value="h3">Subheading</option>
            <option value="h4">Small heading</option>
          </select>
          <select value={family} onChange={(e) => (e.target.value ? editor.chain().focus().setFontFamily(e.target.value).run() : editor.chain().focus().unsetFontFamily().run())}
            className={selectCls} style={{ maxWidth: 96 }} title="Font">
            {FONT_FAMILIES.map((f) => <option key={f.label} value={f.value}>{f.label}</option>)}
          </select>
          <select value={size} onChange={(e) => (e.target.value
            ? (editor.chain().focus() as unknown as { setFontSize: (s: string) => { run: () => void } }).setFontSize(e.target.value).run()
            : (editor.chain().focus() as unknown as { unsetFontSize: () => { run: () => void } }).unsetFontSize().run())}
            className={selectCls} style={{ maxWidth: 60 }} title="Size">
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
          <Btn dark active={editor.isActive('bulletList')} onClick={() => editor.chain().focus().toggleBulletList().run()} title="Bullet list"><BulletListIcon size={14} /></Btn>
          <Btn dark active={editor.isActive('orderedList')} onClick={() => editor.chain().focus().toggleOrderedList().run()} title="Numbered list"><NumberedListIcon size={14} /></Btn>
          <Btn dark active={editor.isActive('blockquote')} onClick={() => editor.chain().focus().toggleBlockquote().run()} title="Quote"><span className="font-serif text-sm leading-none">“</span></Btn>
          <Btn dark onClick={() => editor.chain().focus().setHorizontalRule().run()} title="Divider line"><span className="text-xs leading-none">—</span></Btn>
          <Divider dark />
          <Btn dark active={editor.isActive('link')} onClick={openLink} title="Link (opens in a new window for agents)"><LinkIcon size={14} /></Btn>
          <Btn dark onClick={() => fileRef.current?.click()} title="Insert an image (or paste / drop one)"><AddImageIcon size={14} /></Btn>
          <Divider dark />
          <Btn dark onClick={() => editor.chain().focus().clearNodes().unsetAllMarks().run()} title="Clear formatting"><ClearFormattingIcon size={14} /></Btn>
        </div>
        {link !== null && (
          <div className="flex items-center gap-2 px-2 py-1.5 border-b border-gray-700 bg-gray-900/60">
            <LinkIcon size={14} className="text-gray-400" />
            <input autoFocus value={link} onChange={(e) => setLink(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); applyLink() } if (e.key === 'Escape') setLink(null) }}
              placeholder="https://…" className="flex-1 bg-gray-800 border border-gray-600 rounded px-2 py-1 text-xs text-white focus:outline-none focus:border-indigo-500" />
            <button onClick={applyLink} className="text-xs bg-indigo-600 hover:bg-indigo-500 text-white rounded px-2 py-1">Apply</button>
            {editor.isActive('link') && (
              <button onClick={() => { editor.chain().focus().extendMarkRange('link').unsetLink().run(); setLink(null) }}
                className="text-xs text-gray-300 hover:text-white px-1">Remove link</button>
            )}
            <button onClick={() => setLink(null)} className="text-xs text-gray-400 hover:text-white px-1">Cancel</button>
          </div>
        )}
        <EditorContent editor={editor} className="helpdesk-editor px-3 py-2 text-sm text-gray-100 max-h-[55vh] overflow-y-auto" />
        <div className="px-3 pb-1.5 text-[11px] text-gray-500">
          {uploading > 0 ? 'Uploading image…' : 'Paste or drop images straight in. Select text and click the link button to add a link.'}
        </div>
      </div>
      {error && <p className="text-xs text-red-400 mt-1">{error}</p>}
      <input ref={fileRef} type="file" accept="image/png,image/jpeg,image/gif,image/webp" multiple className="hidden"
        onChange={(e) => { const f = Array.from(e.target.files ?? []); e.target.value = ''; if (f.length) void addImages(f, editor) }} />
    </div>
  )
})

export default HelpdeskEditor
