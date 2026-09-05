import { api } from './client'
import { useAuthStore } from '../stores/authStore'
import { getSubdomainFromHostname } from '../utils/subdomain'
import platformPhrasesRaw from '../data/platformPhrases.json'

// ---- Platform phrase library ---------------------------------------------------
// Platform-wide catalog of common IVR phrases, pre-synthesized once (per voice) by
// scripts/generate-platform-phrases.mjs into committed OGGs under freeswitch/sounds/_platform/.
// A flow references one as "__platform:{voiceKey}/{phraseKey}"; TelephonyAudioResolver expands it.
// Available to every tenant regardless of their own TTS vendor config.
export interface PlatformPhraseVoice {
  key: string
  id: string
  label: string
  lang: 'en' | 'es'
  accent: string
  gender: 'male' | 'female'
}
export interface PlatformPhrase {
  key: string
  category: string
  en: string
  es: string
}
export const PLATFORM_PHRASE_VOICES = platformPhrasesRaw.voices as unknown as PlatformPhraseVoice[]
export const PLATFORM_PHRASES = platformPhrasesRaw.phrases as unknown as PlatformPhrase[]

export const PLATFORM_REF_PREFIX = '__platform:'
export const platformPhraseRef = (voiceKey: string, phraseKey: string) =>
  `${PLATFORM_REF_PREFIX}${voiceKey}/${phraseKey}`
export const isPlatformPhraseRef = (v: string) => v.startsWith(PLATFORM_REF_PREFIX)
export function parsePlatformPhraseRef(v: string): { voiceKey: string; phraseKey: string } | null {
  if (!v.startsWith(PLATFORM_REF_PREFIX)) return null
  const [voiceKey, phraseKey] = v.slice(PLATFORM_REF_PREFIX.length).split('/')
  return voiceKey && phraseKey ? { voiceKey, phraseKey } : null
}
/** Human label for a platform ref, e.g. `Will — "Please hold while we connect your call."` */
export function platformPhraseLabel(v: string): string | null {
  const parsed = parsePlatformPhraseRef(v)
  if (!parsed) return null
  const voice = PLATFORM_PHRASE_VOICES.find((x) => x.key === parsed.voiceKey)
  const phrase = PLATFORM_PHRASES.find((x) => x.key === parsed.phraseKey)
  if (!voice || !phrase) return `Platform: ${parsed.voiceKey}/${parsed.phraseKey}`
  const text = voice.lang === 'es' ? phrase.es : phrase.en
  const short = text.length > 60 ? `${text.slice(0, 57)}…` : text
  return `${voice.label} — "${short}"`
}

export interface AudioFileRecord {
  id: string
  name: string
  originalFileName: string
  contentType: string
  fileSizeBytes: number
  createdAt: string
  isTtsGenerated: boolean
  ttsSourceText: string | null
  ttsProviderKey: string | null
  ttsVoiceId: string | null
}

// Built-in FreeSWITCH audio options — available without uploading anything.
// Grouped for use in the Play node dropdown.
// "__builtin:" prefix tells the backend to use the path directly as a FreeSWITCH media arg.
// "tone_stream://..." generates tones in-process; loops=-1 means FreeSWITCH loops internally.
export const BUILTIN_AUDIO_GROUPS: { group: string; options: { value: string; label: string }[] }[] = [
  {
    group: 'Hold / Background',
    options: [
      { value: 'local_stream://moh', label: 'Hold Music (auto-loop MOH playlist)' },
      { value: 'silence_stream://-1,1400', label: 'Comfort Noise' },
      { value: 'silence_stream://-1,0', label: 'Pure Silence' },
    ],
  },
  {
    group: 'Ring Tones',
    options: [
      { value: 'tone_stream://%(2000,4000,440,480);loops=-1', label: 'US Ring Back (440+480 Hz)' },
      { value: 'tone_stream://%(400,200,400,450);%(400,2000,400,450);loops=-1', label: 'UK Ring Back (400+450 Hz)' },
      { value: 'tone_stream://%(1000,4000,425);loops=-1', label: 'EU Ring Back (425 Hz)' },
    ],
  },
  {
    group: 'Status Tones',
    options: [
      { value: 'tone_stream://%(500,500,480,620);loops=-1', label: 'US Busy Signal (480+620 Hz)' },
    ],
  },
  {
    group: 'Music (Built-In)',
    options: [
      { value: '__builtin:/usr/share/freeswitch/sounds/music/8000/danza-espanola-op-37-h-142-xii-arabesca.wav', label: 'Classical — Danza Española (Arabesca)' },
      { value: '__builtin:/usr/share/freeswitch/sounds/music/8000/partita-no-3-in-e-major-bwv-1006-1-preludio.wav', label: 'Classical — Bach Partita No. 3 (Preludio)' },
      { value: '__builtin:/usr/share/freeswitch/sounds/music/8000/ponce-preludio-in-e-major.wav', label: 'Classical — Ponce Preludio in E Major' },
      { value: '__builtin:/usr/share/freeswitch/sounds/music/8000/suite-espanola-op-47-leyenda.wav', label: 'Classical — Suite Española (Leyenda)' },
    ],
  },
]

