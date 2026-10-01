// Softphone audio devices (S169). The agent's chosen microphone and speaker, remembered per browser.
// An empty id means "system default". Used by SoftphonePanel for every call it opens a mic for,
// and to route every call's audio element to the chosen speaker.

const INPUT_KEY = 'cc.audio.input'
const OUTPUT_KEY = 'cc.audio.output'

function read(key: string): string {
  try { return localStorage.getItem(key) ?? '' } catch { return '' }
}

function write(key: string, value: string) {
  try {
    if (value) localStorage.setItem(key, value)
    else localStorage.removeItem(key)
  } catch { /* storage unavailable — the choice lasts until reload */ }
  listeners.forEach((l) => l())
}

const listeners = new Set<() => void>()

/** Called whenever the chosen microphone or speaker changes. Returns an unsubscribe function. */
export function onAudioDevicesChanged(listener: () => void): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

export const getInputDeviceId = () => read(INPUT_KEY)
export const getOutputDeviceId = () => read(OUTPUT_KEY)
export const setInputDeviceId = (id: string) => write(INPUT_KEY, id)
export const setOutputDeviceId = (id: string) => write(OUTPUT_KEY, id)

/** Mic constraints for the chosen input. `ideal`, not `exact`, so an unplugged headset falls back to
 *  the default mic instead of failing the call. */
export function micConstraints(): MediaStreamConstraints {
  const id = getInputDeviceId()
  return { audio: id ? { deviceId: { ideal: id } } : true, video: false }
}

/** Browsers without setSinkId (e.g. some Safari / Firefox versions) always play to the system default. */
export const canChooseSpeaker = () =>
  typeof HTMLMediaElement !== 'undefined' && 'setSinkId' in HTMLMediaElement.prototype

/** Routes an audio element to the chosen speaker (or back to the default). */
export async function applySpeaker(el: HTMLMediaElement | null, deviceId = getOutputDeviceId()) {
  if (!el || !canChooseSpeaker()) return
  try {
    await (el as HTMLMediaElement & { setSinkId(id: string): Promise<void> }).setSinkId(deviceId)
  } catch { /* device gone — the element keeps its previous output */ }
}
