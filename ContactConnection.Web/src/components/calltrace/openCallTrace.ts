import { useCallTraceStore, type TracePresetFilters } from '../../stores/callTraceStore'

/// Opens a new call trace popup pre-filled with the given filters. Never auto-starts —
/// the filter form is always shown first with a "Start Trace" button.
export function openCallTrace(preset?: TracePresetFilters) {
  useCallTraceStore.getState().openWindow(preset)
}
