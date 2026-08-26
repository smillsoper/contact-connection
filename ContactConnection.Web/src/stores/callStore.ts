import { create } from 'zustand'

export type CallStatus    = 'idle' | 'queued' | 'auto-connecting' | 'ringing' | 'dialing' | 'on-call'
export type TransferState = 'idle' | 'dialing' | 'connected' | 'conference'

interface CallState {
  callStatus: CallStatus
  callerNumber: string | null
  callerName: string | null
  destinationNumber: string | null   // DNIS — the number the caller dialed
  isMuted: boolean
  isOnHold: boolean
  callStartedAt: number | null  // Date.now() timestamp when call was answered
  callRecordId: string | null
  campaignId: string | null

  // Warm-transfer consultation leg
  transferState: TransferState
  transferTarget: string | null       // E.164 number being consulted
  transferTargetLabel: string | null  // Display label from the transfer number config

  setQueued: (callerNumber: string, callerName: string, callRecordId: string, destinationNumber?: string, campaignId?: string) => void
  // RingStrategy.AutoAnswerBestAgent — server picked this agent, no click required. Pushed via
  // receiveAutoConnecting before the whisper/bridge INVITE arrives, so SoftphonePanel can arm
  // auto-answer proactively (see its useEffect on callStatus === 'auto-connecting').
  setAutoConnecting: (callerNumber: string, callerName: string, callRecordId: string, destinationNumber?: string, campaignId?: string) => void
  setRinging: (callerNumber: string, callerName: string) => void
  setDialing: (dialedNumber: string) => void
  setOnCall: () => void
  setMuted: (muted: boolean) => void
  setOnHold: (held: boolean) => void
  setCallRecordId: (id: string) => void
  setCampaignId: (id: string) => void
  setTransferDialing: (target: string, label: string) => void
  setTransferConnected: () => void
  setTransferConference: () => void
  resetTransfer: () => void
  reset: () => void
}

const TRANSFER_RESET = {
  transferState: 'idle' as TransferState,
  transferTarget: null,
  transferTargetLabel: null,
  isOnHold: false,
}

export const useCallStore = create<CallState>((set) => ({
  callStatus: 'idle',
  callerNumber: null,
  callerName: null,
  destinationNumber: null,
  isMuted: false,
  isOnHold: false,
  callStartedAt: null,
  callRecordId: null,
  campaignId: null,
  transferState: 'idle',
  transferTarget: null,
  transferTargetLabel: null,

  setQueued: (callerNumber, callerName, callRecordId, destinationNumber, campaignId) =>
    set({ callStatus: 'queued', callerNumber, callerName, destinationNumber: destinationNumber ?? null, callRecordId, isMuted: false, callStartedAt: null, campaignId: campaignId || null, ...TRANSFER_RESET }),

  setAutoConnecting: (callerNumber, callerName, callRecordId, destinationNumber, campaignId) =>
    set({ callStatus: 'auto-connecting', callerNumber, callerName, destinationNumber: destinationNumber ?? null, callRecordId, isMuted: false, callStartedAt: null, campaignId: campaignId || null, ...TRANSFER_RESET }),

  // When transitioning from 'queued' → 'ringing' (agent answered via bridge), preserve the
  // screen-pop callRecordId so we don't lose the DID-routed call record association.
  setRinging: (callerNumber, callerName) =>
    set((state) => ({
      callStatus: 'ringing',
      callerNumber,
      callerName,
      isMuted: false,
      callStartedAt: null,
      callRecordId: state.callStatus === 'queued' ? state.callRecordId : null,
      campaignId: null,
      ...TRANSFER_RESET,
    })),

  setDialing: (dialedNumber) =>
    set({ callStatus: 'dialing', callerNumber: dialedNumber, callerName: null, isMuted: false, callStartedAt: null, callRecordId: null, campaignId: null, ...TRANSFER_RESET }),

  setOnCall: () =>
    set({ callStatus: 'on-call', callStartedAt: Date.now() }),

  setMuted:  (muted) => set({ isMuted: muted }),
  setOnHold: (held)  => set({ isOnHold: held }),

  setCallRecordId: (id) => set({ callRecordId: id }),
  setCampaignId:   (id) => set({ campaignId: id }),

  setTransferDialing: (target, label) =>
    set({ transferState: 'dialing', transferTarget: target, transferTargetLabel: label }),

  setTransferConnected: () =>
    set({ transferState: 'connected' }),

  // Entering conference unholds the primary so all three parties can talk
  setTransferConference: () =>
    set({ transferState: 'conference', isOnHold: false }),

  resetTransfer: () =>
    set(TRANSFER_RESET),

  reset: () =>
    set({ callStatus: 'idle', callerNumber: null, callerName: null, destinationNumber: null, isMuted: false, callStartedAt: null, callRecordId: null, campaignId: null, ...TRANSFER_RESET }),
}))
