export interface TracePresetFilters {
  campaignId?: string
  flowId?: string
  dnis?: string
}

const WINDOW_WIDTH = 420
const WINDOW_HEIGHT = 640
const CASCADE_STEP = 32
const CASCADE_WRAP = 6

let cascadeIndex = 0

/// Opens a call trace in a genuine separate, movable browser window (not an in-page
/// modal) so it survives navigating the rest of the app and several can be open at
/// once. Same-origin, so the popup shares localStorage (auth token) with the opener —
/// it authenticates itself, no state needs to be passed except the preset filters.
/// Never auto-starts — the popup always shows the filter form first.
export function openCallTrace(preset?: TracePresetFilters) {
  const params = new URLSearchParams()
  if (preset?.campaignId) params.set('campaignId', preset.campaignId)
  if (preset?.flowId) params.set('flowId', preset.flowId)
  if (preset?.dnis) params.set('dnis', preset.dnis)

  const offset = (cascadeIndex++ % CASCADE_WRAP) * CASCADE_STEP
  const left = Math.round((window.screen.availWidth - WINDOW_WIDTH) / 2) + offset
  const top = 80 + offset

  const features = [
    `width=${WINDOW_WIDTH}`,
    `height=${WINDOW_HEIGHT}`,
    `left=${left}`,
    `top=${top}`,
    'resizable=yes',
    'scrollbars=yes',
    'toolbar=no',
    'menubar=no',
    'location=no',
    'status=no',
  ].join(',')

  // A unique window name ensures each call opens a NEW window rather than reusing
  // one — multiple simultaneous traces with different filters is a hard requirement.
  const name = `call-trace-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
  window.open(`/call-trace-window?${params.toString()}`, name, features)
}
