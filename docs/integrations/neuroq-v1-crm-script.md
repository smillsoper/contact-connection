# NeuroQ - V1 — CRMPro script → ContactConnection CRM flow (S164 analysis)

Source: CRMPro production DB (`Scripts` row **"NeuroQ - V1"**, `Script_ID 94fe2334-…`, client
"Life Seasons, LLC", active, revision 10.5, last updated 2026-07-09) → its `ScriptFiles` list →
`X:\Backup_Data\CRMPro_Scripts\SCRIPT_PROD_NEUROQ -INBOUND.*` (all dated Jul 9, matching). Other
main scripts: "NeuroQ - SF TV" → `SCRIPT_PROD_NEUROQ - SF TV.*`; "My Best Heart - Life Seasons" →
`SCRIPT_PROD_MY BEST HEART LATEST.*`; "JF - Healthy Aging" (client Nordic Healthy Living, Inc) →
`SCRIPT_PROD_JOINT FOOD INBOUND.*`.

Structure only — the source contains live-looking credentials in dead code (a Konnective login in
`SubmitOrder`, SMTP details); none are reproduced or used (see `feedback_no_backup_credentials`).

## How the files fit together

| File | Holds |
|---|---|
| `.DESIGNER.VB` | Layout: a tab strip with 8 pages, ~140 controls, positions, labels |
| `.DESIGNER.RESOURCES` | Binary .NET resources: **the script text** (60 RTF `ScriptBox` blocks, one per question), the offer catalog (`$this.Offers`), the two web-service controls |
| `.vb` | Behaviour: combo items, `*_SelectionItemChanged` branching, Next-button validation, `UpdateCart`, auth/order submit |

Script text embeds VB in `<* … *>` tags — mostly `ResponseText = Script.txtFirstName.Text`, but also
conditional text (`Select Case` on the chosen objection/probe, card type, shipping state, AutoShip).

## Tabs and flow (live paths only)

Call types assigned to V1 in the DB: **Order**, **All Other Calls** (14 dispositions), **Junk**
(8 dispositions). "C/S Message" and "Web Order" tabs are unreachable in V1 (no call type leads there;
the embedded Chrome browser is unused).

1. **Opening** — first name → zip (lookup fills city/state) → call type. Order → Offer; others →
   Closing (disposition). A static "price redirect" panel (How much is it? / Is it really free?).
