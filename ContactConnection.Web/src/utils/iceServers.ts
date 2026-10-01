import { api } from '../api/client'

// TURN relay for the softphone (S171). FreeSWITCH (in Docker) only offers browsers its Docker address,
// which only the Docker host PC can reach; every other agent relays audio through our TURN server. The
// API issues short-lived credentials to the signed-in agent. With none configured the list is empty and
// calls behave as before.

interface IceServersResponse { iceServers: RTCIceServer[]; expiresAt: string | null }

let current: RTCIceServer[] = []
let expiresAt = 0
let inflight: Promise<void> | null = null

/** Fetches (or refreshes, an hour before expiry) the TURN credentials. Safe to call repeatedly. */
export function loadIceServers(): Promise<void> {
  if (Date.now() < expiresAt - 60 * 60 * 1000 && current.length > 0) return Promise.resolve()
  inflight ??= api.get<IceServersResponse>('/api/v1/softphone/ice-servers')
    .then((r) => {
      current = r.iceServers ?? []
      expiresAt = r.expiresAt ? Date.parse(r.expiresAt) : 0
    })
    .catch(() => { /* keep whatever we had — calls still work on the Docker host without TURN */ })
    .finally(() => { inflight = null })
  return inflight
}

/** RTCPeerConnection config for every softphone call / answer. */
export function rtcConfig(): RTCConfiguration {
  // Refresh in the background when close to expiry; this call uses what we already have.
  void loadIceServers()
  return { iceServers: current }
}
