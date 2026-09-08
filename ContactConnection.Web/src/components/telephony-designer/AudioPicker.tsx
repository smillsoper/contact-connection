import React, { useCallback, useEffect, useRef, useState } from 'react'
import {
  audioFilesApi,
  BUILTIN_AUDIO_GROUPS,
  BUILTIN_AUDIO_OPTIONS,
  PLATFORM_PHRASE_VOICES,
  PLATFORM_PHRASES,
  isPlatformPhraseRef,
  parsePlatformPhraseRef,
  platformPhraseRef,
  platformPhraseLabel,
  type AudioFileRecord,
} from '../../api/audioFiles'
import { ttsServiceApi, type TtsServiceStatus } from '../../api/ttsService'

export type AudioAccent = 'teal' | 'indigo' | 'purple' | 'blue' | 'cyan' | 'amber'

type RecordPhase = 'idle' | 'requesting' | 'recording' | 'review'

// ── Shared audio-files list ─────────────────────────────────────────────────────
// Module-cached so multiple pickers (and the Play node's periodic-announcement editor) share
// one fetch and stay in sync when a clip is uploaded/recorded/regenerated. Keying the designer
// properties panel per node (Session 123) would otherwise refetch this list on every node click.

let _cache: AudioFileRecord[] | null = null
let _inflight: Promise<AudioFileRecord[]> | null = null
const _subs = new Set<(files: AudioFileRecord[]) => void>()

function _emit() {
  const snapshot = _cache ?? []
  _subs.forEach((fn) => fn(snapshot))
}

export function useAudioFiles() {
  const [files, setFiles] = useState<AudioFileRecord[]>(_cache ?? [])

  useEffect(() => {
    _subs.add(setFiles)
    if (_cache) {
      setFiles(_cache)
    } else {
      _inflight ??= audioFilesApi
        .list()
        .then((f) => { _cache = f; _inflight = null; _emit(); return f })
        .catch(() => { _inflight = null; _cache = _cache ?? []; _emit(); return _cache! })
    }
    return () => { _subs.delete(setFiles) }
  }, [])

  const addFile = useCallback((f: AudioFileRecord) => {
    _cache = [...(_cache ?? []), f]
    _emit()
  }, [])

  const updateFile = useCallback((f: AudioFileRecord) => {
    _cache = (_cache ?? []).map((x) => (x.id === f.id ? f : x))
    _emit()
  }, [])

  return { files, addFile, updateFile }
}

// ── Recording helpers ──────────────────────────────────────────────────────────

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

// ── Platform phrase library sub-form ───────────────────────────────────────────

/**
 * Inline form for picking a clip from the platform phrase library — a voice + a common IVR phrase,
 * both from the committed catalog (ContactConnection.Web/src/data/platformPhrases.json). Emits
 * "__platform:{voice}/{phrase}" via onSelect.
 */
