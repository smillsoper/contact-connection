// Shared API category/sub-type metadata for the Portal and Admin API Definitions/Preferences
// UIs. Single source of truth on the frontend — was previously duplicated per-component, which
// meant a new backend ApiSubType (tts_streaming) silently didn't show up in the picker until
// someone remembered to also update this file by hand. Still has to be kept in sync with the
// backend's ApiSubType/ApiCategory static classes manually (no live endpoint for this — unlike
// TTS providers, which come from a real registry via /tts-providers).

export const API_CATEGORIES = [
  { value: 'address',     label: 'Address' },
  { value: 'order',       label: 'Order' },
  { value: 'fulfillment', label: 'Fulfillment' },
  { value: 'media',       label: 'Media' },
  { value: 'general',     label: 'General' },
]

export const API_CATEGORY_LABELS: Record<string, string> = Object.fromEntries(
  API_CATEGORIES.map((c) => [c.value, c.label])
)

export const API_SUB_TYPES = [
  { value: 'address_validation',            label: 'Address Validation',             category: 'address' },
  { value: 'zip_code_lookup',               label: 'ZIP Code Lookup',                category: 'address' },
  { value: 'realtime_address_autocomplete', label: 'Realtime Address Autocomplete',  category: 'address' },
  { value: 'order_submit',                  label: 'Submit Order',                   category: 'order' },
  { value: 'order_lookup',                  label: 'Lookup',                         category: 'order' },
  { value: 'order_cancel',                  label: 'Cancel',                         category: 'order' },
  { value: 'fulfillment_tracking',          label: 'Tracking',                       category: 'fulfillment' },
  { value: 'fulfillment_cancel',            label: 'Cancel',                         category: 'fulfillment' },
  { value: 'fulfillment_return',            label: 'Return',                         category: 'fulfillment' },
  { value: 'fulfillment_submit',            label: 'Submit',                         category: 'fulfillment' },
  { value: 'tfn_assignment_add',            label: 'Add TFN Assignment',             category: 'media' },
  { value: 'tfn_assignment_update',         label: 'Update TFN Assignment',          category: 'media' },
  { value: 'tfn_assignment_delete',         label: 'Delete TFN Assignment',          category: 'media' },
  { value: 'campaign_results',              label: 'Campaign Results',               category: 'media' },
  { value: 'tts_streaming',                 label: 'TTS Streaming',                  category: 'media' },
]

export const API_SUB_TYPE_LABELS: Record<string, string> = Object.fromEntries(
  API_SUB_TYPES.map((s) => [s.value, s.label])
)

export const API_CATEGORY_BADGE_COLORS: Record<string, string> = {
  address:     'bg-sky-900/50 text-sky-300',
  order:       'bg-emerald-900/50 text-emerald-300',
  fulfillment: 'bg-amber-900/50 text-amber-300',
  media:       'bg-violet-900/50 text-violet-300',
  general:     'bg-indigo-900/50 text-indigo-300',
}

export const API_SUB_TYPE_BADGE_COLORS: Record<string, string> = {
  address_validation:            'bg-sky-900/40 text-sky-300',
  zip_code_lookup:               'bg-indigo-900/40 text-indigo-300',
  realtime_address_autocomplete: 'bg-violet-900/40 text-violet-300',
  order_submit:                  'bg-emerald-900/40 text-emerald-300',
  order_lookup:                  'bg-teal-900/40 text-teal-300',
  order_cancel:                  'bg-red-900/40 text-red-300',
  fulfillment_tracking:          'bg-amber-900/40 text-amber-300',
  fulfillment_cancel:            'bg-orange-900/40 text-orange-300',
  fulfillment_return:            'bg-yellow-900/40 text-yellow-300',
  fulfillment_submit:            'bg-lime-900/40 text-lime-300',
  tfn_assignment_add:            'bg-pink-900/40 text-pink-300',
  tfn_assignment_update:         'bg-fuchsia-900/40 text-fuchsia-300',
  tfn_assignment_delete:         'bg-rose-900/40 text-rose-300',
  campaign_results:              'bg-purple-900/40 text-purple-300',
  tts_streaming:                 'bg-cyan-900/40 text-cyan-300',
}

// Display labels for known TTS provider keys — falls back to the raw key for anything not
// listed here (e.g. a newly-added provider before someone gets around to naming it nicely).
export const TTS_PROVIDER_LABELS: Record<string, string> = {
  azure: 'Azure Speech',
  elevenlabs: 'ElevenLabs',
}
