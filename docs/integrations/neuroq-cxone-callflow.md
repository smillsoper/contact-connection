# NeuroQ — CXone Production Call Flow → ContactConnection Mapping

Source: TMS's CXone Studio exports (`X:\CXOne Scripts\`), reviewed Session 163. Structure only —
an embedded API key (TMS reject/override service) and internal staff email addresses were present
and are deliberately not reproduced.

## Which scripts matter

| Script | Role | In scope |
|---|---|---|
| **NeuroQ_Only** | Inbound entry for NeuroQ sales (NeuroQ TV / LF TV, SF TV, Elite) | **Yes** |
| **NQ_Queue** | Queueing, "Alpha" tier routing, hold, event handlers | **Yes** |
| Block_ANI_with_DB, CheckAgents, Port Checker | Sub-scripts called by NQ_Queue | Yes (mapped below) |
| CallbackPop_V2 → CallbackInfo | Caller-requested callback (menu) | Present but **disconnected** in NQ_Queue at export — callback offer was not live |
| NeuroQ | Older entry: NeuroQ CS (customer care, own hours) + generic TMS_Queue | No — CS line, not in Life Seasons' list |
| NeuroQ_Dialer, NeuroQAbandons | Outbound abandon-callback program | **No — built, never used in production** (per Stephen) |

## Call path (NeuroQ_Only → NQ_Queue)

