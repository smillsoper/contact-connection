import { api } from './client'
import { useAuthStore } from '../stores/authStore'
import { getSubdomainFromHostname } from '../utils/subdomain'

export interface AudioFileRecord {
  id: string
  name: string
  originalFileName: string
  contentType: string
  fileSizeBytes: number
  createdAt: string
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
      { value: '__builtin:/usr/share/freeswitch/sounds/music/8000/danza-espanola-op-37-h-142-1.wav', label: 'Classical — Danza Española 1' },
      { value: '__builtin:/usr/share/freeswitch/sounds/music/8000/danza-espanola-op-37-h-142-2.wav', label: 'Classical — Danza Española 2' },
      { value: '__builtin:/usr/share/freeswitch/sounds/music/8000/danza-espanola-op-37-h-142-3.wav', label: 'Classical — Danza Española 3' },
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
}
