# Life Seasons Client Readiness Checklist

**Created:** Session 160 (2026-09-24), following a client-requirements dump from the user after
Clint (a former coworker, now working for Life Seasons) got into the test tenant. Life Seasons
recently stood up their own contact center and is frustrated with Five9's disconnected back-end
systems and clunky dashboard — genuine switch potential, not a speculative prospect.

**Scope:** Everything needed to get a real, revenue-generating Life Seasons campaign live on this
platform — CRM commerce/cart handling, payment processing, commissions, media-agency call
attribution & reporting, a general scheduled-export engine, supervisor/dashboard tooling, an agent
helpdesk CMS, and team chat. This is a multi-month roadmap, not a single session.

**Known context about Life Seasons:**
- One or more Shopify and/or WooCommerce shops.
- Uses "Lira" for CS (tool/vendor name as given by the user — not yet investigated, may matter for
  the scripting/QA tier items later).
- Strictly-scripted customer service flows — no agent improvisation, which raises the priority of
  supervisor monitoring/QC tooling relative to a less script-strict client.
- Business model is media-driven direct-response marketing (TV/radio/print), currently through
  Cannella as their media agency — this is *why* Tier 4's media-agency/Cannella work exists at all.
- Sometimes runs out of stock on specific products; direct access to their live stock levels is not
  yet confirmed.

## How to use this file

- Work top-to-bottom by tier unless the user redirects.
- Check an item `[x]` and append `— closed Session N (date): <one-line summary>` once it's built
  and verified to this project's usual standard (clean build, tests where they exist, live-verified
  where practical).
- If an item is partially addressed or deliberately descoped, say so explicitly rather than leaving
  it ambiguous.
- Each item below carries the user's original requirement detail inline — don't lose that detail
  when checking an item off; append to it, don't replace it.
- Update the "Next up" pointer at the bottom after every session that touches this file.

---

## Tier 1 — Revenue-critical MVP

Nothing else matters if an agent can't take an order and get paid on a call.

