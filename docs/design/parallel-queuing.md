# Parallel Queuing — Priority Agent Tiers (Design, S163 — for review)

Implements ARCHITECTURE.md §15 for the case that drove it: Life Seasons' **Alpha** team (premium,
higher-commission agents) competing with the regular pool on NeuroQ TV / NeuroQ SF TV / NeuroQ Elite /
My Best Heart / Joint Food, plus **Elite** routing driven by an external router (RingSquared).
Source of the requirements: Stephen (built the CXone equivalent at TMS) and the CXone exports — see
`docs/integrations/neuroq-cxone-callflow.md`.

## Decisions (confirmed with Stephen)

| # | Decision |
|---|---|
| 1 | **Pure priority** by default: a call waits for every tier at once; the highest tier with an eligible *available* agent gets it. Optional per-assignment **exclusive window** (hold for that tier N seconds even if lower tiers are free) — off by default. |
| 2 | **Re-evaluated until answered**: if a higher-tier agent frees up while lower-tier agents are being offered the call, the offer moves up (lower-tier offers are withdrawn). |
| 3 | **Winning group / tier / campaign recorded on the call** — the basis for commission reporting and the agent's "Alpha Sales" cue. |
| 4 | **Elite = an agent group within the campaign** (revised S163, confirmed by Stephen — cleaner than a skill that only meant agent assignment): `X-Elite: true` → the queue node offers the call **only to the Elite group** and it **stays queued with Elite** (no fallback), so the external router's records match ours. The call stays on its own campaign, so script, recording, tax, payment credentials and offers are all the campaign's — nothing duplicated. |
| 5 | Agents available to several waiting calls: **longest-waiting call first**, Campaign.Priority (with queue acceleration) as the override — the existing arbitration. |

## Data model

- **`GroupCampaignAssignment`** (existing: group ↔ campaign, proficiency) gains
  - `RoutingTier` (int, default 0 = regular; higher = offered first; Alpha = 10, say)
  - `ExclusiveWindowSeconds` (int?, default null = pure priority)
  - `CommissionTier` / label (string?, e.g. "Alpha") — what the call is tagged with when won through
    this assignment
- **New `AgentGroupMemberCampaignExclusion`** (group, agent, campaign) — *which of the group's
  campaigns a member may NOT take* (CXone's per-agent routing attributes). Stored as exclusions, not
  an allow-list, so new members and newly assigned campaigns need no re-syncing: a member takes all of
  the group's campaigns until an admin unchecks one. (Built S163 — the original draft had an allow-list.)
- **`CallRecord`** gains `RoutedGroupId`, `RoutedTier`, `RoutedTierLabel` (e.g. "Alpha", "Elite") —
  what commission reporting keys on.
- Direct `AgentCampaignAssignment` stays tier 0 (regular).

## Queue engine

- `EligibleAgentRanker` returns each eligible available agent with its **best tier for the
  campaign** (direct = 0; via group = that assignment's `RoutingTier`, only if the member is allowed
  the campaign), then proficiency, then longest idle — as today.
- `QueuePollingService` (already a 1-second loop with atomic claims) offers each queued call only
  to agents in the **highest tier that currently has anyone available** (respecting an exclusive
  window: during the window, only that tier or higher). Recomputed every tick — so an Alpha agent
  freeing up mid-ring takes over the offer.
- **Withdrawing offers**: RingAll/RingTopN offers are click-to-answer screen pops keyed
  `queue_ring:{channel}:{agent}`; when the call's tier moves up, lower-tier pops are retracted
  (new SignalR "offer withdrawn" message) and their ring keys cleared. The atomic claim already
  guarantees only one agent can win a race.
- On delivery: stamp the call record's routed group/tier/label/campaign (single write alongside the
  existing delivery bookkeeping).

**As built (S163):** `EligibleAgentRanker.GetOfferSetAsync` / `SelectOffer` (tier → proficiency →
longest idle; exclusive windows ignored when a group restriction applies); `QueuePollingService`
uses it for ring, auto-answer and queue-callback delivery, tracks the offered set in Redis
`queue_offer:{channel}` and pushes `ReceiveOfferWithdrawn` to agents who drop out of it (tier moved
up, or they went unavailable); `ReceiveIncomingCall` / `ReceiveAutoConnecting` carry the tier label
for the agent's badge; `QueuedCallDeliveryService` stamps `SetRoutedTier` via
`EligibleAgentRanker.ResolveRouteAsync` and refuses a click from outside a restricted group.

## Flow side

- `tf_route_to_queue` gains an optional **"only offer to agent group"** (exclusive, no fallback) —
  e.g. NeuroQ_Only's `X-Elite` branch queues NeuroQ restricted to the Elite group. The flow otherwise
  just queues; hold messages / periodic announcements / queue-callback interrupt stay on the play node.
- **Which script pops** is unchanged and call-driven, never agent/group-driven: node-level flow →
  transfer override → **dialed number's** script → **call's campaign** script. An Alpha agent taking
  a NeuroQ TV call gets the NeuroQ TV script; the same agent on a Joint Food call gets Joint Food's.

## External routing API (Elite / RingSquared) — contract from TMS's Dial800Routing service

