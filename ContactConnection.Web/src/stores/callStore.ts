import { create } from 'zustand'

export type CallStatus = 'idle' | 'ringing' | 'dialing' | 'on-call'

interface CallState {
  callStatus: CallStatus
  callerNumber: string | null
  callerName: string | null
  isMuted: boolean
  callStartedAt: number | null  // Date.now() timestamp when call was answered
  callRecordId: string | null

  setRinging: (callerNumber: string, callerName: string) => void
  setDialing: (dialedNumber: string) => void
  setOnCall: () => void
  setMuted: (muted: boolean) => void
  setCallRecordId: (id: string) => void
  reset: () => void
}

export const useCallStore = create<CallState>((set) => ({
  callStatus: 'idle',
  callerNumber: null,
  callerName: null,
  isMuted: false,
  callStartedAt: null,
  callRecordId: null,

  setRinging: (callerNumber, callerName) =>
    set({ callStatus: 'ringing', callerNumber, callerName, isMuted: false, callStartedAt: null, callRecordId: null }),

  setDialing: (dialedNumber) =>
    set({ callStatus: 'dialing', callerNumber: dialedNumber, callerName: null, isMuted: false, callStartedAt: null, callRecordId: null }),

  setOnCall: () =>
    set({ callStatus: 'on-call', callStartedAt: Date.now() }),

  setMuted: (muted) => set({ isMuted: muted }),

  setCallRecordId: (id) => set({ callRecordId: id }),

  reset: () =>
    set({ callStatus: 'idle', callerNumber: null, callerName: null, isMuted: false, callStartedAt: null, callRecordId: null }),
}))