2. **Offer** — Probe 1 (10 choices; "I just want the price" → Price Rebuttal), Probes 2–4 (free
   text), Hot Button, then the sales ladder:
   - Main Offer: *Yes* → B2G1 every 3 months · *No – does not want auto* → Auto Rebuttal (*Yes* →
     main; *No* → Basic with OTS: *Yes* → one-time 3 bottles; *No* → Downsell 1) · *No – rebuttal*
     → Rebuttal (*Yes* → main; *No* → Objections (7) → Objection Rebuttal (text per objection,
     weaving in the caller's probe answers) → *Yes* B2G1 / *No* Downsell 1).
   - Downsell 1 → 2 bottles every 2 months · Rebuttal 1 → 2 months + DHA · Rebuttal 2 → 3-month /
     2-month / 2-month+DHA / No (log call).
3. **Customer Info** — billing + shipping address (USPS-validated), email; phone or email required
   for orders; Canada → All Other Calls / "Shipping to Canada"; foreign blocked.
4. **Payment** — card type, number, expiry, CVV typed by the agent (Luhn, expiry, length, test-card
   block).
5. **Upsell** (main-offer path) — Preferred Customer (AutoShip) with two rebuttal rounds → Family
   offer (couples 4+2) or One-Time offer → **matching** Memory DHA cross-sell → **matching** Sleep
   Now cross-sell. "Matching" = the tier that fits the base package (family / 3-month / 2-month).
   Downsell path skips to cross-sells; one-time (OTS) path skips upsells entirely.
6. **Closing** — closing read → **SMS consent** (Yes/No, stamped with timestamp; marketing +
   transactional) → confirm → force tax recalculation → **Authorize.Net auth** → if approved,
   **`SubmitLifeSeasonsOrder()`** (the Life Seasons Order API; OrderLogix only before 9/4/2024).
   Declines: Change customer info / Change payment info / Cancel order; after **3** tries only
   Cancel (→ All Other Calls / Credit Card Declined). Order post failure: change info, or "keeps
   failing" → email IT (once). Then disposition (+ required explanation for some) → Finish.

## Cart (UpdateCart) and offer catalog

| SKU | Offer | Price | S/H |
|---|---|---|---|
| 283-3-CTY-90 | NeuroQ Memory & Focus Buy 2 Get 1 Free Every 3 Months (main) | 139.90 | 4.95 |
| 283-6-CTY-90 | Couples Buy 4 Get 2 Free Every 3 Months (family) | 209.85 | 4.95 |
| 283-2-CTY-60 | 2 Bottles Every 2 Months (downsell) | 99.90 | 4.95 |
| 283-2-CTY-60-P1 | 2 Bottles Every 2 Months with DHA | 99.90 | 4.95 |
| 283-3-OT-1 | Buy 2 Get 1 Free (one-time) | 179.90 | 12.95 |
| 326-1-CTY-90 | B2G1 + Matching DHA | 189.80 | 4.95 |
| 326-2-CTY-90 | Couples 4+2 + Matching DHA | 309.60 | 4.95 |
| 303-6/3/2-CTY-*, 303-1-OT-1 | Memory DHA-400 (6/3/2 bottles, one-time) | 99.75 / 49.90 / 49.90 / 24.95 | — |
| 307-6/3/2-CTY-*, 307-1-OT-1 | Sleep Now (6/3/2 boxes, one-time) | 89.70 / 49.90 / 39.90 / 19.95 | — |
| Retail Delivery Fee | CO Delivery Fee (tax code OF400000) | via Avalara | — |

Each offer also carries a **"Cannella Order/Upsell SKU"** flag (e.g. `B2G1 CTY 139.90`) for the
media agency's reporting. The cart is rebuilt from the answers on every change (single base
package; matching DHA/Sleep added on top). Tax: Avalara `PF050714` / shipping `FR020200`, ship-from
Kaysville UT, CO fee — already built (S163).

**Commissions (feeds the Commissions item):** if the call came in on the **Alpha Sales** skill, a
`COMMISSION` line = **10%** of (cart total − shipping − tax); otherwise `ORDER-COMMISSION` = **1%**.
ContactConnection records `CallRecord.RoutedTierLabel` ("Alpha") for exactly this.

## Mapping to ContactConnection nodes

| CRMPro | ContactConnection |
|---|---|
| ComboBox + ScriptBox | `input` (select) with the RTF converted to the node's script HTML |
| TextBox + ScriptBox | `input` (text) |
| `*_SelectionItemChanged` enable/disable chains | option-specific edges / `branch` |
| Conditional ScriptBox text (Select Case) | branch → one `script`/`input` per case |
| `ResponseText = Script.txtFirstName.Text` | `{{input.<first name node>}}` / `{{flow.first_name}}` |
| Zip lookup | address node ZIP lookup (built) |
| AddressControl + USPS | `address` node (validation + autocomplete, built) |
| `UpdateCart` | `add_to_cart` / `reset_cart` per chosen package (+ matching cross-sells) |
| Avalara + CO fee | per-campaign Avalara provider (built S163) |
| `DoAuthNet` | `authorize_payment` (built S161) |
| `SubmitLifeSeasonsOrder` | `api_call` → "Life Seasons Order API / Add Order" (Liquid, once per call — built S163) |
| Decline tries (3) | `set_variable` counter `{{flow.auth_tries + 1}}` + `branch` |
| "Order post keeps failing" email | `send_email` (built S163) |
| SMS consent + timestamp | `input` + `set_variable` / custom fields |
| Call type / disposition | **gap** — no first-class lists yet (see decisions) |
| Card entry on screen | **decision** — see below |

## Decisions (Stephen, S164)

1. **Card capture = keypad secure capture** (`tf_secure_collect` via the `cc_capture` event) — the
   agent never sees the card. V1's on-screen card fields, Luhn/test-card checks and the "read back
   the last four" verification are replaced; the agent note says so.
2. **Call type / disposition = flow variables + custom fields** for now (`call_type`, `disposition`
   tenant fields; new NeuroQ fields `disposition_reason`, `sms_consent`). First-class dispositions
   come later with reporting.

## Built (S164) — draft, not published

- **CRM flow "NeuroQ - V1 (from CRMPro, draft)"** — 125 nodes, inactive, in `tenant_test_tenant`
  (`4fe3b048-47d6-5e3f-bb0c-cf33045fad5d`); exported to `neuroq-v1-crm-flow.json`. Generated from
  the V1 sources: every ScriptBox's text verbatim (RTF → HTML, formatting and colours kept), `<* *>`
  code replaced by `{{variables}}` or split into per-case nodes (one objection rebuttal per
  objection; DHA/Sleep Now cross-sells per package tier). Sections: Opening, Offer, Customer Info,
  Payment, Upsell, Closing — all jumpable, which replaces V1's Change Customer/Payment Info tab jumps.
- **Catalog** — the 15 V1 offers/products (SKUs, prices, S/H, AutoShip interval, Cannella SKU flag),
  scoped to Life Seasons / NeuroQ. Base packages reset the cart; the family offer and DHA bundles use
  `replace`; matching cross-sells add on top — same as `UpdateCart`.
- **Telephony draft** — gained the `cc_capture` → `tf_secure_collect` branch (copied from the
  live-verified Test Campaign 1 flow; its prompts are that flow's test audio).
- **Platform additions:** `{{cart.total|subtotal|shipping|sales_tax|shipping_tax|fees|first_payment|
  item_count|items_summary}}` in CRM scripts (refreshed from the call's cart right before a node that
  uses it); `{{now.iso|date|time}}` (UTC) — the SMS-consent timestamp sent to the Order API.

## Left for review / follow-up

- **Clint:** read through the wording; audio for the secure-capture prompts; MOD/IT email address
  for the order-post-failure email (To is blank).
- **Dropped from V1 on purpose:** the NY "sales tax calculated at the main facility" note (Avalara
  now taxes NY); the unreachable Questions/Answer, C/S Message and Web Order controls; the in-cart
  COMMISSION lines (→ Commissions item, keyed on `RoutedTierLabel`).
- **Not carried:** `keycode`, `coupon_code`, `vendor_number`, referrer fields the Order template can
  send — V1 never set them; confirm with Clint whether Life Seasons expects any.
- **Follow-up:** expose the card's last 4 from secure capture (a shared variable) so the closing
  verification can come back; card-type-specific wording.
- To go live: publish the CRM flow, set it as the NeuroQ campaign's script (or the telephony
  `tf_script_pop`), activate the Order API definition with Life Seasons credentials, and test.
