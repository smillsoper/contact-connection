import { useEffect } from 'react'
import { useCallStore } from '../stores/callStore'
import { useIntercomStore } from '../stores/intercomStore'
import { connectExtension, requestPortalFocus, useExtensionStore } from '../lib/extensionBridge'
import { EXTENSION, versionAtLeast } from '../config/extension'
import {
  campaignRecordsScreen, loadRecordingCampaigns, startScreenShare, startSegment, stopScreenShare, stopSegment,
  useScreenShareStore,
} from '../lib/screenRecorder'

/**
 * Agent portal glue for the browser extension and screen recording (S183):
 *  • brings this tab forward when work lands here (call offer / auto-connect / ringing / supervisor call — script pops and
 *    take overs ask from where they happen);
 *  • records each call on a screen-recorded campaign from the agent's once-per-shift screen share;
 *  • shows the agent a slim bar: share status, and a nudge when the extension is missing.
 */
export default function ScreenRecordingBar() {
  const share = useScreenShareStore()
  const extension = useExtensionStore()

  useEffect(() => {
    connectExtension()
    void loadRecordingCampaigns()
  }, [])

  // Focus + recording follow the call.
  useEffect(() => useCallStore.subscribe((s, prev) => {
    if (s.callStatus !== prev.callStatus && (s.callStatus === 'queued' || s.callStatus === 'auto-connecting' || s.callStatus === 'ringing'))
      requestPortalFocus(s.callStatus)
    // Refresh which campaigns record, as each call is offered — a setting changed mid-shift applies to the next call.
    if (s.callStatus !== prev.callStatus && (s.callStatus === 'queued' || s.callStatus === 'auto-connecting')) void loadRecordingCampaigns()

    const recordingCall = useScreenShareStore.getState().recordingCallId
    const shouldRecord = s.callStatus === 'on-call' && !!s.callRecordId && campaignRecordsScreen(s.campaignId)
    if (shouldRecord && recordingCall !== s.callRecordId) startSegment(s.callRecordId!)
    else if (!shouldRecord && recordingCall && (s.callStatus !== 'on-call' || s.callRecordId !== recordingCall)) void stopSegment('call ended')
  }), [])

  // A supervisor calling this agent.
  useEffect(() => useIntercomStore.subscribe((s, prev) => {
    if (s.call?.role === 'callee' && !prev.call) requestPortalFocus('supervisor call')
  }), [])

  // Recording campaigns loaded after the call connected (or the share started mid-call) — catch up.
  useEffect(() => {
    const s = useCallStore.getState()
    if (share.status === 'sharing' && s.callStatus === 'on-call' && s.callRecordId && campaignRecordsScreen(s.campaignId)
      && share.recordingCallId !== s.callRecordId)
      startSegment(s.callRecordId)
  }, [share.status, share.campaignIds, share.recordingCallId])

  const needed = (share.campaignIds?.length ?? 0) > 0
  if (!needed && extension.installed && versionAtLeast(extension.version, EXTENSION.minimumVersion)) return null

  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-1.5 text-xs border-b border-gray-800 bg-gray-950 shrink-0">
      {needed && (
        share.status === 'sharing' ? (
          <span className="flex items-center gap-2 text-emerald-300">
            <span className={`inline-block w-2 h-2 rounded-full ${share.recordingCallId ? 'bg-red-500 animate-pulse' : 'bg-emerald-400'}`} />
            {share.recordingCallId ? 'Recording your screen for this call' : 'Screen shared — calls on recorded campaigns are recorded'}
            {share.otherScreen && <span className="text-amber-300">· you shared a different screen from this window's, so clicks won't be marked</span>}
            <button onClick={stopScreenShare} className="text-gray-500 hover:text-white underline">Stop sharing</button>
          </span>
        ) : (
          <span className="flex items-center gap-2 text-amber-300">
            <span className="inline-block w-2 h-2 rounded-full bg-amber-400" />
            {share.status === 'ended' ? 'Screen sharing stopped.' : 'Some of your campaigns record your screen.'}
            <button onClick={() => void startScreenShare()} disabled={share.status === 'requesting'}
              className="bg-amber-600 hover:bg-amber-500 disabled:opacity-50 text-white rounded px-2 py-0.5 font-medium">
              {share.status === 'requesting' ? 'Choose a screen…' : 'Share your screen'}
            </button>
            <span className="text-gray-500">Choose the entire screen this window is on.</span>
          </span>
        )
      )}
      {share.error && <span className="text-red-400">{share.error}</span>}
      {share.lastProblem && <span className="text-red-400">{share.lastProblem}</span>}
      {!extension.installed && (
        <span className="text-gray-500 ml-auto">
          The ContactConnection Agent extension isn't installed — calls won't bring this tab forward{needed ? ' and clicks won\'t be marked in recordings' : ''}.{' '}
          <a href="/extension" target="_blank" rel="noreferrer" className="text-sky-400 hover:text-sky-300 underline">Install it</a>
        </span>
      )}
      {extension.installed && !versionAtLeast(extension.version, EXTENSION.minimumVersion) && (
        <span className="text-amber-300 ml-auto">
          Your ContactConnection Agent extension is out of date.{' '}
          <a href="/extension" target="_blank" rel="noreferrer" className="text-sky-400 hover:text-sky-300 underline">Update it</a>
        </span>
      )}
    </div>
  )
}