1. **SIP headers** — read `X-dnis` (actual dialed number, for Singlecomm's single-endpoint delivery)
   and `X-Elite` (true/false, from RingSquared).
2. **DNIS fix-up (Singlecomm only)** — if `X-dnis` differs from the delivered DNIS, set DNIS from the
   header and call a TMS "override numbers" REST service. Annotated as removable once fully on
   RingSquared (direct TFN delivery).
3. **Base variables** — OriginalANI/DNIS, skill, `QueueMessage` = `AllRepsBusyKelly.wav`, `NoLog`.
4. **Whisper** — read the skill's *notes* field via the CXone admin API and use it as the agent
   whisper text.
5. **Store OriginalANI** (24h, keyed by ContactID) — for screen pop after transfers.
6. **Elite?** — `X-Elite = true` → switch to the **NeuroQ Elite** skill.
7. → **NQ_Queue**:
   1. Port Checker (voice-port capacity ≥ 90% → email ops, throttled 15 min).
   2. **Blocked ANI?** (SQL lookup) → play `Inactive.wav` ("this number is not currently active") → hang up.
   3. **Maintenance hours profile** — "Meeting" state → play `TechDiff.wav` → hang up.
   4. **Any agents logged in?** — none → email the MOD (throttled 5 min) → `TechDiff.wav` → hang up.
   5. **Alpha tier routing** (skills 29280987 NeuroQ TV LF, 35569704 NeuroQ Elite LF, 35684467 NeuroQ SF TV):
      the "Alpha Sales" skill (35708009) is a premium agent pool across MBH, NeuroQ Elite/TV/SF TV
      and Joint Food, selected per campaign by routing attribute (97 = NeuroQ TV, 99 = NeuroQ SF TV).
      - Alpha agent available now → request Alpha; ring loop 3 × (ringtone + 3.5 s) ≈ 30 s.
      - Not answered / none available → request the **normal** skill (with whisper), then loop:
        play `NQ_Hold_MessageV2.wav` in **20-second chunks resuming where it left off**, and after
        each chunk re-check whether an Alpha agent became available → re-request Alpha.
      - Skills not Alpha-queueable go straight to the normal request.
   6. **OnAnswer** — if answered on the Alpha skill, send the agent "Alpha Sales!" (Alpha agents earn
      a higher commission); trigger the CRM screen pop (REST); start recording unless `NoLog`.
   7. **OnTransfer** — re-store OriginalANI, wait 5 s, stop recording. **OnHold** — MOH loop.

## Mapping to ContactConnection telephony nodes

| CXone | ContactConnection | Status |
|---|---|---|
| SIPGETHEADER `X-Elite` | `tf_get_sip_header` → `tf_branch` | ✅ |
| SIPGETHEADER `X-dnis` + DNIS override REST | — | ⏭ Singlecomm-era; Telnyx delivers the dialed DNIS directly |
| Base variable ASSIGNs | `tf_set_variable` | ✅ |
| Whisper from skill notes | `tf_whisper` (per campaign) | ✅ (text lives on the flow/campaign, not an admin API lookup) |
| PUTVALUE OriginalANI | not needed — call record + shared variables carry ANI through transfers | ✅ |
| Elite → Elite skill | Separate **NeuroQ Elite** campaign; branch → route to it | ⚠ confirm a flow can route to a campaign other than the one the number belongs to |
| Block_ANI_with_DB | `tf_check_block_list` → `tf_play` → `tf_hangup` | ✅ |
| HOURS (maintenance) | `tf_time_of_day` | ✅ |
| CheckAgents (none logged in) | `tf_check_agent_availability` → `tf_play` → `tf_hangup` | ✅ routing; ❌ email alert |
| REQAGENT normal skill + whisper | `tf_route_to_queue` (+ campaign ring strategy / proficiency) | ✅ |
| Alpha tier: premium agents first ~30 s, then everyone, re-offer to premium as they free up | proficiency ranking / ring-top-N exists; no time-based tier overflow | ❌ gap |
| Hold message in 20 s chunks resuming position | hold MOH + periodic announcements | ⚠ partial — no resume-at-position |
| OnAnswer "Alpha Sales!" agent message | `tf_on_agent_answer` | ⚠ needs an agent notification + ties to commissions |
| OnAnswer screen pop REST | `tf_on_agent_answer` / `tf_script_pop` (native) | ✅ |
| Recording start/stop, NoLog | campaign recording settings / `tf_record` | ✅ |
| Port Checker capacity alert | — | ⏭ platform monitoring concern, not a call-flow step |
| Callback menu (disconnected) | `tf_queue_callback` | ✅ available if wanted |

## Gaps to decide on

1. **Tiered "preferred agents first" routing** — premium (Alpha) agents get the call first for a
   window, then it overflows to all eligible agents, and is re-offered to premium agents as they
   free up. Core to how NeuroQ sold; CXone could only simulate it.
2. **Route to a different campaign mid-flow** (Elite) — verify, or add a target-campaign option.
3. **Operational email alerts from a flow** (no agents logged in; throttled) — or surface on the
   supervisor dashboard instead.
4. **Agent notification on answer** ("Alpha Sales!") — and the premium-agent commission tie-in
   (Tier 1 commissions work).
5. **Hold message resume-at-position** (minor).

## Assets needed

Audio prompts referenced: `AllRepsBusyKelly.wav`, `Inactive.wav`, `TechDiff.wav`,
`NQ_Hold_MessageV2.wav`, `RingTone.wav`, `Good_MOH_Mix.wav` (plus `GenericCallback.wav`,
`Callback_6100–6104.wav`, `Thanks.wav` if the callback offer is used). Until provided, TTS
placeholders with the recorded phrases (captured in the scripts) can stand in.

## Built (S163) — draft flow "NeuroQ Inbound — Parallel Queuing (draft)"

Seeded **inactive** in `tenant_test_tenant` (flow `a1b2c3d4-0163-4000-9000-00000000e001`, campaign
NeuroQ); definition exported to `neuroq-telephony-flow.json` (its `agentGroupId` is the test tenant's
"NeuroQ Elite" group — remap for production). Groups created alongside, no members yet: **Alpha
Sales** (NeuroQ tier 10, label "Alpha") and **NeuroQ Elite** (tier 0, label "Elite").

`X-Elite` header → block list → blocked: **`tf_reject` before answering** (the one deliberate
reject — no media, call never committed; CXone answered and played Inactive.wav) → answer → **any agent logged in?** (`check: logged_in`; busy counts) → if nobody: **`tf_send_email`
to the MOD and queue anyway** → Elite? → `tf_route_to_queue` pinned to NeuroQ Elite, or unpinned
(parallel queuing: Alpha first, regular pool, re-offered to Alpha) → "all reps busy" → music with a
periodic hold message. Event branches: agent selected → script pop → whisper "NeuroQ"; agent
answer → start recording.

**Deliberate departures from CXone (Stephen, S163):** no hang-up when nobody is logged in — the MOD
is emailed and the supervisor dashboard's Queued Calls widget raises a "no agents logged in" alert
with an **Assign agent** action (assignments are read every 1-second poll, so the call goes to the
newly assigned agent once Available). The maintenance-hours hang-up is dropped (rarely used; a
contact center answers calls). NeuroQ runs with **no queue size limit and no queue timeout**
(campaign MaxQueueSize = QueueTimeoutSeconds = 0) — Life Seasons wants every call answered. The MOD
email's To is left blank for Clint to fill in.

Gaps from the list above now closed: tiered routing (1), Elite without a second campaign (2),
"Alpha Sales!" cue = the agent's tier badge + `CallRecord.RoutedTierLabel` for commissions (4).
Also closed: the "nobody logged in" alert (3) — email node + dashboard alert. Still open:
hold-message resume-at-position (5); real audio is Clint's to drop in.