export function PlatformPhrasePicker({
  current,
  accent = 'teal',
  onSelect,
  onCancel,
}: {
  current: string
  accent?: AudioAccent
  onSelect: (ref: string) => void
  onCancel: () => void
}) {
  const parsed = parsePlatformPhraseRef(current)
  const [voice, setVoice] = useState(parsed?.voiceKey ?? PLATFORM_PHRASE_VOICES[0]?.key ?? '')
  const [phrase, setPhrase] = useState(parsed?.phraseKey ?? '')
  const [previewUrl, setPreviewUrl] = useState('')
  const [previewLoading, setPreviewLoading] = useState(false)

  useEffect(() => () => { if (previewUrl) URL.revokeObjectURL(previewUrl) }, [previewUrl])

  const categories = [...new Set(PLATFORM_PHRASES.map((p) => p.category))]
  const voiceObj = PLATFORM_PHRASE_VOICES.find((v) => v.key === voice)
  const phraseObj = PLATFORM_PHRASES.find((p) => p.key === phrase)
  const text = phraseObj ? (voiceObj?.lang === 'es' ? phraseObj.es : phraseObj.en) : ''

  function resetPreview() {
    setPreviewUrl((u) => { if (u) URL.revokeObjectURL(u); return '' })
  }
  async function loadPreview() {
    if (!voice || !phrase) return
    setPreviewLoading(true)
    try {
      resetPreview()
      setPreviewUrl(await audioFilesApi.fetchPlatformBlobUrl(voice, phrase))
    } catch { /* silent */ }
    finally { setPreviewLoading(false) }
  }

  return (
    <div className="bg-gray-800 border border-gray-700 rounded p-2 mt-2 flex flex-col gap-2">
      <div className="text-xs text-gray-300 font-medium">Platform phrase library</div>
      <select
        value={voice}
        onChange={(e) => { setVoice(e.target.value); resetPreview() }}
        className="w-full bg-gray-700 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs focus:outline-none focus:border-teal-500"
      >
        <optgroup label="English voices">
          {PLATFORM_PHRASE_VOICES.filter((v) => v.lang === 'en').map((v) => (
            <option key={v.key} value={v.key}>{v.label} — {v.accent} {v.gender}</option>
          ))}
        </optgroup>
        <optgroup label="Spanish voices">
          {PLATFORM_PHRASE_VOICES.filter((v) => v.lang === 'es').map((v) => (
            <option key={v.key} value={v.key}>{v.label} — {v.accent} {v.gender}</option>
          ))}
        </optgroup>
      </select>
      <select
        value={phrase}
        onChange={(e) => { setPhrase(e.target.value); resetPreview() }}
        className="w-full bg-gray-700 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs focus:outline-none focus:border-teal-500"
      >
        <option value="">— Select phrase —</option>
        {categories.map((cat) => (
          <optgroup key={cat} label={cat}>
            {PLATFORM_PHRASES.filter((p) => p.category === cat).map((p) => (
              <option key={p.key} value={p.key}>{p.key}</option>
            ))}
          </optgroup>
        ))}
      </select>
      {text && <p className="text-[10px] text-gray-400 italic leading-snug">&ldquo;{text}&rdquo;</p>}
      {phrase && (
        previewUrl ? (
          <audio
            controls
            src={previewUrl}
            className="w-full"
            style={{ filter: 'invert(0.88) hue-rotate(180deg) brightness(0.85)' }}
          />
        ) : (
          <button
            onClick={loadPreview}
            disabled={previewLoading}
            className={`self-start text-xs text-${accent}-400 hover:text-${accent}-300 disabled:opacity-50`}
          >
            {previewLoading ? 'Loading…' : '▶ Preview'}
          </button>
        )
      )}
      <div className="flex gap-1.5">
        <button
          onClick={() => voice && phrase && onSelect(platformPhraseRef(voice, phrase))}
          disabled={!phrase}
          className={`flex-1 text-xs bg-${accent}-700 hover:bg-${accent}-600 text-white rounded py-1.5 disabled:opacity-50`}
        >
          ✓ Use this phrase
        </button>
        <button
          onClick={onCancel}
          className="text-xs bg-gray-700 hover:bg-gray-600 text-gray-300 rounded px-3 py-1.5"
        >
          Cancel
        </button>
      </div>
    </div>
  )
}

// ── The picker ─────────────────────────────────────────────────────────────────

/**
 * Full-featured audio picker for a single audio-file slot: select (built-in options + tenant
 * clips), preview, upload, in-browser record → review → name → save, platform phrase library, and
 * — when the tenant has a TTS vendor configured — synthesize-and-save a named reusable clip
 * ("Generate TTS"; that clip is a plain saved AudioFile, distinct from Play/Whisper's per-call
 * live-TTS source mode).
 *
 * The single home for this UI — the Play, Whisper, Transfer, Voicemail, IVR Menu and Queue
 * Callback editors all render one instance per slot. Recording/saving under an instance always
 * targets that instance's own field, so a multi-slot node (IVR Menu) has no ambiguity.
 *
 * Props that vary by host:
 *   - `builtins`: 'all' offers hold/tone streams too; 'builtin-only' restricts to finite WAV
 *     built-ins (Whisper — looping streams never fire PLAYBACK_STOP so the bridge never triggers).
 *   - `onAudioFilesChange`: lets the Play editor feed its periodic-announcement playlist the same
 *     (shared, cached) clip list.
 */
