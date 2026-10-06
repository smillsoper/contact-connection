# Dispositions + KPI widgets — plan (S181, Sprint 2 item 3)

Agreed with Stephen, 2026-10-06. The KPI widget needs to know what each call *meant*. Today a disposition is free text
written into the `disposition` custom field by a script (CRMPro had the same gap: call type → disposition, but nothing
tied a disposition to its meaning for KPIs). So dispositions become first-class, tenant-managed records with a
reporting meaning, and the KPIs are built on them.

## Decisions (Stephen, S181)

- **The disposition is what matters at the end of the call.** Call type only steers the script; it stays as it is.
- **Each disposition maps to a reporting category.** Built-in categories plus **tenant-created categories**, which make
  **tenant-defined KPIs** possible (lead capture rate, retention save rate…).
- **KPIs follow the *current* mapping.** Recategorizing a disposition corrects history.
- **KPIs that depend on disposition count *interactions*, not calls.** Each interaction has its own disposition. A call
  with several interactions has a **compound disposition**. Example: a sales agent logs "Transferred to CS", then the CS
  agent on the same record logs "Cancelled Subscription". The call reads **Transferred to CS + Cancelled Subscription**;
  the sales campaign counts one interaction and the CS campaign counts one.
- **Revenue in every form:** gross, net, excluding tax, and merchandise only.
- **KPIs v1:** close rates + orders, revenue per call / order, revenue per agent hour, call handling.
- **Production calls only** (training / sandbox never count).

## Phase 1: dispositions as a real thing

### Model (tenant schema)

- **`disposition_categories`**
  - Fields: id, name, key, description, display order, active, `is_system`.
  - KPI meaning:
    - `sales_opportunity` (bool): in the gross / net close-rate denominator;
    - `excluded_from_kpis` (bool): left out of every KPI, the raw denominator included (test calls).
  - Built-ins, seeded per tenant (they can be renamed but not deleted):

    | Key | Name | Sales opportunity | Excluded from KPIs |
    |---|---|---|---|
    | `sale` | Sale | yes | no |
    | `opportunity_no_sale` | Sales opportunity, no sale | yes | no |
    | `customer_service` | Customer service | no | no |
    | `junk` | Junk / wrong number | no | no |
    | `test` | Test call | no | **yes** |
    | `other` | Other | no | no |

  - Tenant-created categories work the same way, e.g. "Lead captured", "Lead opportunity, not captured".