// Flat list — used where filtering by value prefix is simpler than iterating groups.
export const BUILTIN_AUDIO_OPTIONS = BUILTIN_AUDIO_GROUPS.flatMap((g) => g.options)

const BASE = '/api/v1/audio-files'

export const audioFilesApi = {
  list: (): Promise<AudioFileRecord[]> => api.get<AudioFileRecord[]>(BASE),

  upload: async (file: File, name?: string): Promise<AudioFileRecord> => {
    const { token, tenantSubdomain } = useAuthStore.getState()
    const subdomain = getSubdomainFromHostname() ?? tenantSubdomain

    const form = new FormData()
    form.append('file', file)
    if (name) form.append('name', name)

    const headers: Record<string, string> = {}
    if (token) headers['Authorization'] = `Bearer ${token}`
    if (subdomain) headers['X-Tenant-Subdomain'] = subdomain

    const res = await fetch(BASE, { method: 'POST', headers, body: form })
    if (!res.ok) {
      const err = await res.json().catch(() => ({}))
      throw new Error((err as { error?: string }).error ?? `Upload failed: ${res.status}`)
    }
    return res.json()
  },

  delete: (id: string): Promise<void> => api.delete<void>(`${BASE}/${id}`),

  // Synthesizes the tenant's configured TTS vendor voice once and saves it as a new, named,
  // reusable audio file — behaves exactly like an upload from then on (appears in every picker).
  saveTtsClip: (name: string, text: string, voiceId: string): Promise<AudioFileRecord> =>
    api.post<AudioFileRecord>(`${BASE}/tts`, { name, text, voiceId }),

  // Re-synthesizes an existing saved TTS clip in place — same id, new audio — so every node still
  // pointing at it by audioFileId keeps working. Only valid on a clip created via saveTtsClip.
  regenerateTtsClip: (id: string, text: string, voiceId: string, name?: string): Promise<AudioFileRecord> =>
    api.put<AudioFileRecord>(`${BASE}/${id}/tts`, { name, text, voiceId }),

  streamUrl: (id: string) => `${BASE}/${id}/stream`,

  // Fetches audio as a blob URL (handles auth headers, required for <audio> preview)
  fetchBlobUrl: async (id: string): Promise<string> => {
    const { token, tenantSubdomain } = useAuthStore.getState()
    const subdomain = getSubdomainFromHostname() ?? tenantSubdomain
    const headers: Record<string, string> = {}
    if (token) headers['Authorization'] = `Bearer ${token}`
    if (subdomain) headers['X-Tenant-Subdomain'] = subdomain
    const res = await fetch(`${BASE}/${id}/stream`, { headers })
    if (!res.ok) throw new Error(`Failed to load audio (${res.status})`)
    const blob = await res.blob()
    return URL.createObjectURL(blob)
  },

  // Preview blob URL for a platform phrase-library clip (committed OGG, not tenant-scoped).
  fetchPlatformBlobUrl: async (voiceKey: string, phraseKey: string): Promise<string> => {
    const { token, tenantSubdomain } = useAuthStore.getState()
    const subdomain = getSubdomainFromHostname() ?? tenantSubdomain
    const headers: Record<string, string> = {}
    if (token) headers['Authorization'] = `Bearer ${token}`
    if (subdomain) headers['X-Tenant-Subdomain'] = subdomain
    const res = await fetch(`${BASE}/platform/${voiceKey}/${phraseKey}/stream`, { headers })
    if (!res.ok) throw new Error(`Failed to load audio (${res.status})`)
    return URL.createObjectURL(await res.blob())
  },
}
