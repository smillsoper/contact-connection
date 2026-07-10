import { useCallTraceStore, type TraceWindow } from '../../stores/callTraceStore'
import CallTraceFilterForm from './CallTraceFilterForm'
import CallTraceRunningView from './CallTraceRunningView'

interface Props {
  win: TraceWindow
}

export default function CallTraceWindow({ win }: Props) {
  const closeWindow = useCallTraceStore((s) => s.closeWindow)
  const focusWindow = useCallTraceStore((s) => s.focusWindow)

  return (
    <div
      className="fixed w-96 bg-gray-900 border border-gray-700 rounded-xl shadow-2xl flex flex-col z-50"
      style={{ left: win.left, top: win.top, maxHeight: '70vh' }}
      onMouseDown={() => focusWindow(win.id)}
    >
      <div className="flex items-center justify-between px-4 py-2.5 border-b border-gray-800 shrink-0 cursor-default">
        <span className="text-sm font-semibold text-white">Call Trace</span>
        <button
          onClick={() => closeWindow(win.id)}
          className="text-gray-500 hover:text-gray-300 text-lg leading-none transition-colors"
        >
          ×
        </button>
      </div>

      <div className="flex-1 min-h-0 overflow-hidden">
        {win.status === 'configuring' ? (
          <CallTraceFilterForm windowId={win.id} preset={win.preset} />
        ) : (
          <CallTraceRunningView window={win} />
        )}
      </div>
    </div>
  )
}
