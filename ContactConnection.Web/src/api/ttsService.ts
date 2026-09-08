import { api } from './client'

export interface TtsServiceStatus {
  configured: boolean
  providerKey?: string
  providerName?: string
}

// The tenant's TTS-streaming preference (Admin → API Preferences) doesn't change while someone
// designs a flow, so cache the in-flight promise and reuse it. Keying the designer properties
// panel per node (so per-node picker state resets) would otherwise refetch this on every node
// click — several times over, since four picker components each ask for it. A failed fetch
// clears the cache so a later caller retries.
let cached: Promise<TtsServiceStatus> | null = null

export const ttsServiceApi = {
  getStatus: (): Promise<TtsServiceStatus> => {
    cached ??= api.get<TtsServiceStatus>('/api/v1/telephony/tts-service-status').catch((err) => {
      cached = null
      throw err
    })
    return cached
  },
}