- [ ] **CRM cart/product handling + inventory awareness.** Most of the Commerce Engine backend
      already exists (`Product`/`Offer`/`CartDocument`/`Order`/`OrderLine`/`PricingService`/
      inventory reservation — see ARCHITECTURE.md and DevLog Sessions 7–13). The gap is entirely
      agent-facing UI plus external stock sync:
      - Add/remove cart items automatically driven by CRM script input selection (a script node
        picks a product/offer, it lands in the cart without a separate manual step).
      - **Also** free-select from a searchable product list (agent can manually search/add outside
        of a scripted flow path).
      - Visible cart summary for the agent at all times — pop-out panel or an always-visible panel
        both acceptable; agent's choice/design call.
      - Product management UI (create/edit products, presumably already partially covered by
        existing Product/Offer admin surfaces — verify what exists vs. what's missing for this
        client's catalog size).
      - **Client/campaign-scoped offer management (found missing — Session 160, 2026-09-24):**
        neither `Offer` nor `Product` has a `ClientId`/`CampaignId` field today — both are
        tenant-wide only. `Offer`'s own doc comment describes "TV Special"/"Web Offer"/"Retention
        Offer" as offers "for different campaigns, channels, or price points," but that's a naming
        convention, not an enforced relationship — there's no way to query "offers for Campaign Y"
        or restrict an offer to a specific client. **Decision: add real scoping**, not just a naming
        convention. Add optional `ClientId`/`CampaignId` to `Offer` (nullable = tenant-wide,
        matching the same campaign > client > tenant precedence `CustomFieldService` already uses
        for scope resolution — see ARCHITECTURE.md §20), migration required. Then an admin view to
        list/assign/filter offers by client/campaign, not just by product.
      - Out-of-stock handling — client sometimes runs out of stock on specific items. Not yet known
        whether we'll get direct access to their live stock levels (Shopify/WooCommerce API) or
        need to rely on manually-maintained inventory in our own system. Needs a decision/spike
        before deep implementation: (a) poll/webhook Shopify & WooCommerce for live stock, or
        (b) tenant-managed manual stock levels via the existing `qty_available`/`qty_reserved`
        fields, or (c) both with Shopify/WooCommerce as source of truth when connected.
      - **Progress — Session 160 (2026-09-24):**
        - **Out-of-stock decision made:** going with (b)-lite — status-flag only, no live qty sync.
          Added `ProductInventoryStatus.OutOfStock` (distinct from `Discontinued`); set
          `decrementOnOrder=false` per Life Seasons product so `CanAddToCart()` is driven purely by
          the status flag, never by quantity math. Domain tests added, 177/177 passing.
        - **Phase 1 shipped and live-verified: manual/searchable cart (no scripted auto-add yet).**
          New `ICartService`/`CartService` (`AddItemAsync`/`RemoveItemAsync`/`UpdateQuantityAsync`/
          `ReplaceCartAsync`) extracted from the old inline whole-document-PUT endpoint; new
          incremental `POST/PATCH/DELETE /api/v1/call-records/{id}/cart/items[/{index}]`; product
          search now matches SKU (was Description-only) — Keywords/AliasSKUs left as a documented
          gap (jsonb-via-value-converter isn't LINQ-translatable without a raw-SQL fragment). New
          frontend from scratch: `CartPanel` (always-visible summary strip above the script view) +
          `CartModal` (line items, qty stepper, remove, search-and-add), dark-themed, wired into
          `AgentShell.tsx`. Live-verified against real HTTP calls: correct pricing math, 409 +
          `unavailableSkus` on an OutOfStock add with confirmed rollback, and — a real bug caught
          along the way — the CRM Flow Designer's manual "Select flow → Start" script-preview
          toolbar started sessions with no `CallRecord` at all (nothing for a cart to attach to);
          fixed via new `CallRecord.CreateManual()` / `POST /call-records/manual`, called
          automatically by `FlowPanel.tsx` when previewing a flow with no real call in progress.
          User confirmed working on-screen.
        - **Also found and fixed along the way (infra, not scope):** the dev API process had drifted
          into two redundant, non-file-watching `dotnet watch` trees fighting over the same output
          files — nothing being edited was actually taking effect. Killed both, one clean instance
          now running.
        - **Client/campaign-scoped offer management shipped and live-verified (same session,
          continued).** `Offer.ClientId`/`CampaignId` (nullable = tenant-wide) + `SetScope()`
          (campaign requires client, mirroring `CustomFieldDefinition`), migration
          `AddOfferClientCampaignScope` applied. New `PUT /offers/{id}` and `PUT /products/{id}` —
          neither existed before. **Two more real bugs caught and fixed while building this:** (1)
          an offer created with just a price and no explicit payment schedule priced every cart
          line at $0 (`PricingService` sums `Payments`, never `FullPrice`) — both Create and Update
          now default to a single full-payment installment when none is given; (2) product search
          always filtered to `Searchable=true`, so toggling a product non-searchable in the new
          admin UI would make it invisible to the admin who just edited it — added an `includeAll`
          flag used by the admin list only. New admin UI: **Admin → Commerce → Products & Offers**
          (`/admin/products`, `/admin/products/:id/offers`) — create/edit products and their
          offers, including the client/campaign scope picker. User confirmed working on-screen with
          real test data (multiple Widget Mobile products/offers, one scoped to a real
          client+campaign).
        - **Explicit scope decision on the new admin UI (user call, same session):** it intentionally
          only covers the "base" Offer/Product fields (name, price, shipping, tax/shipping exempt,
          inventory status, scope). **Not exposed yet, staying API-only**: Offer — Quantity Price
          Breaks, MixMatch, multi-payment schedules, AutoShip, upsell fields, ship-to/delivery-message
          options, campaign window (ValidFrom/ValidTo), personalization prompts, flags. Product —
          Keywords/AliasSKUs, geographic surcharges, Kits, category/attribute assignment, advanced
          inventory (qty limits, restock date, backorder/discontinued messaging). **Decision: expand
          incrementally as the feature that needs each one gets built** (e.g. QPB/MixMatch/Upsell
          UI when the `add_to_cart` flow node's upsell/replace design gets built, AutoShip UI when
          subscriptions get tested) — not a round-out-everything-now pass. Don't re-ask this; a
          future session should just build the specific field(s) a concrete task needs.
        - **`add_to_cart` / `remove_cart_item` / `reset_cart` CRM flow nodes shipped and
          live-verified (same session, continued).** Design revised from the original sketch during
          planning: dropped the node-reference + `FlowVars`-breadcrumb idea for "replace on upsell"
          in favor of the replace node picking its own offer(s) to remove directly via the same
          search UI as the offer being added — self-contained, no `FlowExecutionContext` changes.
          Per explicit user direction, "replace" is **multi-select**
          (`replacesOfferIds: Guid[]`, not a single id) since one upsell can need to supersede
          several previously-added lines at once. User also asked mid-build for two companion node
          types using the same infrastructure: `remove_cart_item` (removes specific offer(s), no
          add) and `reset_cart` (clears the whole cart, no config). New
          `ICartService.ReplaceItemsAsync`/`RemoveOffersAsync`; three new `INodeHandler`s; new
          shared `OfferPickerField`/`OfferListManagerField` frontend components (search-a-product →
          pick-an-offer, wrapped in an add/remove list manager) reused across both `add_to_cart`
          replace mode and `remove_cart_item`; five backend node-type registration points and the
          designer's own five frontend registration points (`types/designer.ts`'s node-type union +
          `NodeData` + the near-duplicate `ContactConnectionNodeDef` + `NODE_META` + palette, plus a
          *sixth*, separate `types/flow.ts` union used only for agent-facing runtime display — easy
          to miss, caused a TS compile error) all updated.
          **Real bug found and fixed via live verification, not by inspection:** these three new
          node types were never added to `FlowEngine.cs`'s `AutoAdvanceTypes` set, so when reached
          as a flow's entry node the engine executed the handler once, then stopped to "display" the
          node exactly like a `script` node waiting for a Continue click — the next `advance()` call
          re-executed the *same* node's handler a second time before finally moving on, silently
          double-adding (or double-removing) the cart line. This is precisely the failure mode an
          existing code comment on `api_call` already warned about for any node with real side
          effects and no natural "waiting for input" signal; the fix is the one-line addition of all
          three new types to `AutoAdvanceTypes`, making them properly transparent/no-agent-wait like
          `api_call`/`set_variable`. Live-verified after the fix: add (qty+price correct, single
          line), remove (correct offer removed, other left untouched), reset (cart emptied),
          multi-offer replace (both original lines replaced by one new bundle line, no doubling),
          and the OutOfStock failure path (correctly routes to `failed`, no line added). All test
          flows/offers/cart state cleaned up afterward.
        - **Product/offer search bug found and fixed (same session, user-reported live).** User hit
          this immediately while trying `OfferPickerField` for real: searching "buy" for their own
          "TV Special - Buy 2 Get 1 Free" offer returned nothing, because `ProductRepository.SearchAsync`
          only ever matched `Product.Description`/`Sku` — a promo/offer name living only on `Offer.Name`
          was invisible to search. User's own framing: "not obvious to tenants designing the flow."
          Fixed by adding `p.Offers.Any(o => EF.Functions.ILike(o.Name, ...))` to the same OR chain
          (translates to a correlated EXISTS, no `Include` needed) — this is the one shared
          `SearchAsync` method behind both `OfferPickerField` (flow designer) and `CartModal`'s manual
          agent search, so both benefit identically. Placeholder text in both updated from "Search by
          name or SKU…" to "Search by product name, SKU, or offer name…". Live-verified: searching
          "buy" (a word appearing only in the offer name, not on either Widget Mobile product) now
          correctly returns the WIDGET-002 product.
        - **Cart-display staleness bug found and fixed (same session, user-reported live).** User
          built a real flow (`Section → Reset Cart → Execute Flow → Branch → Add to Cart → Section →
          Address`), ran it end-to-end, confirmed the flow correctly took the `added` transition off
          `add_to_cart` — but the cart strip and its modal both showed empty. Root cause was purely
          frontend, not the node/handler: `CartPanel.tsx` fetches `GET .../cart` exactly **once**, in
          a `useEffect` keyed only on `callRecordId` (set once when the flow-preview toolbar mints its
          stub call record) — it never refetches again, and `CartModal` only ever receives `cart` as a
          prop from `CartPanel`, with no independent fetch of its own. A cart-mutating node like
          `add_to_cart`/`remove_cart_item`/`reset_cart` has no display and no event of its own (by
          design — it's meant to be silent/transparent), so nothing ever told the cart display to look
          again. Fixed with a `cartVersion` counter on `useCallStore`, bumped by `FlowPanel.tsx` after
          every `advance()`/`jump()`/`startSession()` HTTP round-trip (every point a node — including
          any silent cart mutation reached via auto-advance — could have run); `CartPanel`'s refetch
          effect now also depends on `cartVersion`. Deliberately not a SignalR push: the agent's own
          browser tab already learns of every node transition via the direct HTTP response to its own
          `advance()` call (the existing `FlowHub`/`ReceiveNodeState` push turned out to be unused by
          the agent's own tab at all — nothing in the frontend even listens for it currently; it
          would only matter for a future supervisor-watching-live-call view), so reusing that
          request/response cycle is simpler and sufficient here. `npm run build` clean.
        - **Still open:** wiring the agent-facing cart's offer picker (`CartModal`) to actually
          filter by the call's client/campaign scope via `IOfferRepository.GetAvailableForContextAsync`
          (built, not called from anywhere yet) — right now scoping is admin-visible metadata only.
- [ ] **Payment Gateway: Authorize.Net.** Workflow from the user's own prior experience running this
      account at the call center: **Auth-only transaction** against Authorize.Net at the point of
      sale, then **separately submit the order via Life Seasons' own Order API** (their backend,
      not Authorize.Net's) — this is a general API integration, and the platform's API Builder /
      General API Definition framework already exists for exactly this shape of work. The user may
      have historical integration notes/data on a home PC from when they worked at the call center
      that could shortcut re-discovering Life Seasons' Order API contract — ask before assuming
      it needs to be rediscovered from scratch.
      - New Authorize.Net-specific payment gateway integration (auth-only transaction type) is
        genuinely new platform capability — nothing in the current architecture actually talks to a
        payment gateway yet (`tf_secure_collect` only captures/encrypts card data via guided DTMF,
        it doesn't authorize a charge).
      - Depends on Tier 1's cart/order item above being functional (needs real cart/order data to
        authorize against and submit).
      - **Progress — Session 161 (2026-09-25): Auth-only + Void shipped and live-verified against a
        real Authorize.Net sandbox account.** Life Seasons Order API submission deliberately deferred
        to a later session — the user has historical CRMPro integration notes on another machine
        (full Postgres DB export + the actual `.vb`/`.designer.vb` script files, API integrations
        stored as base64-encoded objects mapping to CRMPro's class library) that need file-sharing set
        up first to access from this machine.
        - **Design revised mid-planning (user's first-hand client knowledge):** Life Seasons doesn't
          have one Authorize.Net account — it has a **separate merchant account per campaign** (My
          Best Heart / NeuroQ / Joint Food each have their own credentials). Credential resolution is
          now a full **campaign → client → tenant** cascade (same precedence `CustomFieldDefinition`
          already uses), not just client-level, via the *existing* `ITenantCredentialStore` — no new
          schema, just a key-naming convention (`AuthorizeNet:{campaignId}:ApiLoginId`, falling back
          to `{clientId}:` then bare tenant-level). Also added a **void** node this session (agent can
          undo a just-authorized transaction if the caller changes their mind before the order is
          submitted or the call ends) — explicitly requested, unlike a future **capture**
          (auth+capture) transaction type, which is deliberately deferred but not architecturally
          blocked.
        - **Two-layer architecture for real multi-gateway support later:** `IPaymentGatewayClient`
          (one per provider — `AuthorizeNetGatewayClient` is the only one today; a second gateway
          later means implementing this interface and registering it, no other code changes) +
          `IPaymentGatewayClientFactory` (mirrors the existing `ITaxProvider`/`ITaxProviderFactory`
          dispatch-dictionary pattern, except it throws on an unconfigured provider rather than
          silently defaulting — a wrong gateway on a real charge is a much worse failure than tax
          defaulting to flat-rate) + `IPaymentService` (the orchestrator every node handler calls,
          owns all `CallRecord` access: decrypts the `tf_secure_collect` blob, resolves the amount,
          persists a new `PaymentTransaction` audit record — deliberately never stores the PAN/CVV,
          only masked last-4 + card type — and wipes `CallRecord.SensitiveData` with reason
          `api_processed` on a definitive approve/decline, but not on a network/gateway error, so a
          script's retry loop doesn't force the caller through another guided-DTMF capture).
        - New CRM flow nodes `authorize_payment` (reads the already-captured `tf_secure_collect` card
          data by configurable field-key names, amount defaults to the current cart total or a fixed
          override; `approved`/`declined`/`error` transitions) and `void_payment` (no config — voids
          the call's own most recent approved, not-yet-voided transaction; `voided`/`failed`).
        - **Two real bugs found and fixed via live testing, not by inspection:** (1) both new node
          types were initially left out of `FlowEngine.cs`'s `AutoAdvanceTypes` — the exact same class
          of bug fixed for `add_to_cart` last session, caught this time *before* it shipped by
          deliberately checking for it up front. (2) `PaymentTransactionConfiguration` was written but
          never registered in `TenantDbContext.OnModelCreating` — EF Core silently fell back to
          default conventions (wrong table/column names, no constraints) instead of throwing, so the
          bug wasn't caught by the build; found only because the live sandbox test's DB verification
          query against the *intended* `payment_transactions` table name came back "relation does not
          exist." Fixed by registering the configuration and regenerating the migration correctly.
        - **Also found, not a code bug:** the first live sandbox test returned a real Authorize.Net
          "User authentication failed" response — turned out to be the user's own long-dormant
          Authorize.Net developer sandbox account having been closed; a fresh sandbox account's
          credentials worked immediately once entered.
        - Live-verified end-to-end against the real Authorize.Net sandbox: a missing-card-data case
          (clean "error", no gateway call attempted), a real approval (correct `transId`/`authCode`/
          masked last-4/card type persisted, sensitive data wiped), a void of that approval
          (`VoidedAt` set) with a correctly-failing second void attempt (nothing left to void), an
          expired-card case (Authorize.Net itself classifies this as a request-level error rather than
          a bank decline — surfaces as this platform's `error` transition, which is the correct/only
          sensible mapping; the `declined` code path is verified by inspection, not yet exercised
          against a live bank-level decline), and the full node-to-node flow-engine path
          (`authorize_payment` → `void_payment` → `end`) in one real flow session. `dotnet build` and
          `npm run build` both clean throughout.
        - **Zip-field design gap found and fixed (user-caught while reviewing the properties panel,
          before it shipped broken).** User's actual setup used `zipField: "{{flow.billing_address.zip}}"`
          — a variable reference to an earlier address node's output — but the original implementation
          only ever looked up `zipField` as a literal key inside the same `tf_secure_collect` blob as
          card/exp/cvv, so it would have silently resolved to no zip at all. Card/exp/cvv deliberately
          stay blob-only with no variable-template fallback (PCI: never normalize "put the card number
          in an ordinary flow variable" as supported) — but zip isn't sensitive, so it now supports
          either source: `AuthorizePaymentNodeHandler` resolves `zipField` as a `{{...}}` template via
          `IVariableResolver` when it looks like one (new `IPaymentService.AuthorizeAsync` parameter
          `zipOverride`, taking precedence over the blob-key lookup). Properties panel help text
          updated to explain the distinction. Live-verified with a deliberately zip-less
          `tf_secure_collect` blob plus a `{{flow.billing_address.zip}}` reference — confirmed (via a
          one-time non-secret-revealing diagnostic, since Authorize.Net's sandbox AVS codes turned out
          to be static/unreliable for proving this either way) the zip value only ever could have come
          from the variable path, then confirmed a full approval end-to-end once a fresh, non-duplicate
          amount was used (Authorize.Net's own anti-fraud duplicate-transaction check rejected an
          identical repeat of an earlier test amount — expected gateway behavior, not a code bug).
        - **Gap found and fixed (user asked directly): the decline/error reason wasn't reaching the
          agent script at all.** Neither node wrote anything to flow variables — a script author had
          no way to tell the caller *why* a card failed, and (separately) no way to hand the
          Authorize.Net transaction id to a future Order API submission step, even though the user had
          already confirmed that id is the one thing that step needs. Fixed by adding the same
          `outputVariable` convention `api_call` already uses: both nodes now flatten their result
          into flow variables when `outputVariable` is set —
          `{{flow.outputVariable.responseReasonText}}` (the decline/error message),
          `{{flow.outputVariable.gatewayTransactionId}}` (what the Order API step will need),
          `{{flow.outputVariable.status}}`/`.authCode`/`.transactionId`. Live-verified: an
          `authorize_payment` node (expired-card → `error`) wired to a downstream `script` node whose
          content was `"Sorry, that card could not be processed:
          {{flow.payment_result.responseReasonText}}"` correctly resolved to `"...The credit card has
          expired."` in the actual flow session response.
        - **Platform-level gap found while reviewing a real user-built flow (not part of the payment
          gateway item itself, but blocking it): the variable resolver had no arithmetic at all.** A
          "retry up to 3 times" pattern needs a counter, and `{{flow.auth_attempts}} + 1` in a
          `set_variable` assignment just concatenates the literal string `" + 1"` — `Resolve()` only
          ever does `{{...}}` tag substitution, never expression evaluation — so the counter never
          actually incremented and a numeric branch condition (`{{flow.auth_attempts}} >= 3`) silently
          always evaluated false (numeric comparison falls back to an always-false-here string
          compare when either side doesn't parse as a number). User asked for the general fix (not a
          narrower `set_variable`-only increment mode). **Design constraint**: the operator+operand
          had to live *inside* the same `{{...}}` tag as the variable reference
          (`{{flow.auth_attempts + 1}}`), never inferred from a *resolved value* — otherwise ordinary
          hyphenated data (a phone number, a date) could misfire as subtraction. Implemented in
          `VariableResolver.ResolveTag` via a new `ArithmeticPattern` regex requiring whitespace
          around the operator (further reduces any collision with a legitimately hyphenated variable
          name); supports `+ - * /`, treats a missing/non-numeric base as `0` (so a counter needs no
          initializer), formats whole-number results without a trailing `.0`. 9 new unit tests added
          (`VariableResolverArithmeticTests`); full solution test suite (1088 tests across all 4 test
          projects) passes with no regressions. Live-verified: three chained
          `{{flow.auth_attempts + 1}}` increments starting from 0 correctly produced `3`, and
          `{{flow.auth_attempts}} >= 3` correctly evaluated true.
        - **Frontend bug found while reviewing the same real flow (unrelated to payment gateway
          itself): `script` nodes never showed the "Jump to section" dropdown other agent-stopping
          node types (input/phone/email/address) already show.** Root cause: `NodeDisplay.tsx`'s
          shared "just show a Continue button" render block — covering `script` and every
          auto-advancing/silent node type (`branch`, `set_variable`, `api_call`,
          `set_custom_field`/`get_custom_field`, `store_value`/`get_value`,
          `add_to_cart`/`remove_cart_item`/`reset_cart`, `authorize_payment`/`void_payment`) — never
          rendered `{jumpDropdown}` at all, unlike every other node-type block. Backend data was
          never the problem: `FlowEngine.AttachSectionInfo`/`BuildJumpTargets` already populate
          `state.JumpTargets` for *any* current node whenever the flow has sections anywhere in it,
          confirmed live via a fresh section→section→script test flow. `script` is the one type in
          that shared block that's genuinely agent-facing (excluded from `AutoAdvanceTypes` on
          purpose) — the rest only ever reach the agent's screen in the rare case one happens to be a
          flow's own entry node, so the fix is a no-op for them in normal operation but still correct
          to leave in place. One-line fix (`{jumpDropdown}` added to that block). `npm run build`
          clean.
        - **Frontend bug found live by the user: the cart strip kept showing a finished call's cart
          after the flow ended and the agent returned to "Select flow… Start."** Root cause:
          `FlowPanel.tsx`'s flow-preview toolbar mints a stub `CallRecord` on first Start
          (`handleStartSession`) and stores its id in `useCallStore.callRecordId` — but ending a
          session only ever called `removeSession(s.id)` (closing the tab), never anything that
          cleared `callRecordId`. Since `CartPanel` keys its display entirely off `callRecordId`, the
          finished call's cart stayed visible indefinitely, and — worse — `handleStartSession` reuses
          `callRecordId` when already set, so the *next* "Start" click would have silently reused the
          same stub call record and its stale cart instead of starting fresh. Fixed with a new,
          narrowly-scoped `clearCallRecordId()` action on `useCallStore` (clears only the id, unlike
          the broad `reset()`, so it can't clobber a real concurrent call's state) — called when the
          *last* open flow-preview tab ends (`useFlowSessionsStore.getState().sessions.length === 0`)
          and `callStatus` is still `'idle'` (an extra guard against ever firing during a real call).
          `npm run build` clean; needs the user's visual confirmation in the browser since this is
          pure frontend session-lifecycle behavior with no server-side call to verify against.
        - **Deeper gap found while the user asked to verify multi-tab cart behavior: there was no
          per-tab call association at all.** `FlowSessionEntry` (one entry per open flow-preview tab)
          never stored which call it belonged to, and `useCallStore.callRecordId` — what `CartPanel`
          reads — is a single global value with nothing syncing it when the active tab changes.
          With 2+ tabs open (a manual preview alongside a real bridged call delivered via
          `receiveScriptPop`, or a warm-transfer auto-opened script, or several in sequence),
          switching tabs would have left the cart strip stuck on whichever call last happened to set
          that global value, never the tab actually being viewed. Fixed by adding `CallRecordId` to
          the backend `FlowNodeState` (now `required`, populated in `NodeHandlerBase.BuildState` from
          `ctx.CallRecordId` — the compiler's `required` check caught two more construction sites that
          needed the same fix while wiring this up: a test file and a third, previously-missed
          `addSession` call site in `SoftphonePanel.tsx`'s warm-transfer script auto-open), threading
          it through to a new `FlowSessionEntry.callRecordId` field, and a new effect in `FlowPanel.tsx`
          that keeps `useCallStore.callRecordId` synced to whichever tab is actually active. Full
          solution test suite (1088 tests) passes. Live-verified the backend half: two sessions
          started against two different call records, each with its own distinct cart, both correctly
          returned their own `callRecordId` in `FlowNodeState`. The frontend tab-switching sync itself
          needs the user's visual confirmation (open 2+ tabs, switch between them, confirm the cart
          strip follows).

## Tier 2 — Attribution & compliance

How the client measures success and enforces their strict-scripting requirement — needed almost
immediately once calls are live, not deferrable.

- [ ] **Media Agency assignment — Phase A (simple, no FCC/geo yet).** The full media-agency system
      (Tier 4) is large; this phase captures the minimum needed to attribute a call to a media
      source and write it onto the call record from day one:
      - Media agency assignment record per campaign: media station, National vs. Local, plus
        arbitrary custom values (e.g. Cannella's "Product Code" that goes into their daily file).
      - **National assignments** — directly assigned by phone number (DNIS), no zip lookup, exactly
        one assignment active per phone number at a time. Simple to build.
      - **Local assignments** — can have multiple assignments per phone number (the zip-code/nearest-
        station lookup that picks *which* local assignment applies is Tier 4/Phase B; Phase A can
        support the data model + a manually-selected/default local assignment per number without
        the automatic geo lookup).
      - Assignments are **not referenced by id at call time** — the actual assignment's field values
        must be copied/written directly onto the call record at the time of the call, since
        assignments change over time and a call record must stay statically pinned to whatever
        assignment was in effect when that call happened.
      - Station assignments have a start date (usually provided by the media agency).
      - Track station-assignment history per phone number (who was assigned when).
      - Some campaigns also need media type (Print, TV, Radio, etc.) and ad type (Short Form/SF,
        Long Form/LF, Mid Form/MF, PI, Paid, etc.) tracked per assignment.
      - Excel import for media hit schedules is part of the full Tier 4 build (bulk-loading
        assignments) — Phase A can start with manual entry only if that's faster to ship first.
- [ ] **Commissions tracking.** Depends on Tier 1's order/payment existing (commissions are computed
      off real order data) and on the flow engine's variable resolution (already built) for
      script-driven flags:
      - Commission driven by a flat $ amount for a **specific product** added to the order.
      - Commission as a flat $ amount **per order**.
      - Commission as a **% of order subtotal**.
      - Commission **triggered by specific flags set within the CRM script itself** — e.g. different
        commission amounts depending on which "save method" was used on a customer-service
        retention call. This needs a way for a script node (likely `set_variable` or a dedicated
        node) to tag a call/order with a save-method or similar flag that the commission
        calculation reads.
- [ ] **Dashboard — Supervisor tools.** A strictly-scripted CS client will want QC on live calls
      early, not as an afterthought:
      - **Monitor** an agent's live call (listen-only).
      - **Coach** (whisper to the agent without the caller hearing).
      - **Barge in** (join the call, all three parties can hear each other).
      - **Takeover** (supervisor takes the call, agent is removed).
      - A way to **call an agent directly from the dashboard** when that agent is *not* currently on
        a call (direct dial to their extension/softphone from the supervisor view).

## Tier 3 — Operational scale & reporting infrastructure

- [ ] **Data Output / Export Worker — general framework.** A new scheduled worker process (likely
      alongside/extending `ContactConnection.Worker`) that runs export jobs on a schedule. This is
      the reusable engine Tier 4's Cannella exports will plug into, so build it generally, not
      Cannella-specific:
      - **Output formats:** CSV with any configurable delimiter, advanced CSV options (quoting
        style, escaping, etc.), Excel output. Likely more formats over time — design the format
        layer to be extensible.
      - **Secure transmission options** for any file containing sensitive data: FTPS, SFTP, WinZip
        (password/AES) encryption, PGP encryption. (PGP key management itself is scoped under
        Tier 4 since it's shared infrastructure with the media-agency file delivery work — build the
        *encrypt-with-a-given-key* capability here, the *key generation/management UI* there.)
      - **Delivery methods:** FTP, FTPS, Email, or via API — S3, Box, Google Drive, SharePoint, etc.
        (extensible connector model, not a hardcoded list).
      - **Full scheduling options** for when an export runs.
      - **Tenant-configurable data range logic relative to the schedule, with explicit timezone
        handling** — this is the trickiest part and needs to be genuinely robust, not a fixed
        "yesterday" assumption. Examples the user gave directly:
        - "Run this export daily at 1:00 AM Pacific, but it should include data from the previous
          day 12:00 AM–11:59:59 PM *Pacific* time" (schedule timezone == data-range timezone).
        - "Run this export at 10 PM Pacific daily, but report in *Eastern* time, so it should
          contain data from yesterday 9 PM Pacific through today 8:59:59 PM Pacific" — i.e. the
          **data range's own timezone can differ from the schedule's trigger timezone**, and the
          UI needs to make that distinction clear rather than conflating "when it runs" with "what
          time zone the reported data window is measured in."
      - **UI/UX must be easy to understand** despite this complexity — this was called out
        explicitly by the user as a requirement, not just an implementation detail.
- [ ] **Dashboard — KPI widgets + other useful widgets.** Beyond the supervisor call-control tools
      above:
      - A KPI metrics widget with a **settings** panel: group by client or campaign, filter to all
        clients or specific client(s), all campaigns or specific campaign(s).
      - Vet out and propose any other widgets that would be broadly useful (not yet specified by the
        user in detail — a design/discovery pass belongs here before building blind).
      - Per existing project convention ([[feedback_dashboard_realtime_push]] memory), any new
        widget must be wired to real-time SignalR push, never polling.
- [ ] **Agent Helpdesk CMS.** Self-contained, doesn't block revenue, but supports script accuracy
      for a strictly-scripted client:
      - A CMS to build out each helpdesk's content.
      - Helpdesks can be scoped at **client level** or **campaign level**.
      - Agent-facing visibility in the CRM as a button or tab — top or bottom of the screen — opening
        as a **pop-out or slide-out that does not block the view of the CRM script flow** underneath
        it (agent needs the script visible while referencing the helpdesk).

## Tier 4 — Advanced / specialized build-out

The single biggest chunk of work on the whole list — deliberately isolated so it doesn't block
everything above it, but genuinely important given Life Seasons' media-driven business model.

- [ ] **Media Agency — Phase B (FCC station database, geo lookup, station history, Cannella export,
      key management).** Builds on Tier 2's Phase A data model.
      - **FCC station database ingestion** — pull down the FCC's database of all radio/TV stations
        on a recurring basis (source/format/cadence not yet researched); this gives us a
        zip-code-per-station reference.
      - **Local-assignment resolution by geography, without needing the caller's actual zip code:**
        caller's ANI → area code → approximate zip code via the existing zip-codes.com API
        integration (just another endpoint on an integration we already have) → look up the nearest
        *assigned* station (from our own Local assignment records, not just any station in the FCC
        DB) to that approximate zip for the phone number the call came in on → write the resolved
        assignment's full details onto the call record. Explicitly **not** a live/precise zip
        lookup — area-code-to-zip approximation is accepted as good enough.
      - **National assignments stay simple** — no zip/geo lookup at all, directly assigned by
        phone number, exactly one active assignment per number (already covered in Phase A, no new
        work here beyond what Phase A built).
      - **Excel-template import** for bulk-loading media hit schedules (if not already covered by
        Phase A's manual-entry path).
      - **Cannella CORE format export.** Life Seasons is believed to still use Cannella. Cannella
        needs a **daily export file in the CORE format**, and Cannella has **separate file formats
        for Short Form (SF) vs. Long Form (LF)** — generate and deliver as **separate files to
        potentially separate SFTP/FTPS destinations**. Built on top of Tier 3's general Data
        Output/Export Worker (schedule + secure delivery), with Cannella CORE as one concrete output
        format plugged into that engine.
      - **Secure file-transfer key management (FTP side):**
        - Ability to load/manage public keys for SFTP key-pair auth. User's own recommendation:
          WinSCP is the most current/robust package for this (FTPS/SFTP/standard FTP/key pairs) —
          worth evaluating a WinSCP-based (or .NET-native equivalent) delivery component rather than
          hand-rolling SFTP client code.
        - Ability to **generate a private key for a specific client/vendor** and provide the
          matching **public key** to that client/vendor for them to use for file delivery to us
          (i.e. we generate the keypair, we keep the private key, they get the public key to encrypt
          to us / verify us with, depending on direction).
      - **PGP key management:**
        - Encrypt an outbound file using a vendor's or client's **public PGP key**, so they decrypt
          with their own private key (standard PGP delivery flow).
        - Generate a **private PGP key per client/vendor** so we can **decrypt an inbound file**
          they've encrypted to our public key — needed for future file-based data ingestion. The
          user explicitly flagged this as not urgently needed for Cannella specifically, but wants
          it built now anyway ("why not build it out") since the ingestion side will be needed
          eventually regardless of which vendor drives it first.
      - **Future (explicitly not this phase, noted by the user as a past idea, not a current ask):**
        a full vendor portal for media agencies and a separate one for fulfillment agencies. Do not
        build this speculatively — only the assignment/export/key-management pieces above are
        actually being asked for right now.
- [ ] **Team Chat.** Valuable but internal-only — doesn't affect the client's service or reportable
      results, hence lowest priority on this list. Full requirements (expands on the existing
      "planned, not yet built" architecture note in CLAUDE.md's Chat System Architecture section):
      - Should function a lot like Slack.
      - **Tenant-level chat has access to real agent state** — every team member can see the true
        live status of other users (available/on call/away/etc.), not just chat presence.
      - **Agents need immediate, direct chat access to their supervisors** specifically (not just
        general DMs — a clear supervisor-reachability path).
      - **Configuration page reachable from the main tenant dashboard**, gated by custom role access
        (reuse the existing custom-Role system, not a new permission concept) — create/configure
        channels, lock channels, assign channels to specific users. All chat configuration —
        including any in-chat configuration features for users who are roled to configure chat —
        should live on this one config surface, not scattered across the chat UI itself.

---

**Status:** No items fully closed yet. Tier 1's CRM cart/product handling item is well underway —
see its **Progress — Session 160** notes above: out-of-stock status flag, a full manual/searchable
cart, client/campaign-scoped offer management with an admin Products/Offers UI, and the
script-driven `add_to_cart`/`remove_cart_item`/`reset_cart` flow node types, all live-verified.
Tier 1's Payment Gateway item is also well underway — see **Progress — Session 161** above:
Authorize.Net auth-only + void, campaign→client→tenant credential scoping, live-verified against a
real sandbox account. Remaining gaps: wiring `IOfferRepository.GetAvailableForContextAsync` into the
agent-facing `CartModal` (cart item, built/unused), and the Life Seasons Order API submission step
(payment gateway item, blocked on the user setting up file sharing to a machine holding historical
CRMPro integration notes).

**Next up:** Once file sharing to the historical-notes machine is set up, decode the old CRMPro
integration objects to learn Life Seasons' actual Order API contract, then build that submission step
(passing the `PaymentTransaction`'s gateway transaction id — confirmed by the user as the only field
the Order API actually needs from the payment side). Until then: either the `GetAvailableForContextAsync`
wiring above, or a future capture (auth+capture) transaction type per the payment gateway's own
"shape for expansion" design. Remember the incremental-expansion decision above before building out
more Offer/Product admin fields speculatively — only add what a concrete task actually needs.
