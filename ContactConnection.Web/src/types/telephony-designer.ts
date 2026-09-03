export type TelephonyNodeType =
  | 'tf_check_block_list'
  | 'tf_check_agent_availability'
  | 'tf_reject'
  | 'tf_answer'
  | 'tf_hangup'
  | 'tf_route_to_queue'
  | 'tf_transfer'
  | 'tf_play'
  | 'tf_time_of_day'
  | 'tf_branch'
  | 'tf_end'
  | 'tf_set_variable'
  | 'tf_get_sip_header'
  | 'tf_set_sip_header'
  | 'tf_set_caller_id'
  | 'tf_cancel_dial'
  | 'tf_script_pop'
  | 'tf_general_api_call'
  // Signal / media actions
  | 'tf_dtmf'
  | 'tf_ivr_menu'
  | 'tf_record'
  | 'tf_voicemail'
  | 'tf_scheduled_callback'
  // Agent-selected event branch actions
  | 'tf_whisper'
  // Event listener nodes — independent entry points that fire on lifecycle events
  | 'tf_on_agent_selected'
  | 'tf_on_agent_answer'
  | 'tf_on_call_disconnected'
  | 'tf_on_custom_event'
  | 'tf_event_wait'

export interface TelVariableAssignment {
  key: string
  value: string
}

