import { ControlButton } from '@xyflow/react'

interface CanvasSelectionToggleProps {
  active: boolean
  onToggle: () => void
}

// Custom button rendered inside React Flow's <Controls> panel (must be a direct child of
// <Controls> to inherit its dark-theme styling). Toggles between the default pan-on-drag canvas
// behavior and a box-selection mode (drag on empty canvas draws a selection rectangle instead of
// panning), so multi-selecting nodes for move/cut/copy/paste doesn't require memorizing a
// keyboard modifier.
export default function CanvasSelectionToggle({ active, onToggle }: CanvasSelectionToggleProps) {
  return (
    <ControlButton
      onClick={onToggle}
      title={active ? 'Selection mode (drag to box-select) — click to switch back to pan' : 'Click to enable selection mode (drag to box-select nodes)'}
      className={active ? 'cc-selection-toggle-active' : undefined}
      style={active ? { background: '#2563eb' } : undefined}
    >
      <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.75">
        <rect x="3" y="3" width="18" height="18" rx="2" strokeDasharray="3 2.5" />
        <rect x="8" y="8" width="8" height="8" rx="1" fill="currentColor" stroke="none" />
      </svg>
    </ControlButton>
  )
}