- **`dispositions`**
  - Fields: id, name, optional export code (for vendor files), category id, active, display order.
  - **Scope:** tenant default, or a client, or a campaign, resolved like custom fields: campaign > client > tenant.
    The same name at a narrower scope overrides the wider one.
  - **`aliases`:** other texts that mean this disposition. Historical wording variants ("Referred to Customer
    Service" / "Transferred to Customer Service") merge into one disposition.
- **`call_interactions`**: gains `disposition_id` (nullable). The existing `disposition` text column stays as the
  name snapshot.
  - **Mapped:** has `disposition_id`.
  - **Unmapped:** has text but no id.
  - **None:** no text.
- **Compound disposition** (call record): derived, not stored. The interactions' dispositions in interaction order,
  joined with " + ". Exposed in the call-record API, Call Records review and the export model (`call.dispositions[]`,
  `call.compound_disposition`, `interaction.disposition_category`).

### Recording

- **Matching:** when an interaction completes, its disposition text is matched to the catalog by name or alias,
  case-insensitive and trimmed, using the campaign's scope. No match → stored as unmapped. This keeps every existing
  script working.
- **Set disposition step** (Flow Designer): picks from the campaign's dispositions in a dropdown, replacing free text.
  It writes the same `disposition` value existing scripts write, plus the id.
- **Question options:** an input step's options can come from the disposition catalog.
- **AI summary:** the AI's allowed dispositions come from the catalog for the campaign. The current script-scanning
  method stays as the fallback when a campaign has none.

### Admin (Admin → Dispositions)

- **Categories:** list / create / rename / KPI flags.
- **Dispositions:** by scope, each with its category, export code and aliases.
- **Unmapped:** the distinct unmapped texts with counts and the campaigns they came from. Each can become a new
  disposition, or an alias of an existing one. Either way the matching interactions are linked at once
  (backfill).
- **First run:** existing dispositions on production calls are offered on the Unmapped list. Nothing is guessed
  automatically.

## Phase 2: the KPI widget

`kpi` dashboard widget.
- **Settings:** time window (today / last N hours / date range), clients and campaigns (all or chosen), group by
  client or campaign, which KPIs to show, revenue basis.
- **Updates:** SignalR push when an interaction completes or an order is submitted, never polling.

### Counting rules (interactions, production only, `excluded_from_kpis` categories left out)

- **Interactions:** completed in the window. **Opportunities:** category is a sales opportunity.
- **Orders:** interactions with `order_submitted_at` (the order API accepted it — see the export notes).
- **Net orders:** orders whose payment was approved and not voided, and not cancelled.
- **Declines:** interactions with a declined authorization and no order.

### Close rates

| KPI | Formula |
|---|---|
| Raw close rate | orders ÷ interactions |
| Gross close rate | orders on opportunity interactions ÷ opportunity interactions |
| Net close rate | net orders on opportunity interactions ÷ opportunity interactions |
| Orders | count |
| Declines | count |

### Revenue (basis chosen in settings; the widget can show several)

| Basis | Definition |
|---|---|
| Gross | order total including tax, shipping, fees |
| Excluding tax | order total minus sales tax (matches the Cannella REVENUE row) |
| Merchandise only | product subtotal after discounts |
| Net | any basis above, counting net orders only |

| KPI | Formula |
|---|---|
| Revenue | sum |
| Revenue per call | revenue ÷ interactions |
| Revenue per opportunity | revenue ÷ opportunity interactions |
| Average order value | revenue ÷ orders |
| Upsell take rate | orders with an upsell line ÷ orders |
| Units per order | units ÷ orders |

### CRM + telephony

| KPI | Formula |
|---|---|
| Revenue per agent hour | revenue ÷ agents' logged-in hours in the window (agent state history) |
| Talk-time variant | revenue ÷ talk hours |

### Call handling

- Offered, handled, abandoned, service level (same threshold logic as the Service Level widget).
- AHT = talk + hold + after-call work.
- Talk / hold / ACW averages.

### Data quality

- **Dispositioned as a sale, but no order submitted:** the CRMPro-era mismatch, now visible.
- **Unmapped interactions:** counted and flagged, with a link to the Unmapped list.

### Custom KPIs (tenant-defined)

- **Definition:** name + numerator categories + denominator categories (blank = all interactions) + format (percent
  or count).
- **Example:** Lead capture rate = Lead captured ÷ (Lead captured + Lead opportunity, not captured).
- **Display:** shown in the widget's KPI picker beside the built-in KPIs.

## Status (S181)

- **Done:** phase 1 (catalog, categories, aliases, scope, Unmapped + backfill, sync on every write, compound disposition),
  designer picker + catalog question options, AI catalog, Call Records filters / badges, commissions per disposition /
  category, KPI widget (close rates, revenue in every basis + net, per call / opportunity / order / agent hour / talk
  hour, upsell, units, call handling, AHT, data quality) and tenant-defined custom KPIs.
- **Not yet:** hold time (no hold state is recorded yet), cancellations after the fact in net orders (needs client
  fulfillment / cancellation data), recording retention by disposition (Campaign "record_always_retain_by_disposition" is
  offered but never implemented — needs Stephen's rules).

## Build order

1. Phase 1 model + migration + seeding built-in categories, name/alias matching on completion, Admin → Dispositions
   (categories, dispositions, Unmapped + backfill), compound disposition in the API / Call Records / export model.
2. Set disposition step + catalog-sourced question options + AI catalog.
3. KPI widget: queries, settings, push, custom KPIs.
