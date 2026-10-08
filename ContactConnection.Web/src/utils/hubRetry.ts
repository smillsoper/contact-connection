import type { IRetryPolicy, RetryContext } from '@microsoft/signalr'

/**
 * Reconnect for as long as the page is open (S184). SignalR's default `withAutomaticReconnect()` tries four times over
 * about 40 s and then gives up for good — after an API restart or a longer network drop, a portal tab kept its
 * softphone registered but silently stopped receiving every push (screen pops, take-overs, script pops, coaching…)
 * until the page was reloaded. Quick retries first, then every 15 s.
 */
export const reconnectForever: IRetryPolicy = {
  nextRetryDelayInMilliseconds: (ctx: RetryContext) => [0, 2000, 5000, 10000][ctx.previousRetryCount] ?? 15000,
}
