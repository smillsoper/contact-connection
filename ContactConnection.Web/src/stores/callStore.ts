import { create } from 'zustand'

export type CallStatus    = 'idle' | 'queued' | 'auto-connecting' | 'ringing' | 'dialing' | 'on-call'
export type TransferState = 'idle' | 'dialing' | 'connected' | 'conference'

// tf_secure_collect — a mid-bridge PCI capture parks the agent's leg on hold music. While active
// the softphone's normal Hold/Transfer controls would operate on that parked leg, not the caller,
// so SoftphonePanel swaps them for a progress indicator instead. endedOutcome briefly holds the
// terminal result (cleared by SoftphonePanel after a short display) so the agent sees *why*
// control returns, rather than the banner just vanishing.
export interface SecureCollectState {
  fieldKey: string
  fieldIndex: number
  fieldCount: number
  endedOutcome: 'collected' | 'failed' | 'timeout' | 'caller_hung_up' | null
}

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

  // tf_secure_collect progress — null when no capture is active on this call
  secureCollect: SecureCollectState | null

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
  setSecureCollectProgress: (fieldKey: string, fieldIndex: number, fieldCount: number) => void
  setSecureCollectEnded: (outcome: SecureCollectState['endedOutcome']) => void
  clearSecureCollect: () => void
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
  secureCollect: null,

  setQueued: (callerNumber, callerName, callRecordId, destinationNumber, campaignId) =>
    set({ callStatus: 'queued', callerNumber, callerName, destinationNumber: destinationNumber ?? null, callRecordId, isMuted: false, callStartedAt: null, campaignId: campaignId || null, secureCollect: null, ...TRANSFER_RESET }),

  setAutoConnecting: (callerNumber, callerName, callRecordId, destinationNumber, campaignId) =>
    set({ callStatus: 'auto-connecting', callerNumber, callerName, destinationNumber: destinationNumber ?? null, callRecordId, isMuted: false, callStartedAt: null, campaignId: campaignId || null, secureCollect: null, ...TRANSFER_RESET }),

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
      secureCollect: null,
      ...TRANSFER_RESET,
    })),

  setDialing: (dialedNumber) =>
    set({ callStatus: 'dialing', callerNumber: dialedNumber, callerName: null, isMuted: false, callStartedAt: null, callRecordId: null, campaignId: null, secureCollect: null, ...TRANSFER_RESET }),

  setOnCall: () =>
    set({ callStatus: 'on-call', callStartedAt: Date.now(), secureCollect: null }),

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

  // Field started/advanced — clears any stale endedOutcome from a previous field's transient message.
  setSecureCollectProgress: (fieldKey, fieldIndex, fieldCount) =>
    set({ secureCollect: { fieldKey, fieldIndex, fieldCount, endedOutcome: null } }),

  // Capture finished — keep the last-known field position on screen alongside the outcome;
  // SoftphonePanel shows it briefly then calls clearSecureCollect().
  setSecureCollectEnded: (outcome) =>
    set((state) => ({
      secureCollect: state.secureCollect
        ? { ...state.secureCollect, endedOutcome: outcome }
        : { fieldKey: '', fieldIndex: 0, fieldCount: 0, endedOutcome: outcome },
    })),

  clearSecureCollect: () => set({ secureCollect: null }),

  reset: () =>
    set({ callStatus: 'idle', callerNumber: null, callerName: null, destinationNumber: null, isMuted: false, callStartedAt: null, callRecordId: null, campaignId: null, secureCollect: null, ...TRANSFER_RESET }),
}))
