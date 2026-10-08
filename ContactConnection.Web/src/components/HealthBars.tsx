import { describeHealth, useCallHealthStore, type AgentHealth } from '../lib/callHealth'
import { MicIcon } from './icons/Icons'

/**
 * Connection health (S183): three signal bars coloured by the call's grade — green good, amber fair, red poor — or grey
 * when not on a call. A red mic replaces them when no audio is coming from the agent's microphone.
 */
export function HealthBars({ health, size = 14 }: { health: AgentHealth | null | undefined; size?: number }) {
  if (!health) return null
  const title = describeHealth(health)
  if (health.onCall && health.micSilent)
    return <span title={title} className="inline-flex text-red-400 animate-pulse"><MicIcon size={size} /></span>
  const bars = !health.onCall ? 0 : health.grade === 'good' ? 3 : health.grade === 'fair' ? 2 : health.grade === 'poor' ? 1 : 0
  const color = !health.onCall ? '#4b5563' : health.grade === 'good' ? '#22c55e' : health.grade === 'fair' ? '#f59e0b' : health.grade === 'poor' ? '#ef4444' : '#6b7280'
  return (
    <span title={title} className="inline-flex">
      <svg width={size} height={size} viewBox="0 0 14 14" aria-label={title}>
        {[0, 1, 2].map((i) => (
          <rect key={i} x={1 + i * 4.5} y={10 - i * 4} width={3} height={3 + i * 4} rx={0.8}
            fill={i < bars ? color : 'none'} stroke={i < bars ? color : '#4b5563'} strokeWidth={1} />
        ))}
      </svg>
    </span>
  )
}

/** The agent's own softphone: their call's grade, and a clear warning when the mic is silent or the line is poor. */
export function CallHealthLine() {
  const h = useCallHealthStore((s) => s.health)
  if (!h?.onCall) return null
  const label = h.micSilent ? 'No audio from your microphone — check your headset isn’t muted'
    : h.grade === 'poor' ? 'Poor connection — the caller may hear you breaking up'
    : h.grade === 'fair' ? 'Connection is fair'
    : h.grade === 'good' ? 'Good connection' : 'Checking connection…'
  const tone = h.micSilent || h.grade === 'poor' ? 'text-red-300' : h.grade === 'fair' ? 'text-amber-300' : 'text-gray-400'
  return (
    <div className={`flex items-center gap-2 px-1 text-xs ${tone}`} title={describeHealth(h)}>
      <HealthBars health={h} />
      <span>{label}</span>
    </div>
  )
}
