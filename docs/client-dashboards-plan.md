# Sprint 3 — KPI builder, client dashboards, records (S181 plan)

Agreed with Stephen, 2026-10-06. Informed by TMS View (Stephen's .NET 8 rebuild of the TMS Live dashboards — X:\GIT\GitLab\TMS
View, architecture in its CLAUDE.md §10–11; design only, nothing copied wholesale). What TMS View had to reconstruct across
two databases (CRM ↔ CDR by GUID) is native here: one call record holds both, and its "call type → metric role" is our
disposition → reporting category.

## Decisions (Stephen, S181)

1. **Formula KPIs use NCalc** — easy for a tenant admin to read and write. (Admin-defined, read-only arithmetic over
   numbers; unrelated to the "no general expressions in agent scripts" rule.)
2. **Client users are a separate kind of account** — no agent record, nothing shared with agents. Their own invite,
   password, MFA. One client user can be given several dashboards, across clients (a media agency managing several).
3. **Recording access is per client user**, decided by the tenant (off by default — e.g. a media agency gets the
   dashboards but no recordings).
4. **Order:** A → B → C, all before Life Seasons goes live.

## Status (S181)

- **A — done:** formula KPIs (`fa4b185`), targets + report dimensions (`d96cb22`), tabbed settings modal (`a165cab`).
- **B — built (`b42a080` backend, `c81cd89` web), awaiting Stephen's browser test:** client users (Admin → Client users),
  "Make client dashboard…" in the builder, client portal at `/client`. Allowed client widgets: KPIs, Service Level, Call
  State by Campaign. Scope = client + optional campaigns; **DNIS narrowing not built yet**. Also closed a pre-existing
  hole: the API now rejects a token whose tenant differs from the request's tenant header.
- **C — next.**

## A. KPI builder upgrade (internal + client dashboards)

- **Formula KPIs.** A custom KPI becomes either a category ratio (today's) or an **NCalc formula** over named variables,
  with a format (number / currency / percent / duration / integer). Variables (one catalog, shown in a click-to-insert
  sidebar, validated as you type):
  - calls: `CallsOffered`, `CallsHandled`, `CallsAbandoned`, `Interactions`, `Opportunities`
  - orders: `Orders`, `NetOrders`, `Declines`, `Units`, `UpsellOrders`
  - revenue: `RevenueGross`, `RevenueExclTax`, `RevenueMerch` (+ `Net…` variants)
  - time (seconds): `TalkSeconds`, `AcwSeconds`, `LoggedInSeconds`
  - every reporting category and every disposition as a count (`Cat_LeadCaptured`, `Disp_TransferredToCS`…)
  - e.g. Save rate = `Disp_Saved / (Disp_Saved + Disp_Cancelled)`; Revenue per handled call = `RevenueExclTax / CallsHandled`
  - divide-by-zero / errors show "—", never crash a dashboard.
- **Targets.** Per KPI on a widget: good / warning thresholds and which direction is better → tiles and cells coloured.
- **Report widget.** Rows grouped by up to two dimensions — agent, campaign, client, disposition, reporting category,
  day, hour, media agency / station, DNIS, a custom field — columns are any KPIs (built-in or custom); options: subtotal
  per first dimension, grand total, "% of total" for counts. Call-handling KPIs show "—" for dimensions calls don't have
  (e.g. disposition). Same push-refresh as every widget.

## B. Client dashboards + client users

- **Client dashboard:** an existing dashboard marked "client dashboard" with a **locked scope** — a client, optionally
  narrowed to campaigns / DNIS. Every widget on it runs inside that scope server-side, whatever its own filters say.
  Only report-type widgets are allowed (KPIs, report, records, service level…) — no supervisor tools.
- **Client users** (`client_users`, tenant schema): name, email, password (BCrypt), MFA, active, **can play recordings**,
  assigned dashboards (many). Invite by email (Resend) → register → sign in at the tenant's own address, separate client
  sign-in; JWT with a client-user role that the agent / admin APIs reject outright.
- **Client portal:** dashboard picker → dashboard view; time zone + default dashboard preferences.
- Audit: client sign-ins, recording plays, exports.

## C. Records widget

Paginated call records inside the dashboard's scope: configurable columns (call, contact, address, media, disposition,
custom fields, order, revenue), column filters, a detail drawer, and recording playback **only for client users allowed
it** (internal users per their permissions).

## Later (not this sprint)

Wall-display carousel (rotate dashboards on a TV), per-agent stats page, scheduled report emails (the export engine can
already deliver files).
