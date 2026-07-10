import { createPortal } from 'react-dom'
import { useCallTraceStore } from '../../stores/callTraceStore'
import { useCallTraceHub } from '../../hooks/useCallTraceHub'
import CallTraceWindow from './CallTraceWindow'

/// Mounted once at the app root — renders every open trace popup (there can be
/// several simultaneously, each with independent filters) and owns the shared
/// SignalR connection that feeds them all.
export default function CallTraceWindowsPortal() {
  const windows = useCallTraceStore((s) => s.windows)
  useCallTraceHub()

  if (windows.length === 0) return null

  return createPortal(
    <>
      {windows.map((win) => (
        <CallTraceWindow key={win.id} win={win} />
      ))}
    </>,
    document.body,
  )
}
