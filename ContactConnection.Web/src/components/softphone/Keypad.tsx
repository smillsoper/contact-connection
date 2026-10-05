// Phone keypad (S179, manual outbound). Number entry in the Place call panel, and DTMF on a connected call —
// navigating the far end's IVR, entering an extension, warm/consult transfers included.

const KEYS: { d: string; sub?: string }[] = [
  { d: '1' }, { d: '2', sub: 'ABC' }, { d: '3', sub: 'DEF' },
  { d: '4', sub: 'GHI' }, { d: '5', sub: 'JKL' }, { d: '6', sub: 'MNO' },
  { d: '7', sub: 'PQRS' }, { d: '8', sub: 'TUV' }, { d: '9', sub: 'WXYZ' },
  { d: '*' }, { d: '0', sub: '+' }, { d: '#' },
]

export default function Keypad({ onDigit, compact = false }: { onDigit: (digit: string) => void; compact?: boolean }) {
  return (
    <div className="grid grid-cols-3 gap-1.5">
      {KEYS.map(({ d, sub }) => (
        <button
          key={d}
          type="button"
          onClick={() => onDigit(d)}
          className={`rounded-lg bg-gray-800 hover:bg-gray-700 active:bg-gray-600 text-white flex flex-col items-center justify-center transition-colors ${compact ? 'h-9' : 'h-11'}`}
        >
          <span className="text-sm font-semibold leading-none">{d}</span>
          {sub && <span className="text-[8px] text-gray-500 leading-none mt-0.5 tracking-wider">{sub}</span>}
        </button>
      ))}
    </div>
  )
}
