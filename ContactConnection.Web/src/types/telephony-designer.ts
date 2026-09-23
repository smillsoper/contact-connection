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
  | 'tf_set_custom_field'
  | 'tf_get_custom_field'
  | 'tf_store_value'
  | 'tf_get_value'
  // Signal / media actions
  | 'tf_dtmf'
  | 'tf_ivr_menu'
  | 'tf_clear_hot_digit'
  | 'tf_secure_collect'
  | 'tf_data_collect'
  | 'tf_delay'
  | 'tf_repeat'
  | 'tf_record'
  | 'tf_voicemail'
  | 'tf_scheduled_callback'
  | 'tf_queue_callback'
  // Agent-selected event branch actions
  | 'tf_whisper'
  // Event listener nodes — independent entry points that fire on lifecycle events
  | 'tf_on_agent_selected'
  | 'tf_on_agent_answer'
  | 'tf_on_call_disconnected'
  | 'tf_on_custom_event'

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
  // tf_play, tf_whisper
  audioSource?: 'file' | 'tts'
  audioFileId?: string
  ttsText?: string
  ttsVoice?: string
  durationSeconds?: number
  startOffsetSeconds?: number
  autoRestart?: boolean
  periodicAnnouncements?: Array<{ fileId: string }>
  periodicAnnouncementIntervalSeconds?: number
  // tf_answer + tf_play — ms of silence played (and waited on) before proceeding / before the
  // prompt, to prime the RTP path so the first syllable isn't clipped. tf_answer defaults 300;
  // tf_play defaults 0 (relies on the answer-time prime). 0 disables.
  leadInSilenceMs?: number
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
  // tf_set_sip_header (shares `headerName` key with tf_get_sip_header)
  value?: string
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
  // tf_set_custom_field / tf_get_custom_field — write/read an existing Custom Field Definition's
  // value for the current call record. Shares `value` (tf_set_sip_header) for the Set node's
  // template and `variableName` (tf_get_sip_header) for the Get node's store-into field.
  // definitionFieldName/definitionDisplayLabel/definitionDataTypeName are denormalized display
  // fields (same pattern as apiDefinitionName/apiEndpointName above) so the canvas node doesn't
  // need a fresh lookup just to show the picked field.
  definitionId?: string
  definitionFieldName?: string
  definitionDisplayLabel?: string
  definitionDataTypeName?: string
  // tf_store_value / tf_get_value — generic tenant/client/campaign key-value store, free-form key
  // (variables allowed), deliberately separate from Custom Fields (no pre-defined schema). Shares
  // `value` and `variableName` with the SIP header nodes above.
  scope?: 'tenant' | 'client' | 'campaign'
  keyName?: string
  retention?: 'forever' | '1_hour' | '24_hours' | '1_week' | '1_month'
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
  /** Each maps an exact DTMF entry to a named transition (its own source handle on the canvas).
   * `phrases` (S148) are spoken alternatives that resolve to the same transition — voice
   * recognition, sync mode only (ignored when alwaysListen or maxDigits > 1), and only takes
   * effect when the tenant has an SttStreaming provider configured. */
  options?: { digit: string; transition: string; label?: string; phrases?: string[] }[]
  // tf_ivr_menu — hot-digit / async mode (S141). When true: arms a single-digit-only background
  // listener from `options` above and returns via "default" immediately, instead of running the
  // usual blocking play_and_get_digits capture. Every other tf_ivr_menu field above (prompt,
  // min/max/tries/timeout/terminators, no_match) is unused in this mode. Cleared automatically on
  // entering any synchronous capture node or a real agent/transfer bridge, or explicitly via the
  // "Clear DTMF Listener" node (tf_clear_hot_digit).
  alwaysListen?: boolean
  // tf_secure_collect — PCI guided DTMF capture. Ordered list of fields; each is one
  // play_and_get_digits step. Digits are AES-encrypted into call_records.sensitive_data and
  // exposed as {{secure.<key>}}. The recording is masked for the whole capture.
  fields?: {
    key: string
    promptAudioFileId?: string
    minDigits?: number
    maxDigits?: number
    terminator?: string
    validation?: 'none' | 'luhn' | 'expiry_mmyy' | 'cvv'
  }[]
  // tf_data_collect — like tf_ivr_menu minus options: collects a DTMF value (any digits, no
  // per-value branching) and writes it verbatim into `variableName` (shared field, declared
  // above under tf_get_sip_header). promptAudioFileId/invalidAudioFileId/minDigits/maxDigits/
  // maxTries/timeoutMs/interDigitTimeoutMs/terminators are all shared with tf_ivr_menu above.
  // Two fixed exits: "collected" (non-empty value) / "timeout" (nothing captured).
  // allowVoice additionally starts a concurrent free-form spoken-value capture (no phrase
  // matching — the first final transcript is taken verbatim) racing the DTMF collection; needs a
  // tenant SttStreaming provider configured, same as tf_ivr_menu's voice option.
  allowVoice?: boolean
  // numericOnly — strips non-digit characters (a vendor transcript may add punctuation, e.g. a
  // trailing period) and maps spelled-out single digit words to numerals before storing. DTMF
  // digits are already clean, so this only actually changes anything on the voice path.
  numericOnly?: boolean
  // tf_delay — pauses the flow for a duration before continuing. Literal ms as text, or a
  // {{variable}} template resolved the same way tf_set_caller_id/tf_set_sip_header values are.
  // (Named delayDurationMs, not durationMs, to avoid colliding with tf_dtmf's numeric field above.)
  delayDurationMs?: string
  // tf_repeat — bounded loop primitive. One entry (fed by both the upstream wire and the
  // tenant's own loop-back wire), two exits: 'repeat' (fires repeatCount - 1 times) and
  // 'finished' (fires once the counter reaches repeatCount).
  repeatCount?: number
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
  // targetCampaignId (campaign context for the answered leg's queue) is declared once under tf_transfer above.
  allowedDays?: string             // optional CSV of 0-6 (0=Sun) the callback may land on
  allowedStartTime?: string        // optional "HH:mm" earliest local time-of-day
  allowedEndTime?: string          // optional "HH:mm" latest local time-of-day
  windowMinutes?: number           // how long past the booked time the worker keeps trying
  maxAttempts?: number             // outbound attempts before the callback is abandoned
  callerIdOverride?: string         // outbound CID; blank = DNIS the caller dialed. Literal or {{variable}} (frozen at request time)
  // tf_queue_callback — virtual hold (keeps queue position, dials the caller back when an agent is free)
  // (numberSource / collectedVar / maxAttempts shared with tf_scheduled_callback above)
  connectAudioFileId?: string       // played to the caller when the callback connects, before the bridge; blank = built-in prompt
  // tf_whisper — audioSource / audioFileId / ttsText / ttsVoice reused from tf_play
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
  tf_set_custom_field: {
    label: 'Set Call Record Value',
    color: '#65a30d',
    description: 'Save a value into a defined custom field for this call',
    handles: 'single',
  },
  tf_get_custom_field: {
    label: 'Get Call Record Value',
    color: '#4d7c0f',
    description: "Read a defined custom field's value into a flow variable",
    handles: 'single',
  },
  tf_store_value: {
    label: 'Store Value',
    color: '#0e7490',
    description: 'Save a free-form value scoped to the tenant, client, or campaign',
    handles: 'single',
  },
  tf_get_value: {
    label: 'Get Value',
    color: '#155e75',
    description: 'Read a stored value back into a flow variable',
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
    description: 'Play a prompt, collect DTMF, branch per option — or, in hot-digit mode, silently listen in the background',
    handles: 'multi',
  },
  tf_clear_hot_digit: {
    label: 'Clear DTMF Listener',
    color: '#4b5563',
    description: 'Explicitly disarm an active hot-digit listener (IVR Menu in "always listen" mode)',
    handles: 'single',
  },
  tf_secure_collect: {
    label: 'Secure Collect',
    color: '#be123c',
    description: 'PCI guided DTMF capture (card / CVV / SSN) — masks the recording during entry',
    // 'collected' (all fields captured + encrypted) + 'failed' (bad config / validation / retries)
    // + 'timeout' (no entry).
    handles: 'multi',
  },
  tf_data_collect: {
    label: 'Data Collect',
    color: '#0d9488',
    description: 'Play a prompt, collect a value (DTMF and/or voice), and store it in a variable',
    // 'collected' (non-empty value captured) + 'timeout' (nothing captured).
    handles: 'multi',
  },
  tf_delay: {
    label: 'Delay',
    color: '#65a30d',
    description: 'Pause the flow for a duration before continuing',
    handles: 'single',
  },
  tf_repeat: {
    label: 'Repeat',
    color: '#a16207',
    description: 'Loop back to this node up to N times, then fall through',
    // 'repeat' (loop body) + 'finished' (count reached).
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
  tf_queue_callback: {
    label: 'Queue Callback',
    color: '#0e7490',
    description: 'Virtual hold — keep queue position, call the caller back when an agent is free',
    // 'queued' (opted in → Play "we'll call you back" → Hangup) + 'failed' (no usable number).
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
      return { label: 'Answer Call', leadInSilenceMs: 300 }
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
        leadInSilenceMs: 0,
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
      return { label: 'Set SIP Header', headerName: '', value: '' }
    case 'tf_set_caller_id':
      return { label: 'Set Caller ID', callerIdValue: '{{caller.ani}}' }
    case 'tf_cancel_dial':
      return { label: 'Cancel Dial', cancelMessage: 'This transfer number is only available during business hours.' }
    case 'tf_script_pop':
      return { label: 'Script Pop', flowId: '' }
    case 'tf_general_api_call':
      return { label: 'New API Call', apiEndpointId: '', apiDefinitionScope: 'tenant', apiDefinitionName: '', apiEndpointName: '', outputVariable: '', timeoutSeconds: 30 }
    case 'tf_set_custom_field':
      return { label: 'Set Call Record Value', definitionId: '', definitionFieldName: '', definitionDisplayLabel: '', definitionDataTypeName: '', value: '' }
    case 'tf_get_custom_field':
      return { label: 'Get Call Record Value', definitionId: '', definitionFieldName: '', definitionDisplayLabel: '', definitionDataTypeName: '', variableName: '' }
    case 'tf_store_value':
      return { label: 'Store Value', scope: 'campaign', keyName: '', value: '', retention: 'forever' }
    case 'tf_get_value':
      return { label: 'Get Value', scope: 'campaign', keyName: '', variableName: '' }
    case 'tf_dtmf':
      return { label: 'Send DTMF', digits: '', durationMs: 100, interDigitGapMs: 50, waitForCompletion: true }
    case 'tf_ivr_menu':
      return {
        label: 'IVR Menu',
        promptAudioFileId: '', invalidAudioFileId: '',
        minDigits: 1, maxDigits: 1, maxTries: 3,
        timeoutMs: 5000, interDigitTimeoutMs: 3000, terminators: '',
        options: [{ digit: '1', transition: 'option_1' }],
        alwaysListen: false,
      }
    case 'tf_clear_hot_digit':
      return { label: 'Clear DTMF Listener' }
    case 'tf_secure_collect':
      return {
        label: 'Secure Collect',
        fields: [
          { key: 'pan', minDigits: 13, maxDigits: 19, terminator: '#', validation: 'luhn' },
          { key: 'expiry', minDigits: 4, maxDigits: 4, terminator: 'none', validation: 'expiry_mmyy' },
          { key: 'cvv', minDigits: 3, maxDigits: 4, terminator: 'none', validation: 'cvv' },
        ],
        invalidAudioFileId: '',
        maxTries: 3, timeoutMs: 12000, interDigitTimeoutMs: 5000,
      }
    case 'tf_data_collect':
      return {
        label: 'Data Collect',
        promptAudioFileId: '', invalidAudioFileId: '',
        minDigits: 1, maxDigits: 20, maxTries: 3,
        timeoutMs: 6000, interDigitTimeoutMs: 3000, terminators: '',
        variableName: '', allowVoice: false, numericOnly: false,
      }
    case 'tf_delay':
      return { label: 'Delay', delayDurationMs: '2000' }
    case 'tf_repeat':
      return { label: 'Repeat', repeatCount: 3 }
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
    case 'tf_queue_callback':
      return {
        label: 'Queue Callback',
        numberSource: 'ani', collectedVar: '',
        maxAttempts: 3, connectAudioFileId: '',
      }
    case 'tf_whisper':
      return { label: 'Whisper', audioSource: 'file', audioFileId: '', ttsText: '', ttsVoice: 'kal' }
    case 'tf_on_agent_selected':
      return { label: 'Agent Selected' }
    case 'tf_on_agent_answer':
      return { label: 'Agent Answer' }
    case 'tf_on_call_disconnected':
      return { label: 'Call Disconnected' }
    case 'tf_on_custom_event':
      return { label: 'Custom Event', eventName: '' }
  }
}