export function AudioPicker({
  value,
  onChange,
  accent = 'teal',
  label = 'Audio File',
  blankLabel = '— Select audio —',
  helpText,
  builtins = 'all',
  onAudioFilesChange,
}: {
  value: string
  onChange: (fileId: string) => void
  accent?: AudioAccent
  label?: string
  blankLabel?: string
  helpText?: React.ReactNode
  builtins?: 'all' | 'builtin-only'
  onAudioFilesChange?: (files: AudioFileRecord[]) => void
}) {
  const { files: audioFiles, addFile, updateFile } = useAudioFiles()

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

  // "Generate TTS" — synthesize once via the tenant's configured vendor and save as a named,
  // reusable clip. ttsEditingId non-null = regenerating that existing clip in place.
  const [ttsStatus, setTtsStatus] = useState<TtsServiceStatus | null>(null)
  const [ttsPhase, setTtsPhase] = useState<'idle' | 'form'>('idle')
  const [ttsEditingId, setTtsEditingId] = useState<string | null>(null)
  const [ttsName, setTtsName] = useState('')
  const [ttsText, setTtsText] = useState('')
  const [ttsVoice, setTtsVoice] = useState('')
  const [ttsGenerating, setTtsGenerating] = useState(false)
  const [ttsError, setTtsError] = useState('')

  const [platformOpen, setPlatformOpen] = useState(false)

  useEffect(() => {
    ttsServiceApi.getStatus().then(setTtsStatus).catch(() => setTtsStatus({ configured: false }))
  }, [])

  useEffect(() => {
    return () => {
      if (timerRef.current) clearInterval(timerRef.current)
      if (mediaRecorderRef.current && mediaRecorderRef.current.state !== 'inactive') {
        mediaRecorderRef.current.stop()
      }
      streamRef.current?.getTracks().forEach((t) => t.stop())
    }
  }, [])

  useEffect(() => {
    onAudioFilesChange?.(audioFiles)
  }, [audioFiles, onAudioFilesChange])

  const selectedFile = audioFiles.find((f) => f.id === value)

  function openTtsForm(existing?: AudioFileRecord) {
    setTtsError('')
    if (existing) {
      setTtsEditingId(existing.id)
      setTtsName(existing.name)
      setTtsText(existing.ttsSourceText ?? '')
      setTtsVoice(existing.ttsVoiceId ?? '')
    } else {
      setTtsEditingId(null)
      setTtsName('New TTS Clip')
      setTtsText('')
      setTtsVoice('')
    }
    setTtsPhase('form')
  }

  function closeTtsForm() {
    setTtsPhase('idle')
    setTtsError('')
  }

  async function saveTtsClip() {
    if (!ttsText.trim() || !ttsVoice.trim()) return
    setTtsGenerating(true)
    setTtsError('')
    try {
      const saved = ttsEditingId
        ? await audioFilesApi.regenerateTtsClip(ttsEditingId, ttsText.trim(), ttsVoice.trim(), ttsName.trim() || undefined)
        : await audioFilesApi.saveTtsClip(ttsName.trim() || 'New TTS Clip', ttsText.trim(), ttsVoice.trim())
      if (ttsEditingId) updateFile(saved)
      else addFile(saved)
      onChange(saved.id)
      setTtsPhase('idle')
    } catch (err) {
      setTtsError(err instanceof Error ? err.message : 'Generation failed')
    } finally {
      setTtsGenerating(false)
    }
  }

  const isUploadedFile =
    value.length > 0 &&
    !value.startsWith('local_stream://') &&
    !value.startsWith('silence_stream://') &&
    !value.startsWith('tone_stream://') &&
    !value.startsWith('__builtin:') &&
    !value.startsWith('__platform:')

  useEffect(() => {
    if (lastPreviewedId.current !== value) {
      lastPreviewedId.current = value
      if (previewBlobUrl) {
        URL.revokeObjectURL(previewBlobUrl)
        setPreviewBlobUrl('')
      }
    }
  }, [value, previewBlobUrl])

  async function handleLoadPreview() {
    if (!isUploadedFile) return
    setPreviewLoading(true)
    try {
      if (previewBlobUrl) URL.revokeObjectURL(previewBlobUrl)
      const url = await audioFilesApi.fetchBlobUrl(value)
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
      addFile(uploaded)
      onChange(uploaded.id)
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
      addFile(uploaded)
      onChange(uploaded.id)
      discardRecording()
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : 'Save failed')
    } finally {
      setSavingRecording(false)
    }
  }

  const inputCls = `w-full bg-gray-800 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-sm focus:outline-none focus:border-${accent}-500`
  const labelCls = 'block text-xs text-gray-400 mb-1'
  // Small dark input used in the record-review / TTS-clip sub-forms.
  const subInputCls = `w-full bg-gray-700 border border-gray-600 rounded px-2 py-1 text-gray-100 text-xs focus:outline-none focus:border-${accent}-500`

  const builtinOnlyOptions = BUILTIN_AUDIO_OPTIONS.filter((b) => b.value.startsWith('__builtin:'))

  return (
    <div>
      <label className={labelCls}>{label}</label>
      <select className={inputCls} value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">{blankLabel}</option>
        {builtins === 'builtin-only'
          ? builtinOnlyOptions.length > 0 && (
              <optgroup label="Built-In Options">
                {builtinOnlyOptions.map((b) => (
                  <option key={b.value} value={b.value}>{b.label}</option>
                ))}
              </optgroup>
            )
          : BUILTIN_AUDIO_GROUPS.map((grp) => (
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
        {isPlatformPhraseRef(value) && (
          <optgroup label="Platform Library">
            <option value={value}>{platformPhraseLabel(value)}</option>
          </optgroup>
        )}
      </select>

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
              className={`text-xs text-${accent}-400 hover:text-${accent}-300 disabled:opacity-50`}
            >
              {previewLoading ? 'Loading…' : '▶ Preview selected file'}
            </button>
          )}
          {selectedFile?.isTtsGenerated && recordPhase === 'idle' && ttsPhase === 'idle' && (
            <button
              onClick={() => openTtsForm(selectedFile)}
              className={`block text-xs text-${accent}-400 hover:text-${accent}-300 mt-1`}
            >
              ✎ Edit &amp; regenerate this TTS clip
            </button>
          )}
        </div>
      )}

      {isPlatformPhraseRef(value) && recordPhase === 'idle' && ttsPhase === 'idle' && !platformOpen && (
        <div className="mt-2">
          <button
            onClick={() => setPlatformOpen(true)}
            className={`text-xs text-${accent}-400 hover:text-${accent}-300`}
          >
            ✦ Change platform phrase
          </button>
        </div>
      )}

      {recordPhase === 'idle' && ttsPhase === 'idle' && !platformOpen && (
        <div className="flex flex-wrap gap-1.5 mt-2">
          <label className="flex-1 min-w-[72px] cursor-pointer">
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
            className="flex-1 min-w-[72px] text-xs bg-gray-700 hover:bg-gray-600 text-gray-200 border border-gray-600 rounded px-2 py-1.5 transition-colors"
          >
            ● Record
          </button>
          <button
            onClick={() => setPlatformOpen(true)}
            className="flex-1 min-w-[72px] text-xs bg-gray-700 hover:bg-gray-600 text-gray-200 border border-gray-600 rounded px-2 py-1.5 transition-colors"
          >
            ✦ Platform
          </button>
          {ttsStatus?.configured && (
            <button
              onClick={() => openTtsForm()}
              className="flex-1 min-w-[72px] text-xs bg-gray-700 hover:bg-gray-600 text-gray-200 border border-gray-600 rounded px-2 py-1.5 transition-colors"
            >
              ✦ Generate TTS
            </button>
          )}
        </div>
      )}

      {uploadError && <p className="text-xs text-red-400 mt-1">{uploadError}</p>}

      {recordPhase === 'requesting' && (
        <p className="text-xs text-gray-400 italic mt-1">Requesting microphone access…</p>
      )}

      {recordPhase === 'recording' && (
        <div className="bg-gray-800 border border-red-800 rounded p-2 mt-2 flex items-center gap-2">
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

      {recordPhase === 'review' && recordBlobUrl && (
        <div className="bg-gray-800 border border-gray-700 rounded p-2 mt-2 flex flex-col gap-2">
          <audio
            controls
            src={recordBlobUrl}
            className="w-full"
            style={{ filter: 'invert(0.88) hue-rotate(180deg) brightness(0.85)' }}
          />
          <input
            className={subInputCls}
            value={recordName}
            onChange={(e) => setRecordName(e.target.value)}
            placeholder="Recording name…"
          />
          {saveError && <p className="text-xs text-red-400">{saveError}</p>}
          <div className="flex gap-1.5">
            <button
              onClick={saveRecording}
              disabled={savingRecording}
              className={`flex-1 text-xs bg-${accent}-700 hover:bg-${accent}-600 text-white rounded py-1.5 disabled:opacity-50`}
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

      {ttsPhase === 'form' && (
        <div className="bg-gray-800 border border-gray-700 rounded p-2 mt-2 flex flex-col gap-2">
          <input
            className={subInputCls}
            value={ttsName}
            onChange={(e) => setTtsName(e.target.value)}
            placeholder="Clip name…"
          />
          <textarea
            className={`w-full bg-gray-700 border border-gray-600 rounded px-2 py-1.5 text-gray-100 text-xs focus:outline-none focus:border-${accent}-500 resize-none`}
            rows={3}
            value={ttsText}
            onChange={(e) => setTtsText(e.target.value)}
            placeholder="Text to synthesize…"
          />
          <input
            className={`${subInputCls} font-mono`}
            value={ttsVoice}
            onChange={(e) => setTtsVoice(e.target.value)}
            placeholder={`${ttsStatus?.providerName ?? ttsStatus?.providerKey ?? 'Vendor'} voice ID/name`}
          />
          <p className="text-[10px] text-gray-500 leading-snug">
            Synthesized once via your configured {ttsStatus?.providerName ?? ttsStatus?.providerKey} voice and saved
            as a reusable audio file — reused on every call without re-billing the vendor.
          </p>
          {ttsError && <p className="text-xs text-red-400">{ttsError}</p>}
          <div className="flex gap-1.5">
            <button
              onClick={saveTtsClip}
              disabled={ttsGenerating || !ttsText.trim() || !ttsVoice.trim()}
              className={`flex-1 text-xs bg-${accent}-700 hover:bg-${accent}-600 text-white rounded py-1.5 disabled:opacity-50`}
            >
              {ttsGenerating ? 'Generating…' : ttsEditingId ? '✓ Regenerate & Save' : '✓ Generate & Save'}
            </button>
            <button
              onClick={closeTtsForm}
              disabled={ttsGenerating}
              className="text-xs bg-gray-700 hover:bg-gray-600 text-gray-300 rounded px-3 py-1.5 disabled:opacity-50"
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {platformOpen && (
        <PlatformPhrasePicker
          current={value}
          accent={accent}
          onSelect={(ref) => { onChange(ref); setPlatformOpen(false) }}
          onCancel={() => setPlatformOpen(false)}
        />
      )}

      {helpText && <p className="text-[10px] text-gray-500 mt-1.5 leading-snug">{helpText}</p>}
    </div>
  )
}
