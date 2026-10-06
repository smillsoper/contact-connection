# Sprint 1 Spec: Go-Live Foundations (decided S175, 2026-10-04)

Decisions were settled ahead of the sprint so the sessions go to code. Order of work: **0 → 1 → 1b → 2 → 3 → 4**.

---

## 0. Mid-call transfer to another campaign's queue (sales → CS) (≈2 sessions, FIRST)

Found S176: a sales agent's script fires `trigger_telephony_event` → the telephony branch's `tf_transfer` (another campaign's queue)
**always takes `failed`**. Event branches fired from the CRM side run with `ctx.Esl == null`, and `TransferNodeHandler`
bails on that (traces 2026-10-04 2:57 PM and 3:18 PM). `SecureCollectNodeHandler`/`DelayNodeHandler` already open a fresh ESL
through `_eslFactory` in that case. Life Seasons needs this: callers often redial the sales number to reach customer service, and
Life Seasons doesn't want an IVR menu on the sales line.

### Decisions (Stephen, S176)

- **Same call record, a new interaction** (ARCHITECTURE §22 multi-interaction model), not a new record.
- **Cold transfer first.** Warm (3-way until CS answers) comes later.

### Build

1. `TransferNodeHandler`: when `ctx.Esl` is null, open one through `_eslFactory` (the Secure Collect pattern).
2. Mid-bridge mechanics: unbridge the caller from the sales agent, **park with MOH** (not hang up; `park_after_bridge` is
   already set at `tf_answer`), release the sales agent into ACW, then enqueue for the target campaign. Reuse the queue
   delivery path so the CS agent is rung normally.
3. **Stop `record.SetCampaign(target)`** for this path. The call record keeps its entry (sales) campaign: media
   attribution, DNIS, the original order. The session's *current* campaign switches for routing only.
4. `CallInteraction` gains **`AgentId`, `CampaignId`** (+ migration; backfill existing interactions from their record).
   Interaction 1 = sales agent/campaign; the CS agent's answer opens **interaction 2** on the same record with the CS
   campaign's screen-pop flow. Commissions, KPIs and agent stats read agent/campaign per interaction.
5. CS agent screen: their own script, plus the caller and the original order from interaction 1 (read-only).
6. Call state history: `transferred` then `in_queue` under the CS campaign; the sales agent's handle time ends at the unbridge.
7. Billing: one caller leg, so the minutes are counted once (the meter is already per record).
8. Live test: a sales call → the agent fires the CS transfer → the caller hears hold music → a CS agent answers with interaction 2 → check commissions and media attribution still sit on sales.

---

## 1. Script launch modes (≈2 sessions)

**Status (S179):** built — part 1 (`2582b10`…`578e254`) + part 2 (`599cfc2`, `7ddf2f8`). Sandbox credential tabs, training
responses on tenant API endpoints, practice runs filtered out of Call Records / usage / commissions / AI / media.
**When the export engine and KPI queries are built (Sprint 2), they must filter `run_mode = 'production'`** — they don't exist yet.

### Modes

| Mode | Who | Requires | Credentials |
|---|---|---|---|
| **Production** | any agent | an **active call** (the manual launch is refused without one) | production |
| **Training** | new permission **`TrainingMode`** (trainees + trainers) | no call | the campaign's **sandbox** set, or **simulated** when there is none |
| **Designer sandbox** | users who can edit flows | no call | **chosen per run**: sandbox or production |

### Decisions

- **Training data is kept and flagged.** The call record is stamped `RunMode = training`, so supervisors can review a trainee's call.
  It is **excluded** from usage billing, commissions, KPIs/dashboards, exports and media-agency reports.
  Designer-sandbox runs get `RunMode = sandbox` with the same exclusions. Production stays `RunMode = production`, the default and all existing rows.
- **No sandbox credentials → simulate.** In training, any provider without a sandbox set returns a canned success:
  - Payment gateway: approved, auth code `TRAINING`, fake transaction ID `TRN-…`, no network call.
  - Tax provider: flat rate (the campaign's fallback rate, else 0) with a "simulated" jurisdiction line.
  - API definitions: each definition gets an optional **Training response** (JSON; the API Definition editor's sample
    model is the starting point). Without one, a generic `{ "success": true, "simulated": true }` is returned.
  - Every simulated step writes a trace and a call-history line ("Simulated: no sandbox credentials") so a trainer can see it.
- **Training mode never falls back to production credentials.**
- A **mode banner** is always visible in the agent UI while a non-production script runs (TRAINING in amber, SANDBOX in violet).

### Where it plugs in

- `CallRecord`: add `RunMode` (string, default `production`) + migration. `CallRecord.CreateManual` takes the mode.
- `POST /api/v1/call-records/manual` (`CallRecordsEndpoints.CreateManual`): accept `{ flowId, mode, credentialSet? }`. Enforce
  the permissions and the active-call rule for production.
- Credential resolution: everything campaign-scoped goes through `ScopedCredentials.ResolveAsync(...)` (used by
  `AuthorizeNetGatewayClient.ResolveScopedAsync`). Make it mode-aware: a sandbox set is stored under a parallel key (e.g. provider
  `AuthorizeNet` → `AuthorizeNet.sandbox`). The resolver picks the set from the call record's `RunMode` (and the designer's choice).
  Tax providers and API definition credentials (`ApiDefinitionExecutor` → `GetCredential`) use the same path.
- Campaign credential card (web): a **Production / Sandbox** tab per provider, with Test on each.
- Exclusions: usage meter (`PortalTenantsEndpoints.Usage`), commissions (`CommissionService`/recalc), dashboards/KPI queries,
  exports and the media snapshot all filter `RunMode = production`.
- Permissions: add `TrainingMode` to the permission catalog and default roles (admin yes, agent no).


---

## 1b. Manual outbound: Place Call picker, campaign caller ID, keypad (Slice A, ≈1–2 sessions)

**Status (S179):** built (`bb78b06`, `aa71928`, `2e396da`, `a0459cf`); Stephen chose **server-placed calls** (API originates: agent softphone auto-answer leg → bridge to the customer with the server-chosen caller ID). Live: caller ID, auto-answer, status restore. `#` DTMF (SIP INFO) retest waits on SignalWire vetting.

Stephen's design (S179, 2026-10-05), refining the S169 manual-outbound decisions (memory: project_manual_outbound_dialing).
Go-live need: Life Seasons makes about 800 outbound calls a month (callbacks), and the wrong caller ID is a carrier-compliance problem.

### Softphone

- The always-visible number field is replaced by a **Place call** button that opens an accordion:
  1. **Internal:** users whose role has the new switch **"Included in softphone internal dial list"**, with live
     presence (available / on call / away …), so the agent knows who can pick up. Dials their extension.
  2. **External:**
     - **Clients** that have **manual outbound campaigns the agent is assigned to** → click a client → its manual outbound
       campaigns (**auto-selected when only one**) → then the **number field + keypad**.
     - **Direct dial (role-gated):** a new role permission **"Can direct dial"** adds a Direct dial entry that dials
       without a client or campaign, using the **tenant's default outbound caller ID** (new tenant setting).
- **Caller ID preview:** before the agent places an external call (campaign or direct dial), the softphone shows the
  caller ID the call will use, e.g. "Calling as (541) 641-3898 · NeuroQ CS". It confirms visually that the right
  campaign is selected. Internal calls don't show it. The preview comes from the same server-side rule that sets the real
  caller ID, so the two can never disagree.
- Direct-dial users get the same **Place call** button (possibly labeled differently) with Internal / External; Direct
  dial is an extra choice under External, not a separate always-visible number field.
- **Keypad** (DTMF) on any connected call: outbound, inbound, warm/consult transfers. Sends RFC 2833 DTMF on the active
  leg (navigating the far end's IVR, entering extensions).

### Server

- The dial carries the chosen campaign (or direct dial). **Caller ID is decided server-side**: the campaign's
  `CallerIdNumber`, else the tenant default for direct dial. The softphone's `X-CC-Caller-Id` header is never trusted.
- The outbound call record is created with the campaign's **client + campaign** (today: none). The agent's interaction
  gets the campaign (interaction-scoped commerce), so reporting, scripts, billing and commissions attach correctly.
- **Calling hours (now):** block an external dial outside the campaign's allowed hours in the callee's local time.
  Time zone from the best source available (a prior call record's address/ZIP for that number → tenant time zone),
  with a plain message to the agent and an audit entry for every block. The internal DNC list is checked if present.
  **National DNC + the area-code time-zone database come with Slice B.**
- Admin: role switches (internal dial list, can direct dial), tenant default outbound caller ID, and campaign allowed
  outbound hours (if not already present).

### Slice B (later)

Outbound call flows (park → flow → dial node with answered / no_answer / busy / failed / machine branches),
configurable script-pop timing, address-book entries (including internal-campaign entries), National + state DNC,
area-code/ZIP time zones (zip-codes.com monthly DB via Worker SFTP), per-state hour overrides, call purpose.

---

## 2. Invoices (≈1–2 sessions)

**Status (S179):** built + live-verified (`15df290`…`557b2bf`).

### Decisions

- **Draft, then issue.** On the 1st (tenant time zone) the Worker builds a **draft** invoice for the previous month from the usage meter
  (rounded-up minutes; local / toll-free / outbound lines; monthly minimum top-up line). You review it in the Portal, add
  adjustment or credit lines (each with a reason), then **Issue**.
- Issuing assigns the number **`INV-YYYY-NNNN`** (platform-wide sequence), freezes the lines, emails the tenant's billing
  contacts, and makes it payable (Stripe in step 4).
- The setup fee is a one-off line added to a draft (or a standalone invoice).
- An issued invoice is never edited. Corrections go out as a **credit note** against it.
- Printable invoice view (Portal now; tenant billing area later reuses it).

### Data (platform `public` schema)

- `invoices`: id, tenant_id, number (null while draft), period_start/end, status (`draft|issued|paid|void`), subtotal,
  total, issued_at, due_at, paid_at, stripe_invoice/payment refs (null until step 4), created_at.
- `invoice_lines`: id, invoice_id, kind (`usage_local|usage_tollfree|usage_outbound|minimum|setup_fee|adjustment|credit`),
  description, quantity (billed min), unit_price, amount, reason, created_by.
- Rates are **copied onto the lines** when the draft is built, so later rate changes don't alter past invoices.
- **Round each line, then total = sum of the rounded lines.** Today `UsageCharges` rounds local+outbound combined, while the card
  rounds each row, so on Oct test data the rows add to $1.13 against a $1.12 total. Fix `UsageCharges` to round per line
  (local, toll-free, outbound) when invoices are built.

---

## 3. Telephony handler null guards + live check (≈½ session)

**Status (S179):** done (`5c29e7a`, 0 warnings); inbound live check matched the SignalWire CDR. Outbound half waits on vetting.

- Add the existing `if (ctx.Esl is null) { log; follow default }` guard to `AnswerNodeHandler`, `HangupNodeHandler`,
  `RejectNodeHandler`, `RouteToQueueNodeHandler`, `SetCallerIdNodeHandler`, `SetSipHeaderNodeHandler`. This clears the last 6
  build warnings.
- One live inbound test call and one softphone outbound call. Confirm `disconnected_at` is stamped, and that the usage card's billed
  minutes match the SignalWire CDR for that day.

---

## 4. Stripe, tenant side (≈1–2 sessions, Stripe **test mode**)

**Status (S179):** built + live-verified (`9d1b0a1`, `6699bd1`, `109dd97`, `06f1dba` — incl. refund / carry-forward for credits on paid invoices).

- Platform Stripe keys in User Secrets / Key Vault (`Stripe:SecretKey`, `Stripe:WebhookSecret`, publishable key in web config).
- `Billing` permission; a tenant **Billing** page: payment methods through Stripe's hosted Payment Element (ACH via Financial
  Connections + card), using SetupIntent. We store only the customer ID, payment-method ID and display info.
- Invoice list and printable view; **Pay now** (PaymentIntent against the saved method; ACH shows *processing*).
- Signature-verified webhook endpoint: `payment_intent.succeeded/processing/payment_failed`, `charge.dispute.created`, which updates
  invoice status and emails on failure.
- Autopay switch: when an invoice is issued, charge the default method automatically.

---

## Not in Sprint 1

Stripe as a **campaign** gateway (needs raw-card access), PCI attestation, Azure hosting, number porting.
