import { useEffect, useState } from 'react'
import { PlayIcon } from '../icons/Icons'

/**
 * Plays a call recording fetched with the viewer's own sign-in (S181) — the bytes come back through an authenticated
 * fetch and play from a blob URL, so no token ever goes into a media URL.
 */
export default function RecordingPlayer({ load }: { load: () => Promise<Response> }) {
  const [state, setState] = useState<'idle' | 'loading' | 'ready' | 'error'>('idle')
  const [url, setUrl] = useState<string | null>(null)
  const [isVideo, setIsVideo] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  useEffect(() => () => { if (url) URL.revokeObjectURL(url) }, [url])

  async function play() {
    setState('loading'); setMessage(null)
    try {
      const res = await load()
      if (res.status === 200 || res.status === 206) {
        const blob = await res.blob()
        // A call with a screen recording (S183) is merged into an mp4 — show it as video.
        setIsVideo(blob.type.startsWith('video/'))
        setUrl(URL.createObjectURL(blob))
        setState('ready')
        return
      }
      const body = await res.json().catch(() => ({} as { status?: string }))
      setMessage(res.status === 202 ? 'The recording is still being prepared — try again in a minute.'
        : res.status === 410 ? 'This recording has been deleted under the retention policy.'
        : res.status === 403 ? "You don't have access to recordings."
        : body.status === 'none' ? 'No recording was made for this call.' : 'The recording is not available.')
      setState('error')
    } catch {
      setMessage('Could not load the recording.')
      setState('error')
    }
  }

  if (state === 'ready' && url) return isVideo
    ? <video controls autoPlay src={url} className="w-full max-h-[70vh] rounded bg-black" />
    : <audio controls autoPlay src={url} className="w-full" />
  return (
    <div className="flex items-center gap-3">
      <button onClick={play} disabled={state === 'loading'}
        className="text-xs bg-indigo-600 hover:bg-indigo-500 text-white px-3 py-1.5 rounded disabled:opacity-50">
        {state === 'loading' ? 'Loading…' : <><PlayIcon size={11} className="inline -mt-0.5 mr-1" />Play recording</>}
      </button>
      {message && <span className="text-xs text-gray-400">{message}</span>}
    </div>
  )
}
