# Interaction-Scoped Commerce (planned and DONE S178, 2026-10-04: all 5 phases built + live-verified)

**Why:** a caller transferred sales → CS is one call with two interactions (ARCHITECTURE §22). CS agents can place
orders too: the NeuroQ and My Best Heart customer-service scripts do save-the-sale scripting and free-form orders (items
picked straight from the catalog, no offer flow). Today the cart and order live on the call record, so a CS order
would overwrite the sales cart. Clint tests next, so everything has to be in the right place first.

## The split (decided with Stephen)

| On the **call record** (one per call, shared) | On the **interaction** (one per agent's piece of work) |
|---|---|
| Phone call: ANI, DNIS, start, hang-up (`disconnected_at`), handle time, recording | Agent, campaign, start/end (done) |
| Customer: names, phones, email, billing/shipping addresses (travel with the caller to CS) | Disposition + script-written custom fields (done) |
| Media attribution; entry campaign/client | **Cart** |
| Card-capture lifecycle (`sensitive_data`) | **Order number, order submitted, payment status** |
| Usage minutes (one caller leg) | Payment transactions + order API calls (linked by `interaction_id`) |
| | **Routing tier** (each delivery has its own) |
| | **Commissions**: agent, campaign, tier of the interaction that sold |
| | **AI summary** (per interaction, for campaigns opted in) |
| | Script session(s), flow variables, commitments |

Script and Liquid names stay the same: `{{call_record.cart…}}`, the order number and the Liquid `call_record.cart` resolve to
**the current interaction's** cart. No existing script or API definition needs editing.

## Phases (each ends green, committed, and live-checked where it touches calls)

**1. Every script session runs inside a saved interaction.** `FlowEngine` start creates the interaction row when it doesn't exist
(manual / training / outbound / callback launches), assigned to the agent and the record's or flow's campaign. Delivery
keeps creating its own. Backfill: records with sessions but no interaction get one per session.

**2. Storage.** `call_interactions` gains `cart` (jsonb), `order_number`, `order_submitted_at`, `payment_status`,
`routed_group_id/tier/tier_label`. `payment_transactions` and `orders` gain `interaction_id`. Migration copies each record's
values onto its first interaction. The record columns stay read-only for one phase, then are dropped (phase 5).

**3. Writers use the current interaction.** Cart service, cart endpoints + node handlers (add/remove/reset), pricing and tax
recalculation, inventory reservations, authorize/void payment, order-number assignment, order submission (API call / commit),
the agent cart strip (keyed by the tab's session → interaction). Delivery stamps the routing tier on the interaction.

**4. Readers use the interaction.** VariableResolver + ApiTemplateModelBuilder (current interaction), commissions
(per interaction; recalculation + report), AI summaries (per interaction: queue, context builder, wrap-up card, review),
Call Records review (edit / resubmit / re-authorize act on a chosen interaction), exports.

**5. View + cleanup.** Call detail: shared header (call, media, customer, addresses), then **one collapsible section per
interaction** (header: #, campaign, agent, disposition, time, status; body: AI summary, fields, flow variables, payments &
API calls, cart/order). Drop the record-level cart/order columns.

## Not changing

- Customer identity and addresses stay on the record and follow the caller to CS (correct today).
- Usage billing stays per record (one caller leg).
- Interactions not transferred behave exactly as now; the first interaction is "the sale" for single-agent calls.

## Setup note (from the live test)

Offers must be assigned to the CS campaign before a CS agent can add them to their cart (offer availability follows the
interaction's campaign).