export interface TelNodeData extends Record<string, unknown> {
  label: string
  isEntry?: boolean
  // tf_reject
  cause?: 'busy' | 'unavailable' | 'declined'
  // tf_check_block_list
  checkVariable?: string
  // tf_check_agent_availability
  campaignId?: string
  // tf_route_to_queue (also the "agent" destination of tf_transfer) — stores the agent's SIP extension
  agentExtension?: string
  // tf_transfer
  destinationType?: 'campaign_queue' | 'agent' | 'telephony_flow' | 'external_number'
  targetCampaignId?: string
  targetTelephonyFlowId?: string
  externalNumber?: string          // E.164 (+18005551234) or a full SIP URI (sip:user@host)
  externalGatewayName?: string     // FreeSWITCH gateway name; blank = server default. Ignored for sip: URIs
  announceAudioFileId?: string     // played to the caller before handoff; TTS fallback below
  announceTtsText?: string
  announceTtsVoice?: string
  screenPopFlowId?: string         // CRM script flow the receiving agent gets instead of the campaign default
  // tf_play
  audioSource?: 'file' | 'tts'
  audioFileId?: string
  ttsText?: string
  ttsVoice?: string
  durationSeconds?: number
  startOffsetSeconds?: number
  rememberPosition?: boolean
  autoRestart?: boolean
  periodicAnnouncements?: Array<{ fileId: string }>
  periodicAnnouncementIntervalSeconds?: number
  // tf_time_of_day
  timezone?: string
  windows?: TimeWindow[]
  // tf_branch
  condition?: string
  // tf_set_variable
  assignments?: TelVariableAssignment[]
  // tf_get_sip_header
  headerName?: string
  variableName?: string
  // tf_set_sip_header
  sipHeaderName?: string
  sipHeaderValue?: string
  // tf_set_caller_id
  callerIdValue?: string
  // tf_cancel_dial
  cancelMessage?: string
  // tf_script_pop
  flowId?: string
  // tf_general_api_call
  apiEndpointId?: string
  apiDefinitionScope?: 'tenant' | 'portal'
  apiDefinitionName?: string
  apiEndpointName?: string
  outputVariable?: string
  timeoutSeconds?: number
  // tf_dtmf
  digits?: string
  durationMs?: number
  interDigitGapMs?: number
  waitForCompletion?: boolean
  // tf_ivr_menu — prompts are audio files (play_and_get_digits can't take a TTS string with spaces)
  promptAudioFileId?: string
  invalidAudioFileId?: string
  minDigits?: number
  maxDigits?: number
  maxTries?: number
  timeoutMs?: number
  interDigitTimeoutMs?: number
  terminators?: string
  /** Each maps an exact DTMF entry to a named transition (its own source handle on the canvas). */
  options?: { digit: string; transition: string; label?: string }[]
  // tf_record
  action?: 'start' | 'stop' | 'mask' | 'unmask'
  maskFill?: 'silence' | 'tone' | 'comfort_noise'   // mask only
  maxMaskSeconds?: number                            // mask only — auto-unmask watchdog override
  recordLimitSeconds?: number                        // start only — 0 = unlimited
  reason?: string                                    // audit context, e.g. "pan", "ssn"
  // start only — two-party consent announcement, played before recording when the campaign's
  // consent model requires it. Audio file first; TTS text is the fallback.
  consentAudioFileId?: string
  consentTtsText?: string
  consentTtsVoice?: string
  // tf_voicemail — greeting (audio file first, TTS fallback), record limits, optional email delivery
  greetingAudioFileId?: string
  greetingTtsText?: string
  greetingTtsVoice?: string
  beepEnabled?: boolean
  maxLengthSeconds?: number
  maxSilenceSeconds?: number
  minLengthSeconds?: number
  deliveryEmailEnabled?: boolean
  /** to / cc / bcc are comma-separated and may contain {{variables}}, resolved at send time. */
  deliveryEmailTo?: string
  deliveryEmailCc?: string
  deliveryEmailBcc?: string
  deliveryEmailFromName?: string
  deliveryEmailReplyTo?: string
  deliveryEmailSubject?: string
  /** HTML from the rich-text editor; {{caller.*}} / {{call_record.*}} / {{flow.*}} resolved at send time. */
  deliveryEmailBodyHtml?: string
  deliveryAttachAudio?: boolean
  // tf_scheduled_callback — book a callback for a specific future time
  numberSource?: 'ani' | 'collected'
  collectedVar?: string            // session/channel var to read when numberSource = 'collected'
  scheduledDateValue?: string      // date text or {{variable}} (e.g. "2026-09-10", "9/10/2026")
  scheduledTimeValue?: string      // time text or {{variable}} (e.g. "14:30", "2:30 PM"); blank => 09:00
  targetFlowId?: string            // telephony flow the answered leg runs (should NOT re-offer callback)
  targetCampaignId?: string        // optional campaign context for the answered leg's queue
  allowedDays?: string             // optional CSV of 0-6 (0=Sun) the callback may land on
  allowedStartTime?: string        // optional "HH:mm" earliest local time-of-day
  allowedEndTime?: string          // optional "HH:mm" latest local time-of-day
  windowMinutes?: number           // how long past the booked time the worker keeps trying
  maxAttempts?: number             // outbound attempts before the callback is abandoned
  callerIdOverride?: string         // outbound CID; blank = DNIS the caller dialed. Literal or {{variable}} (frozen at request time)
  // tf_whisper
  // (audioFileId reused from tf_play)
  // tf_on_custom_event
  eventName?: string
}

export interface TimeWindow {
  name: string
  windowType?: 'weekly' | 'holiday' | 'date'  // defaults to 'weekly'; 'date' has highest priority
  days?: number[]    // for weekly: 0=Sun … 6=Sat
  start: string      // "HH:mm"
  end: string        // "HH:mm"
  holiday?: string   // for holiday: key like 'christmas', 'thanksgiving', etc.
  date?: string      // for date: "YYYY-MM-DD" specific one-time date
}

export interface TelephonyNodeDef {
  type: TelephonyNodeType
  label: string
  cause?: string
  campaignId?: string
  agentExtension?: string
  timezone?: string
  windows?: TimeWindow[]
  condition?: string
  eventName?: string
  flowId?: string
  // tf_transfer (see TelNodeData for the rest — persisted via spread, this is just the common subset)
  destinationType?: string
  targetCampaignId?: string
  targetTelephonyFlowId?: string
  externalNumber?: string
  screenPopFlowId?: string
  _pos?: { x: number; y: number }
  transitions: Record<string, string>
}

