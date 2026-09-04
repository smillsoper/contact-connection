import { api } from './client'

export interface TtsServiceStatus {
  configured: boolean
  providerKey?: string
  providerName?: string
}

export const ttsServiceApi = {
  getStatus: (): Promise<TtsServiceStatus> => api.get<TtsServiceStatus>('/api/v1/telephony/tts-service-status'),
}
