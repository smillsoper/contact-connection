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
        - **Still not started:** the `add_to_cart` CRM flow node type itself (script-driven
          automatic add/replace based on agent input selection) — the upsell/replace design (node
          reference + a `FlowVars` breadcrumb, distinct from `Offer.MixMatchCode`'s group-pricing
          case) is planned but not built. Also still open: wiring the agent-facing cart's offer
          picker (`CartModal`) to actually filter by the call's client/campaign scope via
          `IOfferRepository.GetAvailableForContextAsync` (built, not called from anywhere yet) —
          right now scoping is admin-visible metadata only.
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
cart, and client/campaign-scoped offer management with an admin Products/Offers UI, all
live-verified. Script-driven auto-add (the `add_to_cart` flow node) is the one piece not started.

**Next up:** the `add_to_cart` CRM flow node type (Tier 1, same item) — script-driven automatic
add/replace-on-upsell, per the design noted inline. After that: Tier 1's Payment Gateway item.
Remember the incremental-expansion decision above before building out more Offer/Product admin
fields speculatively — only add what a concrete task actually needs.