Source reviewed S163: `X:\VB Projects\Dial800Routing` (WCF), which got live numbers from
`X:\GIT\GitLab\Rejects` (`GetStats`), which in turn spawned the CXone `Get_Skill_Stats` script
(CXone's real-time API lagged by seconds). Structure only — the originals contain DB passwords and an
API key, not reproduced. **We need none of the stats plumbing**: our queue engine *is* the real-time
source, so the answer always agrees with what the queue will do.

**It's a routing decision, not a yes/no.** RingSquared (formerly Dial800) asks before sending a call:

```
POST /{BusinessProcessID}/Routing          BusinessProcessID = CXone skill id (for us: campaign)
{ "SessionId": "...", "DNIS": "tel:+1XXXXXXXXXX", "ANI": "tel:+1XXXXXXXXXX" }   (wrapped JSON)

Accept  → 200  { "Targets": ["<our delivery TFN>"] }
Reject  → 404  { "Targets": [...], "Message": "No Agents Available" }
```

- `tel:+1` is stripped from DNIS/ANI.
- **Target** = our delivery number for the dialed client TFN (CRMPro `Telephony.DNIS` looked up by
  `Client_TFN = DNIS`); several targets possible. Unmapped → a default placeholder number.
- **Accept rule** — per-skill limits (`reject_limits`), three modes:
  - *max queue count* (default when no row: **1**): logged-in agents > 0 **and** queued < N;
  - *max queue length*: logged-in agents > 0 **and** longest wait < N seconds;
  - *neither set on an existing row*: logged-in agents > 0 **and** an agent available.
- **Rejects are written to the CDR** (as a 1-second interaction flagged `IsReject`) so reject volume
  shows in reporting.
- Companion endpoint `POST /CallInfo` `{ClientID, CallDate, Dialed_TFN, ANI}` — RingSquared reports
  calls it placed; stored for reconciliation.
- The original Routing endpoint had **no authentication**; ours will require an API key header
  (confirm RingSquared can send one).

**ContactConnection equivalent** (decisions S163)
- **Our campaign ids** in every URL (direct tie to the campaign); optional `?group=<agentGroupId>`
  to ask about one group only (Elite).
- **API key required** (header) on all external routing endpoints; the key identifies the tenant.
- Three endpoint styles, because different routers wanted different things (all built by Stephen
  at TMS over time):
  1. **RingSquared routing decision** — `POST /api/v1/external-routing/{campaignId}/Routing` and
     `/CallInfo`, the exact request/response/status shapes above.
  2. **Availability stats** — `GET …/{campaignId}/availability` → JSON (logged in, available,
     unavailable, queued, longest wait; per tier when groups apply).
  3. **Simple yes/no** — `GET …/{campaignId}/available` → `200 {"available": true}` or
     `404 {"available": false}`.
  4. (No endpoint) routers that just **connect the call**: the flow plays the existing
     no-agents-available tone — plumbing already exists.
- Per-campaign routing settings replace `reject_limits`: accept mode (queue count / queue wait /
  agent available), limit, and client TFN → delivery number mapping. **Client numbers will be
  provided fresh** (not taken from the CRMPro backup).
- Answers come from the same ranker + queue sessions the queue engine uses — tier/group-aware.
- Rejects recorded for reporting; CallInfo stored per tenant.

**Call delivery from RingSquared** — the campaigns' numbers were ported to RingSquared; at TMS they
delivered by SIP to CXone CloudConnect numbers. We'll need RingSquared's SIP connection details.
Note: custom SIP headers (`X-Elite`) only survive **SIP-to-SIP** delivery straight to our platform;
a hop through the PSTN / a carrier's DIDs would drop them — so either direct SIP peering, or distinct
delivery numbers for Elite. To decide once the carrier onboarding (Bandwidth) is settled.

## Number providers (added S163 — Stephen: "something we both overlooked")

CXone had no notion of who houses a number; Stephen could only mark CloudConnect numbers through
their description. We need it explicitly:

- **`NumberProvider`** (tenant-scoped): name, type **carrier** (Telnyx, Bandwidth — they host the
  number and hand us the call) or **routing_platform** (RingSquared — houses the public number, we
  only receive its calls), optional SIP gateway / source IP allowlist, the **API key** it uses on our
  external routing endpoints (identifies tenant *and* partner), notes.
- **`PhoneNumber`** gains `ProviderId` and a **role**: `hosted` (a real number on our carrier) or
  `routing_delivery` (pseudo-DNIS a routing platform delivers to), plus **`ClientNumber`** — the
  public TFN it stands for (housed at the routing platform).
- RingSquared's Routing lookup (client TFN → our delivery number) becomes a query on
  `ClientNumber` for that provider — no separate mapping table; fresh client numbers are entered on
  the numbers screen.
- Every call records its provider (media attribution / Cannella reporting / billing
  reconciliation). The numbers admin shows provider + role instead of relying on descriptions.

## UI

- **Agent groups**: assign campaigns with tier / window / label; per-member campaign checkboxes.
- **Agent pop / answer**: tier badge (e.g. "Alpha") on the incoming call and on answer.
- **Supervisor dashboard**: which tier each queued call is currently being offered to.

## Not in scope here

Commission calculation itself (Tier 1 "commissions" item) — this records what it needs.
