import type { ChartType } from '../../types/dashboard'

/**
 * A miniature of each chart type, in its own colour (William's idea, S184) — the chart picker reads at a glance.
 * 40×28 drawing; `active` brightens it.
 */
export const CHART_TYPE_COLOR: Record<ChartType, string> = {
  line: '#38bdf8', area: '#2dd4bf', bar: '#818cf8', hbar: '#a78bfa', stacked: '#f59e0b',
  combo: '#f472b6', pie: '#34d399', donut: '#a3e635', heatmap: '#fb923c', funnel: '#22d3ee',
}

export default function ChartTypeIcon({ type, active = false, size = 40 }: { type: ChartType; active?: boolean; size?: number }) {
  const c = CHART_TYPE_COLOR[type]
  const dim = active ? 1 : 0.75
  const axis = <path d="M4 3v21h33" stroke="#4b5563" strokeWidth={1} fill="none" />
  let body: React.ReactNode
  switch (type) {
    case 'line':
      body = <>{axis}<polyline points="6,19 13,12 19,15 26,7 34,10" fill="none" stroke={c} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" /></>
      break
    case 'area':
      body = <>{axis}<path d="M6 19 L13 12 L19 15 L26 7 L34 10 L34 23 L6 23 Z" fill={c} fillOpacity={0.35} />
        <polyline points="6,19 13,12 19,15 26,7 34,10" fill="none" stroke={c} strokeWidth={1.8} strokeLinejoin="round" /></>
      break
    case 'bar':
      body = <>{axis}{[[7, 13], [14, 8], [21, 15], [28, 5]].map(([x, y]) => <rect key={x} x={x} y={y} width={5} height={23 - y} rx={1} fill={c} />)}</>
      break
    case 'hbar':
      body = <><path d="M5 2v23" stroke="#4b5563" strokeWidth={1} />{[[4, 26], [10, 18], [16, 22], [22, 11]].map(([y, w]) => <rect key={y} x={5} y={y} width={w} height={4} rx={1} fill={c} />)}</>
      break
    case 'stacked':
      body = <>{axis}{[[7, 9, 14], [14, 5, 11], [21, 11, 16], [28, 7, 13]].map(([x, top, mid]) => (
        <g key={x}><rect x={x} y={top} width={5} height={mid - top} fill={c} fillOpacity={0.55} /><rect x={x} y={mid} width={5} height={23 - mid} fill={c} /></g>))}</>
      break
    case 'combo':
      body = <>{axis}{[[7, 14], [14, 10], [21, 16], [28, 9]].map(([x, y]) => <rect key={x} x={x} y={y} width={5} height={23 - y} rx={1} fill={c} fillOpacity={0.55} />)}
        <polyline points="9,9 16,6 23,11 31,4" fill="none" stroke={c} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" /></>
      break
    case 'pie':
      body = <><circle cx={20} cy={14} r={11} fill={c} fillOpacity={0.4} /><path d="M20 14 L20 3 A11 11 0 0 1 30.5 17.5 Z" fill={c} />
        <path d="M20 14 L30.5 17.5 A11 11 0 0 1 14 23.3 Z" fill={c} fillOpacity={0.7} /></>
      break
    case 'donut':
      body = <><circle cx={20} cy={14} r={9} fill="none" stroke={c} strokeOpacity={0.35} strokeWidth={5} />
        <path d="M20 5 A9 9 0 0 1 28.6 16.7" fill="none" stroke={c} strokeWidth={5} /></>
      break
    case 'heatmap':
      body = <>{[0, 1, 2, 3].flatMap((r) => [0, 1, 2, 3, 4, 5].map((col) => {
        const o = [0.2, 0.45, 0.8, 1, 0.6, 0.3][(col + r * 2) % 6]
        return <rect key={`${r}-${col}`} x={4 + col * 5.5} y={3 + r * 5.5} width={4.6} height={4.6} rx={0.8} fill={c} fillOpacity={o} />
      }))}</>
      break
    case 'funnel':
      body = <>{[[4, 32], [8, 24], [12, 16], [16, 8]].map(([x, w], i) => <rect key={x} x={x} y={3 + i * 6} width={w} height={5} rx={1} fill={c} fillOpacity={1 - i * 0.18} />)}</>
      break
  }
  return (
    <svg width={size} height={size * 0.7} viewBox="0 0 40 28" style={{ opacity: dim }} aria-hidden>
      {body}
    </svg>
  )
}