export interface TelephonyFlowDefinition {
  flow_type: 'telephony'
  name: string
  entry_node: string
  nodes: Record<string, TelephonyNodeDef>
  _waypoints?: Record<string, { x: number; y: number }[]>
}

export const TELEPHONY_NODE_META: Record<
  TelephonyNodeType,
  { label: string; color: string; description: string; handles: 'single' | 'dual' | 'none' | 'multi' | 'source-only' }
> = {
  tf_check_block_list: {
    label: 'Block List',
    color: '#dc2626',
    description: 'Check if the caller is on the block list',
    handles: 'dual',
  },
  tf_check_agent_availability: {
    label: 'Agent Availability',
    color: '#0369a1',
    description: 'Check if agents are available for this campaign',
    handles: 'dual',
  },
  tf_reject: {
    label: 'Reject',
    color: '#7f1d1d',
    description: 'Reject the call with a SIP cause code',
    handles: 'none',
  },
  tf_answer: {
    label: 'Answer',
    color: '#15803d',
    description: 'Answer the inbound call',
    handles: 'single',
  },
  tf_hangup: {
    label: 'Hang Up',
    color: '#991b1b',
    description: 'Hang up the call (post-answer)',
    handles: 'none',
  },
  tf_route_to_queue: {
    label: 'Route to Queue',
    color: '#1d4ed8',
    description: 'Push the call to the agent queue',
    // 'default' (chain into e.g. hold music) always renders, same as before; 'on_timeout' is a
    // second, optional-to-wire handle for MaxQueueSize/QueueTimeoutSeconds overflow — see
    // RouteToQueueNode.tsx.
    handles: 'multi',
  },
  tf_transfer: {
    label: 'Transfer',
    color: '#4338ca',
    description: 'Hand the caller to another queue, agent, flow, or external number',
    // 'transferred' (usually terminal) + 'failed' (handoff could not be set up) source handles.
    handles: 'multi',
  },
  tf_play: {
    label: 'Play',
    color: '#0f766e',
    description: 'Play an audio file or TTS on the call channel',
    handles: 'multi',
  },
  tf_time_of_day: {
    label: 'Time of Day',
    color: '#92400e',
    description: 'Branch based on day/time schedule',
    handles: 'multi',
  },
  tf_branch: {
    label: 'Branch',
    color: '#b45309',
    description: 'Conditional split on a variable',
    handles: 'dual',
  },
  tf_end: {
    label: 'End',
    color: '#374151',
    description: 'End this branch (call session stays live until disconnect)',
    handles: 'none',
  },
  tf_set_variable: {
    label: 'Set Variable',
    color: '#7c3aed',
    description: 'Assign one or more named variables for use by later nodes',
    handles: 'single',
  },
  tf_get_sip_header: {
    label: 'Get SIP Header',
    color: '#0f766e',
    description: 'Extract a SIP header value from the inbound INVITE into a variable',
    handles: 'single',
  },
  tf_set_sip_header: {
    label: 'Set SIP Header',
    color: '#0e7490',
    description: 'Inject a SIP header into subsequent outgoing SIP messages for this channel',
    handles: 'single',
  },
  tf_set_caller_id: {
    label: 'Set Caller ID',
    color: '#0284c7',
    description: 'Override the outbound caller ID — accepts a literal number or any {{variable}}',
    handles: 'single',
  },
  tf_cancel_dial: {
    label: 'Cancel Dial',
    color: '#c2410c',
    description: 'Abort the outbound dial and send a message back to the agent',
    handles: 'none',
  },
  tf_script_pop: {
    label: 'Script Pop',
    color: '#0891b2',
    description: "Auto-open the CRM script flow on the answering agent's screen",
    handles: 'single',
  },
  tf_general_api_call: {
    label: 'API Call',
    color: '#6366f1',
    description: 'Call a saved General API Definition',
    handles: 'single',
  },
  tf_dtmf: {
    label: 'Send DTMF',
    color: '#ca8a04',
    description: 'Send a sequence of DTMF tones on the current call channel',
    handles: 'single',
  },
  tf_ivr_menu: {
    label: 'IVR Menu',
    color: '#0d9488',
    description: 'Play a prompt, collect DTMF, branch per option',
    handles: 'multi',
  },
  tf_record: {
    label: 'Record',
    color: '#e11d48',
    description: 'Start / stop / mask / unmask the call recording',
    handles: 'single',
  },
  tf_voicemail: {
    label: 'Voicemail',
    color: '#9333ea',
    description: 'Play a greeting, record the caller’s message, optionally email it',
    handles: 'multi',
  },
  tf_scheduled_callback: {
    label: 'Scheduled Callback',
    color: '#0891b2',
    description: 'Book a callback for a specific future date/time',
    // 'scheduled' (booked → Play confirmation → Hangup) + 'invalid_time' (parsed but past /
    // outside allowed window) + 'failed' (no number / unparseable date).
    handles: 'multi',
  },
  tf_whisper: {
    label: 'Whisper',
    color: '#7c3aed',
    description: "Play audio on the agent's ear only before bridging the caller",
    handles: 'single',
  },
  // ── Event listener nodes ──────────────────────────────────────────────────
  tf_on_agent_selected: {
    label: 'Agent Selected',
    color: '#6d28d9',
    description: 'Fires when an agent is assigned/presented with the call',
    handles: 'source-only',
  },
  tf_on_agent_answer: {
    label: 'Agent Answer',
    color: '#0369a1',
    description: 'Fires when the agent picks up — bridge is live at this point',
    handles: 'source-only',
  },
  tf_on_call_disconnected: {
    label: 'Call Disconnected',
    color: '#991b1b',
    description: 'Fires when the call ends — use for post-call actions',
    handles: 'source-only',
  },
  tf_on_custom_event: {
    label: 'Custom Event',
    color: '#92400e',
    description: 'Fires when a named custom event is emitted (e.g. from a script flow)',
    handles: 'source-only',
  },
  tf_event_wait: {
    label: 'Wait for Event',
    color: '#6d28d9',
    description: 'Pauses flow execution until a named event fires',
    handles: 'single',
  },
}

export function defaultTelNodeData(type: TelephonyNodeType): TelNodeData {
  switch (type) {
    case 'tf_check_block_list':
      return { label: 'Check Block List' }
    case 'tf_check_agent_availability':
      return { label: 'Check Agent Availability', campaignId: '' }
    case 'tf_reject':
      return { label: 'Reject Call', cause: 'busy' }
    case 'tf_answer':
      return { label: 'Answer Call' }
    case 'tf_hangup':
      return { label: 'Hang Up' }
    case 'tf_route_to_queue':
      return { label: 'Route to Queue', agentExtension: '' }
    case 'tf_transfer':
      return {
        label: 'Transfer',
        destinationType: 'campaign_queue',
        targetCampaignId: '', agentExtension: '', targetTelephonyFlowId: '',
        externalNumber: '', externalGatewayName: '',
        announceAudioFileId: '', announceTtsText: '', announceTtsVoice: 'kal',
        screenPopFlowId: '',
      }
    case 'tf_play':
      return {
        label: 'Play Audio',
        audioSource: 'file',
        audioFileId: '',
        ttsText: '',
        ttsVoice: 'kal',
        durationSeconds: 0,
        startOffsetSeconds: 0,
        rememberPosition: false,
        autoRestart: false,
        periodicAnnouncements: [],
        periodicAnnouncementIntervalSeconds: 30,
      }
    case 'tf_time_of_day':
      return {
        label: 'Time of Day',
        timezone: 'America/Chicago',
        windows: [
          { name: 'business_hours', windowType: 'weekly', days: [1, 2, 3, 4, 5], start: '08:00', end: '17:00' },
          { name: 'after_hours', windowType: 'weekly', days: [1, 2, 3, 4, 5], start: '17:00', end: '08:00' },
          { name: 'weekend', windowType: 'weekly', days: [0, 6], start: '00:00', end: '24:00' },
        ],
      }
    case 'tf_branch':
      return { label: 'Branch', condition: '' }
    case 'tf_end':
      return { label: 'End' }
    case 'tf_set_variable':
      return { label: 'Set Variable', assignments: [{ key: '', value: '' }] }
    case 'tf_get_sip_header':
      return { label: 'Get SIP Header', headerName: 'X-Original-ANI', variableName: 'custom_ani' }
    case 'tf_set_sip_header':
      return { label: 'Set SIP Header', sipHeaderName: '', sipHeaderValue: '' }
    case 'tf_set_caller_id':
      return { label: 'Set Caller ID', callerIdValue: '{{caller.ani}}' }
    case 'tf_cancel_dial':
      return { label: 'Cancel Dial', cancelMessage: 'This transfer number is only available during business hours.' }
    case 'tf_script_pop':
      return { label: 'Script Pop', flowId: '' }
    case 'tf_general_api_call':
      return { label: 'New API Call', apiEndpointId: '', apiDefinitionScope: 'tenant', apiDefinitionName: '', apiEndpointName: '', outputVariable: '', timeoutSeconds: 30 }
    case 'tf_dtmf':
      return { label: 'Send DTMF', digits: '', durationMs: 100, interDigitGapMs: 50, waitForCompletion: true }
    case 'tf_ivr_menu':
      return {
        label: 'IVR Menu',
        promptAudioFileId: '', invalidAudioFileId: '',
        minDigits: 1, maxDigits: 1, maxTries: 3,
        timeoutMs: 5000, interDigitTimeoutMs: 3000, terminators: '',
        options: [{ digit: '1', transition: 'option_1' }],
      }
    case 'tf_record':
      return { label: 'Record', action: 'start', maskFill: 'silence', recordLimitSeconds: 0 }
    case 'tf_voicemail':
      return {
        label: 'Voicemail',
        greetingAudioFileId: '', greetingTtsText: '', greetingTtsVoice: 'kal',
        beepEnabled: true, maxLengthSeconds: 120, maxSilenceSeconds: 5, minLengthSeconds: 2,
        deliveryEmailEnabled: false,
        deliveryEmailTo: '', deliveryEmailCc: '', deliveryEmailBcc: '',
        deliveryEmailFromName: '', deliveryEmailReplyTo: '',
        deliveryEmailSubject: 'New voicemail from {{caller.phone}}',
        deliveryEmailBodyHtml: '', deliveryAttachAudio: true,
      }
    case 'tf_scheduled_callback':
      return {
        label: 'Scheduled Callback',
        numberSource: 'ani', collectedVar: '',
        scheduledDateValue: '', scheduledTimeValue: '',
        targetFlowId: '', targetCampaignId: '',
        allowedDays: '', allowedStartTime: '', allowedEndTime: '',
        windowMinutes: 120, maxAttempts: 3, callerIdOverride: '',
      }
    case 'tf_whisper':
      return { label: 'Whisper', audioFileId: '' }
    case 'tf_on_agent_selected':
      return { label: 'Agent Selected' }
    case 'tf_on_agent_answer':
      return { label: 'Agent Answer' }
    case 'tf_on_call_disconnected':
      return { label: 'Call Disconnected' }
    case 'tf_on_custom_event':
      return { label: 'Custom Event', eventName: '' }
    case 'tf_event_wait':
      return { label: 'Wait for Event', eventName: 'agent_answer' }
  }
}
